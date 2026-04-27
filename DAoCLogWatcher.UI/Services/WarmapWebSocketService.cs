using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DAoCLogWatcher.UI.Services;

public sealed record WarmapKeepState(int Realm, bool InCombat);

public sealed record WarmapActivityEntry(int Zone, int X, int Y, int Size, int Realm);

public sealed class WarmapWebSocketService: IDisposable
{
	private const string WsUri = "wss://ws.eden-daoc.net:60005";
	private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan PingIdleThreshold = TimeSpan.FromSeconds(25);
	private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
	private const long ActivityExpiryMs = 45 * 1000;

	private readonly Dictionary<int, string> idToName = new();
	private readonly Dictionary<string, WarmapKeepState> states = new();
	private readonly Dictionary<string, DateTime> combatStartTimes = new();
	private readonly Dictionary<string, (WarmapActivityEntry Entry, long LastMs)> fights = new();
	private readonly Dictionary<string, (WarmapActivityEntry Entry, long LastMs)> groups = new();
	private readonly Lock stateLock = new();
	private CancellationTokenSource? cts;
	private Timer? expiryTimer;

	public event EventHandler? KeepsUpdated;
	public event EventHandler? FightsUpdated;

	public void Start()
	{
		this.cts = new CancellationTokenSource();
		this.expiryTimer = new Timer(_ => this.ExpireStaleActivity(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
		_ = this.RunAsync(this.cts.Token);
	}

	public IReadOnlyDictionary<string, WarmapKeepState> GetSnapshot()
	{
		lock(this.stateLock)
		{
			return new Dictionary<string, WarmapKeepState>(this.states);
		}
	}

	public IReadOnlyList<WarmapActivityEntry> GetFightsSnapshot()
	{
		lock(this.stateLock)
		{
			return this.fights.Values.Select(v => v.Entry).ToList();
		}
	}

	public IReadOnlyList<WarmapActivityEntry> GetGroupsSnapshot()
	{
		lock(this.stateLock)
		{
			return this.groups.Values.Select(v => v.Entry).ToList();
		}
	}

	public IReadOnlyDictionary<string, DateTime> GetCombatStartSnapshot()
	{
		lock(this.stateLock)
		{
			return new Dictionary<string, DateTime>(this.combatStartTimes);
		}
	}

	private async Task RunAsync(CancellationToken ct)
	{
		while(!ct.IsCancellationRequested)
		{
			try
			{
				await this.RunConnectionAsync(ct);
			}
			catch(OperationCanceledException)
			{
				break;
			}
			catch
			{
				// connection lost -- wait before reconnecting
			}

			if(!ct.IsCancellationRequested)
			{
				await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false);
			}
		}
	}

	private async Task RunConnectionAsync(CancellationToken ct)
	{
		using var ws = new ClientWebSocket();
		await ws.ConnectAsync(new Uri(WsUri), ct);

		var startBytes = Encoding.UTF8.GetBytes("start");
		await ws.SendAsync(startBytes, WebSocketMessageType.Text, true, ct);

		var lastDataTime = DateTime.UtcNow;
		using var pingTimer = new PeriodicTimer(PingInterval);
		var receiveTask = this.ReceiveLoopAsync(ws, ct, () => lastDataTime = DateTime.UtcNow);

		while(!ct.IsCancellationRequested&&ws.State == WebSocketState.Open)
		{
			if(!await pingTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
			{
				break;
			}

			if(receiveTask.IsCompleted)
			{
				break;
			}

			if(DateTime.UtcNow - lastDataTime > PingIdleThreshold)
			{
				var pingBytes = Encoding.UTF8.GetBytes("{\"t\":\"ping\"}");
				await ws.SendAsync(pingBytes, WebSocketMessageType.Text, true, ct);
			}
		}

		await receiveTask.ConfigureAwait(false);
	}

	private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct, Action onData)
	{
		var chunk = new byte[65536];
		using var ms = new MemoryStream();

		while(ws.State == WebSocketState.Open&&!ct.IsCancellationRequested)
		{
			ms.SetLength(0);
			WebSocketReceiveResult result;

			do
			{
				result = await ws.ReceiveAsync(chunk, ct);
				if(result.MessageType == WebSocketMessageType.Close)
				{
					return;
				}

				ms.Write(chunk, 0, result.Count);
			} while(!result.EndOfMessage);

			onData();
			var json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
			this.ProcessMessage(json);
		}
	}

	private void ProcessMessage(string json)
	{
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(json);
		}
		catch(JsonException)
		{
			return;
		}

		using(doc)
		{
			var root = doc.RootElement;

			if(root.TryGetProperty("keeps", out var keepsEl))
			{
				this.ProcessKeepsSnapshot(keepsEl);
			}
			else if(root.TryGetProperty("keep", out var keepEl))
			{
				this.ProcessKeepUpdate(keepEl);
			}
			else if(root.TryGetProperty("warmap", out var warmapEl))
			{
				this.ProcessWarmapMessage(warmapEl);
			}
		}
	}

