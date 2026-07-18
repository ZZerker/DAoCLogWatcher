using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace DAoCLogWatcher.UI.Services;

/// <summary>
/// On a KDE Plasma (KWin) Wayland session, makes sure a KWin window rule exists that raises the
/// overlay into the on-screen-display stacking layer. Avalonia has no Wayland backend, so the overlay
/// is an XWayland window that KWin otherwise stacks below a focused fullscreen game; the rule lifts
/// just the overlay above it while leaving the game untouched.
///
/// Idempotent and best-effort: no-op on Windows, on X11, and on non-KDE desktops. Works inside the
/// Flatpak sandbox too: the host home is exposed via --filesystem=home, so the rule is written to the
/// host's ~/.config/kwinrulesrc (XDG_CONFIG_HOME is sandbox-redirected and KWin never reads it). Never
/// modifies unrelated rules, and any failure is logged rather than thrown so it can't break startup.
/// </summary>
internal static class KWinOverlayRuleInstaller
{
	// Must stay in sync with OverlayWindow's Title and the WmClass set in Program.cs.
	private const string OverlayTitle = "DAoC Overlay";
	private const string WmClass = "io.github.zzerker.DAoCLogWatcher";

	// True when a KWin overlay rule should be written: a KDE Plasma Wayland session (Flatpak included, as
	// we target the host ~/.config via RulesFilePath) and no matching rule is present yet. Read-only:
	// never writes anything and never throws — any failure is logged and treated as "not needed".
	public static bool IsNeeded()
	{
		try
		{
			if(!IsKdeWaylandSession())
			{
				return false;
			}

			var path = RulesFilePath();
			var existing = File.Exists(path)?File.ReadAllText(path):string.Empty;

			// TryBuildWithOverlayRule returns true when the rule had to be appended, i.e. it is not present.
			return TryBuildWithOverlayRule(existing, () => Guid.NewGuid().ToString(), out _);
		}
		catch(Exception ex)
		{
			AppLog.Exception("KWinOverlayRuleInstaller.IsNeeded", ex);
			return false;
		}
	}

	// Writes the overlay rule into kwinrulesrc and live-reloads KWin. Idempotent and best-effort:
	// no-op when a matching rule is already present, any failure is logged rather than thrown.
	public static void Install()
	{
		try
		{
			var path = RulesFilePath();
			var existing = File.Exists(path)?File.ReadAllText(path):string.Empty;

			if(!TryBuildWithOverlayRule(existing, () => Guid.NewGuid().ToString(), out var updated))
			{
				return; // a matching rule is already present (ours or a manually-imported one)
			}

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, updated);
			ReloadKWin();
			AppLog.Info("KWinOverlayRuleInstaller", "Installed KWin window rule to keep the overlay above fullscreen games.");
		}
		catch(Exception ex)
		{
			AppLog.Exception("KWinOverlayRuleInstaller.Install", ex);
		}
	}

	internal static bool IsKdeWaylandSession()
	{
		if(!OperatingSystem.IsLinux())
		{
			return false;
		}

		var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
		if(!string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
		var kdeFull = Environment.GetEnvironmentVariable("KDE_FULL_SESSION") ?? string.Empty;
		return desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase)
				||string.Equals(kdeFull, "true", StringComparison.OrdinalIgnoreCase);
	}

	// Pure/testable: given the current kwinrulesrc contents, returns true + the rewritten contents when
	// an overlay rule had to be appended; false (contents unchanged) when one is already present.
	internal static bool TryBuildWithOverlayRule(string existing, Func<string> uuidFactory, out string updated)
	{
		var groups = ParseGroups(existing);

		if(groups.Any(IsOverlayRule))
		{
			updated = existing;
			return false;
		}

		var uuid = uuidFactory();
		var rule = new Group(uuid);
		rule.Set("Description", "DAoC Log Watcher overlay - keep above fullscreen (KDE Wayland)");
		rule.Set("layer", "osd");
		rule.Set("layerrule", "2");
		rule.Set("title", OverlayTitle);
		rule.Set("titlematch", "1");
		rule.Set("wmclass", WmClass);
		rule.Set("wmclassmatch", "1");

		var general = groups.FirstOrDefault(g => g.Name == "General");
		if(general == null)
		{
			general = new Group("General");
			groups.Insert(0, general);
		}

		var rules = (general.Get("rules") ?? string.Empty)
				.Split(',', StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
				.ToList();
		rules.Add(uuid);
		general.Set("rules", string.Join(',', rules));
		general.Set("count", rules.Count.ToString());

		groups.Add(rule);
		updated = Serialize(groups);
		return true;
	}

	private static bool IsOverlayRule(Group g)
	{
		return string.Equals(g.Get("wmclass"), WmClass, StringComparison.Ordinal)
				&&string.Equals(g.Get("layer"), "osd", StringComparison.Ordinal);
	}

	private static string RulesFilePath()
	{
		// Inside the Flatpak sandbox, XDG_CONFIG_HOME is redirected into the per-app tree
		// (~/.var/app/<id>/config), which KWin never reads. HOME still points at the real host home
		// (exposed via --filesystem=home), so write to the host's ~/.config/kwinrulesrc directly.
		var inFlatpak = Environment.GetEnvironmentVariable("FLATPAK_ID") != null;
		var configHome = inFlatpak?null:Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
		if(string.IsNullOrEmpty(configHome))
		{
			configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
		}

		return Path.Combine(configHome, "kwinrulesrc");
	}

	private static void ReloadKWin()
	{
		try
		{
			var psi = new ProcessStartInfo("dbus-send",
			                               "--session --type=method_call --dest=org.kde.KWin /KWin org.kde.KWin.reconfigure")
			          {
					          UseShellExecute = false,
					          RedirectStandardOutput = true,
					          RedirectStandardError = true,
			          };
			using var proc = Process.Start(psi);
			proc?.WaitForExit(3000);
		}
		catch(Exception ex)
		{
			// Best-effort: the rule still takes effect on the next KWin restart / login.
			AppLog.Warning("KWinOverlayRuleInstaller", $"Could not reload KWin config live: {ex.Message}");
		}
	}

	// ---- minimal KConfig (.ini-style) model, order-preserving ----

	private static List<Group> ParseGroups(string content)
	{
		var groups = new List<Group>();
		Group? current = null;

		foreach(var raw in content.Split('\n'))
		{
			var line = raw.Trim();
			if(line.Length == 0)
			{
				continue;
			}

			if(line.StartsWith('[')&&line.EndsWith(']'))
			{
				current = new Group(line[1..^1]);
				groups.Add(current);
				continue;
			}

			var idx = line.IndexOf('=');
			if(idx <= 0||current == null)
			{
				continue;
			}

			current.Set(line[..idx], line[(idx + 1)..]);
		}

		return groups;
	}

	private static string Serialize(List<Group> groups)
	{
		var sb = new StringBuilder();
		for(var i = 0; i < groups.Count; i++)
		{
			if(i > 0)
			{
				sb.Append('\n');
			}

			sb.Append('[').Append(groups[i].Name).Append("]\n");
			foreach(var (key, value) in groups[i].Entries)
			{
				sb.Append(key).Append('=').Append(value).Append('\n');
			}
		}

		return sb.ToString();
	}

	private sealed class Group
	{
		public Group(string name)
		{
			this.Name = name;
		}

		public string Name { get; }

		public List<(string Key, string Value)> Entries { get; } = [];

		public string? Get(string key)
		{
			foreach(var e in this.Entries)
			{
				if(e.Key == key)
				{
					return e.Value;
				}
			}

			return null;
		}

		public void Set(string key, string value)
		{
			for(var i = 0; i < this.Entries.Count; i++)
			{
				if(this.Entries[i].Key == key)
				{
					this.Entries[i] = (key, value);
					return;
				}
			}

			this.Entries.Add((key, value));
		}
	}
}