	private void ProcessKeepsSnapshot(JsonElement keepsEl)
	{
		lock(this.stateLock)
		{
			foreach(var kv in keepsEl.EnumerateObject())
			{
				if(!int.TryParse(kv.Name, out var id))
				{
					continue;
				}

				var k = kv.Value;
				var name = k.TryGetProperty("n", out var nEl)?Normalize(nEl.GetString() ?? ""):"";
				if(string.IsNullOrEmpty(name))
				{
					continue;
				}

				var rlm = k.TryGetProperty("rlm", out var rlmEl)?rlmEl.GetInt32():0;
				var combat = k.TryGetProperty("c", out var cEl)&&cEl.GetInt32() != 0;

				this.idToName[id] = name;
				this.states[name] = new WarmapKeepState(rlm, combat);
				if(combat && !this.combatStartTimes.ContainsKey(name))
				{
					this.combatStartTimes[name] = DateTime.UtcNow;
				}
				else if(!combat)
				{
					this.combatStartTimes.Remove(name);
				}
			}
		}

		this.KeepsUpdated?.Invoke(this, EventArgs.Empty);
	}

	private void ProcessKeepUpdate(JsonElement keepEl)
	{
		if(!keepEl.TryGetProperty("id", out var idEl)||!idEl.TryGetInt32(out var id))
		{
			return;
		}

		lock(this.stateLock)
		{
			if(!this.idToName.TryGetValue(id, out var name))
			{
				return;
			}

			var existing = this.states.GetValueOrDefault(name, new WarmapKeepState(0, false));
			var rlm = keepEl.TryGetProperty("r", out var rEl)?rEl.GetInt32():existing.Realm;
			var combat = keepEl.TryGetProperty("c", out var cEl)?cEl.GetInt32() != 0:existing.InCombat;
			this.states[name] = new WarmapKeepState(rlm, combat);
			if(combat && !existing.InCombat && !this.combatStartTimes.ContainsKey(name))
			{
				this.combatStartTimes[name] = DateTime.UtcNow;
			}
			else if(!combat)
			{
				this.combatStartTimes.Remove(name);
			}
		}

		this.KeepsUpdated?.Invoke(this, EventArgs.Empty);
	}

	private void ProcessWarmapMessage(JsonElement warmapEl)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var changed = false;

		lock(this.stateLock)
		{
			if(warmapEl.TryGetProperty("f", out var fightsEl))
			{
				foreach(var item in fightsEl.EnumerateArray())
				{
					if(!item.TryGetProperty("z", out var zEl)||!zEl.TryGetInt32(out var z))
					{
						continue;
					}

					if(!item.TryGetProperty("x", out var xEl)||!xEl.TryGetInt32(out var x))
					{
						continue;
					}

					if(!item.TryGetProperty("y", out var yEl)||!yEl.TryGetInt32(out var y))
					{
						continue;
					}

					var s = item.TryGetProperty("s", out var sEl)?sEl.GetInt32():1;
					var c = item.TryGetProperty("c", out var cEl)?cEl.GetInt32():0;
					var key = $"{z}_{x}_{y}";
					this.fights[key] = (new WarmapActivityEntry(z, x, y, Math.Clamp(s, 1, 3), c), now);
					changed = true;
				}
			}

			if(warmapEl.TryGetProperty("g", out var groupsEl))
			{
				foreach(var item in groupsEl.EnumerateArray())
				{
					if(!item.TryGetProperty("z", out var zEl)||!zEl.TryGetInt32(out var z))
					{
						continue;
					}

					if(!item.TryGetProperty("x", out var xEl)||!xEl.TryGetInt32(out var x))
					{
						continue;
					}

					if(!item.TryGetProperty("y", out var yEl)||!yEl.TryGetInt32(out var y))
					{
						continue;
					}

					var s = item.TryGetProperty("s", out var sEl)?sEl.GetInt32():1;
					if(s == 0)
					{
						s = 1;
					}

					var c = item.TryGetProperty("c", out var cEl)?cEl.GetInt32():0;
					var key = $"{z}_{x}_{y}";
					this.groups[key] = (new WarmapActivityEntry(z, x, y, Math.Clamp(s, 1, 3), c), now);
					changed = true;
				}
			}
		}

		if(changed)
		{
			this.FightsUpdated?.Invoke(this, EventArgs.Empty);
		}
	}

	private void ExpireStaleActivity()
	{
		var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ActivityExpiryMs;
		var changed = false;

		lock(this.stateLock)
		{
			foreach(var key in this.fights.Keys.ToList())
			{
				if(this.fights[key].LastMs < cutoff)
				{
					this.fights.Remove(key);
					changed = true;
				}
			}

			foreach(var key in this.groups.Keys.ToList())
			{
				if(this.groups[key].LastMs < cutoff)
				{
					this.groups.Remove(key);
					changed = true;
				}
			}
		}

		if(changed)
		{
			this.FightsUpdated?.Invoke(this, EventArgs.Empty);
		}
	}

	private static string Normalize(string s)
	{
		return s.Replace('’', '\'').Replace('‘', '\'').Trim();
	}

	public void Dispose()
	{
		this.cts?.Cancel();
		this.cts?.Dispose();
		this.expiryTimer?.Dispose();
	}
}
