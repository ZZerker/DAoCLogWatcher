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

/// <summary>
/// One of the six relics, from the <c>relics</c> message. <paramref name="Type"/> is 0 = Strength,
/// 1 = Power (Eden's <c>t</c>); <paramref name="OriginRealm"/> is the realm it belongs to and
/// <paramref name="OwnerRealm"/> the realm currently holding it (1 Albion, 2 Midgard, 3 Hibernia).
/// </summary>
public sealed record WarmapRelic(int Id, int OriginRealm, int Type, int OwnerRealm, int Site)
{
	/// <summary>Being carried by a player right now: Eden reports site 0 while a relic is in transit.</summary>
	public bool IsMoving => this.Site == 0;

	/// <summary>Sitting on its own realm's relic keep pad. Eden encodes those three pads as sites 1, 2 and 3.</summary>
	public bool IsAtHomeSite => this.Site >= 1&&this.Site <= 3;

	/// <summary>The keep the relic sits in, or 0 when it is moving or at home.</summary>
	public int KeepId => this.IsMoving||this.IsAtHomeSite?0:this.Site;
}

/// <summary>
/// A relic pad from the <c>relicpads</c> message: the six home relic keeps (<paramref name="IsHomePad"/>)
/// plus every regular frontier keep, which can host a captured relic. <paramref name="RelicId"/> is 0
/// when the pad is empty, and <paramref name="RelicType"/> is -1 then. Coordinates are region-163 game
/// units, not zone-local.
/// </summary>
public sealed record WarmapRelicPad(
		string Name,
		int KeepId,
		int GameX,
		int GameY,
		int OwnerRealm,
		int OriginRealm,
		int PadType,
		bool IsHomePad,
		int RelicId,
		int RelicType);

/// <summary>
/// A relic resolved onto the pad it currently sits on — what the map actually draws.
/// <paramref name="IsAtHome"/> is true only when the relic rests on its own realm's home pad.
/// </summary>
public sealed record WarmapRelicPlacement(
		int RelicId,
		string DisplayName,
		int Type,
		int OriginRealm,
		int OwnerRealm,
		string PadName,
		int PadKeepId,
		bool IsHomePad,
		bool IsAtHome,
		int GameX,
		int GameY,
		bool IsMoving)
{
	/// <summary>"Strength" or "Power".</summary>
	public string TypeName => this.Type == 0?"Strength":"Power";
}

/// <summary>
/// A live world event from Eden's campaign system (the <c>events</c> message, which Eden's own
/// warmap.js has no handler for). <paramref name="Source"/> partitions cleanly: <c>"ws"</c> are
/// campaign spawns (Behemoth, Scout, SupplyDrop, CampExtinction) which never expire, <c>"gm"</c>
/// are <c>GeneratedMissionStatic*</c> missions which usually carry a ~21 min deadline.
/// An event vanishing from the snapshot is its removal — there is no terminal State.
/// <paramref name="X"/>/<paramref name="Y"/> are zone-local (0-65535), like warmap fights.
/// </summary>
public sealed record WarmapEvent(string Id, string Type, string Size, string State, string Source, int Zone, int X, int Y, long EndsAtMs)
{
	/// <summary>True for campaign/world spawns; false for generated missions.</summary>
	public bool IsCampaignSpawn => string.Equals(this.Source, "ws", StringComparison.Ordinal);

	/// <summary>False when the event has no deadline (all "ws" spawns, and some missions).</summary>
	public bool HasDeadline => this.EndsAtMs > 0;

	/// <summary>Still announced but not yet running — a transient state, typically only one tick.</summary>
	public bool IsPending => string.Equals(this.State, "pending", StringComparison.OrdinalIgnoreCase);

	/// <summary>Time left, or null when the event has no deadline. Never negative.</summary>
	public TimeSpan? TimeRemaining
	{
		get
		{
			if(!this.HasDeadline)
			{
				return null;
			}

			var ms = this.EndsAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			return ms <= 0?TimeSpan.Zero:TimeSpan.FromMilliseconds(ms);
		}
	}

	/// <summary>"GeneratedMissionStaticTreasureHunt" -> "Treasure Hunt"; "SupplyDrop" -> "Supply Drop".</summary>
	public string DisplayName => FormatTypeName(this.Type);

	private static string FormatTypeName(string type)
	{
		const string missionPrefix = "GeneratedMissionStatic";
		var name = type.StartsWith(missionPrefix, StringComparison.Ordinal)?type[missionPrefix.Length..]:type;

		var sb = new StringBuilder(name.Length + 4);
		for(var i = 0; i < name.Length; i++)
		{
			if(i > 0&&char.IsUpper(name[i])&&!char.IsUpper(name[i - 1]))
			{
				sb.Append(' ');
			}

			sb.Append(name[i]);
		}

		return sb.ToString();
	}
}

/// <summary>
/// Campaign rotation phase (the <c>rotation</c> message). <paramref name="Current"/> cycles
/// 1 -> 2 -> 0 with phases of roughly 18-23 minutes, and it gates which mission types exist:
/// phase 0 runs PvpTeleporter/Koth/MurderBall, phases 1 and 2 run EspionageStart/Merchant instead.
/// </summary>
public sealed record WarmapRotation(int Current, int Of, string Next, long NextAtMs, string NextType)
{
	/// <summary>Time until the next phase change, or null once it has passed.</summary>
	public TimeSpan? TimeToNext
	{
		get
		{
			if(this.NextAtMs <= 0)
			{
				return null;
			}

			var ms = this.NextAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			return ms <= 0?TimeSpan.Zero:TimeSpan.FromMilliseconds(ms);
		}
	}
}

public sealed class WarmapWebSocketService: IDisposable
{
	private const string WsUri = "wss://ws.eden-daoc.net:60005";
	private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan PingIdleThreshold = TimeSpan.FromSeconds(25);
	private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
	// Matches Eden's own warmap client (warmap.js: fight.last + 2 * 60 * 1000), so markers
	// linger on our minimap exactly as long as they do on eden-daoc.net/warmap.
	private const long ActivityExpiryMs = 2 * 60 * 1000;

	private readonly Dictionary<int, string> idToName = new();
	private readonly Dictionary<string, WarmapKeepState> states = new();
	private readonly Dictionary<string, DateTime> combatStartTimes = new();
	private readonly Dictionary<string, (WarmapActivityEntry Entry, long LastMs)> fights = new();
	private readonly Dictionary<string, (WarmapActivityEntry Entry, long LastMs)> groups = new();
	// Both arrive as full snapshots on connect. Eden has no incremental relic message, so a
	// relic move is only seen on the next snapshot (i.e. after a reconnect) -- see RelicsUpdated.
	private Dictionary<int, WarmapRelic> relics = new();
	private IReadOnlyList<WarmapRelicPad> relicPads = [];
	// The "events" message is a FULL snapshot every ~10s, so this list is replaced wholesale
	// rather than merged+expired like fights/groups. An event vanishing from the snapshot IS
	// its removal — there is no terminal State to wait for (only pending -> active is ever sent).
	private IReadOnlyList<WarmapEvent> events = [];
	private WarmapRotation? rotation;
	private readonly Lock stateLock = new();
	private CancellationTokenSource? cts;
	private Timer? expiryTimer;

	public event EventHandler? KeepsUpdated;

	public event EventHandler? FightsUpdated;

	/// <summary>Raised when a new campaign/mission snapshot or rotation phase arrives.</summary>
	public event EventHandler? EventsUpdated;

	/// <summary>Raised when a relic or relic-pad snapshot arrives.</summary>
	public event EventHandler? RelicsUpdated;

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

	/// <summary>Currently live campaign spawns and missions. Typically 11-17 entries.</summary>
	public IReadOnlyList<WarmapEvent> GetEventsSnapshot()
	{
		lock(this.stateLock)
		{
			return this.events;
		}
	}

	/// <summary>The six relics with their current owning realm. Empty before the first snapshot.</summary>
	public IReadOnlyList<WarmapRelic> GetRelicsSnapshot()
	{
		lock(this.stateLock)
		{
			return this.relics.Values.ToList();
		}
	}

	/// <summary>Every relic pad, home and keep, whether occupied or not.</summary>
	public IReadOnlyList<WarmapRelicPad> GetRelicPadsSnapshot()
	{
		lock(this.stateLock)
		{
			return this.relicPads;
		}
	}

	/// <summary>
	/// The relics resolved onto the pads they sit on. A relic being carried by a player sits on no
	/// pad and is therefore absent from the result.
	/// </summary>
	public IReadOnlyList<WarmapRelicPlacement> GetRelicPlacements()
	{
		Dictionary<int, WarmapRelic> relicsCopy;
		IReadOnlyList<WarmapRelicPad> padsCopy;

		lock(this.stateLock)
		{
			relicsCopy = new Dictionary<int, WarmapRelic>(this.relics);
			padsCopy = this.relicPads;
		}

		var placements = new List<WarmapRelicPlacement>();

		// Driven by the relic records, not by the pad list: only the relics are kept current by the
		// incremental "relic" message, so a pad's own RelicId goes stale as soon as one is captured.
		// The pads are used purely for names and coordinates, which never change.
		foreach(var relic in relicsCopy.Values)
		{
			var pad = FindPad(padsCopy, relic);
			var atHome = relic.IsAtHomeSite&&relic.Site == relic.OriginRealm;

			placements.Add(new WarmapRelicPlacement(relic.Id,
			                                        RelicName(relic.OriginRealm, relic.Type),
			                                        relic.Type,
			                                        relic.OriginRealm,
			                                        relic.OwnerRealm,
			                                        pad?.Name ?? (relic.IsMoving?"In transit":"Unknown"),
			                                        pad?.KeepId ?? 0,
			                                        pad?.IsHomePad ?? false,
			                                        atHome,
			                                        pad?.GameX ?? 0,
			                                        pad?.GameY ?? 0,
			                                        relic.IsMoving));
		}

		return placements;
	}

	/// <summary>
	/// Resolves a relic's site to the pad it sits on. Eden encodes the site as 0 for a relic being
	/// carried, 1 to 3 for the home relic keep of that realm, and otherwise the id of the keep holding it.
	/// </summary>
	private static WarmapRelicPad? FindPad(IReadOnlyList<WarmapRelicPad> pads, WarmapRelic relic)
	{
		if(relic.IsMoving)
		{
			return null;
		}

		if(relic.IsAtHomeSite)
		{
			return pads.FirstOrDefault(p => p.IsHomePad&&p.OriginRealm == relic.Site&&p.PadType == relic.Type);
		}

		return pads.FirstOrDefault(p => p.KeepId == relic.KeepId);
	}

	/// <summary>Canonical relic names — the payload carries only realm + type.</summary>
	private static string RelicName(int originRealm, int type)
	{
		return (originRealm, type) switch
		       {
				       (1, 0) => "Scabbard of Excalibur",
				       (1, 1) => "Merlin's Staff",
				       (2, 0) => "Thor's Hammer",
				       (2, 1) => "Horn of Valhalla",
				       (3, 0) => "Lug's Spear of Lightning",
				       (3, 1) => "Cauldron of Dagda",
				       _ => type == 0?"Strength Relic":"Power Relic"
		       };
	}

	/// <summary>Current campaign rotation phase, or null before the first message arrives.</summary>
	public WarmapRotation? GetRotation()
	{
		lock(this.stateLock)
		{
			return this.rotation;
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

	/// <summary>Internal for tests: feeds one raw server message through the parsers.</summary>
	internal void ProcessMessage(string json)
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

			if(root.TryGetProperty("relics", out var relicsEl))
			{
				this.ProcessRelicsSnapshot(relicsEl);
				this.RelicsUpdated?.Invoke(this, EventArgs.Empty);
			}

			if(root.TryGetProperty("relic", out var relicEl))
			{
				this.ProcessRelicUpdate(relicEl);
				this.RelicsUpdated?.Invoke(this, EventArgs.Empty);
			}

			if(root.TryGetProperty("relicpads", out var padsEl))
			{
				this.ProcessRelicPadsSnapshot(padsEl);
				this.RelicsUpdated?.Invoke(this, EventArgs.Empty);
			}

			// Checked independently of the chain above: Eden's server sends these as their own
			// messages, but tests each key separately in warmap.js rather than as else-if.
			var eventsChanged = false;

			if(root.TryGetProperty("events", out var eventsEl))
			{
				this.ProcessEventsMessage(eventsEl);
				eventsChanged = true;
			}

			if(root.TryGetProperty("rotation", out var rotationEl))
			{
				this.ProcessRotationMessage(rotationEl);
				eventsChanged = true;
			}

			if(eventsChanged)
			{
				this.EventsUpdated?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	/// <summary>
	/// Replaces the whole event list — the payload is a full snapshot, not a delta. Also carries
	/// <c>df</c>, the realm currently holding Darkness Falls.
	/// </summary>
	private void ProcessEventsMessage(JsonElement eventsEl)
	{
		var parsed = new List<WarmapEvent>();

		if(eventsEl.TryGetProperty("e", out var listEl)&&listEl.ValueKind == JsonValueKind.Array)
		{
			foreach(var item in listEl.EnumerateArray())
			{
				var id = item.TryGetProperty("Id", out var idEl)?idEl.GetString():null;
				var type = item.TryGetProperty("Type", out var typeEl)?typeEl.GetString():null;
				if(string.IsNullOrEmpty(id)||string.IsNullOrEmpty(type))
				{
					continue;
				}

				var size = item.TryGetProperty("Size", out var sizeEl)?sizeEl.GetString() ?? "":"";
				var state = item.TryGetProperty("State", out var stateEl)?stateEl.GetString() ?? "":"";
				var src = item.TryGetProperty("Src", out var srcEl)?srcEl.GetString() ?? "":"";
				var zone = item.TryGetProperty("Zone", out var zoneEl)&&zoneEl.TryGetInt32(out var z)?z:0;
				var x = item.TryGetProperty("X", out var xEl)&&xEl.TryGetInt32(out var xv)?xv:0;
				var y = item.TryGetProperty("Y", out var yEl)&&yEl.TryGetInt32(out var yv)?yv:0;
				var endsAt = item.TryGetProperty("EndsAt", out var endsEl)&&endsEl.TryGetInt64(out var e)?e:0;

				var parsedEvent = new WarmapEvent(id, type, size, state, src, zone, x, y, endsAt);

				// Announced but not running yet. Dropping it here rather than at each drawing site
				// keeps every consumer of the snapshot on live events only; it reappears in the next
				// snapshot (~10s) once the server flips it to active.
				if(parsedEvent.IsPending)
				{
					continue;
				}

				parsed.Add(parsedEvent);
			}
		}

		// The envelope also carries "df" (realm holding Darkness Falls). Eden does not keep it
		// current, so it is deliberately ignored rather than surfaced as stale data.
		lock(this.stateLock)
		{
			this.events = parsed;
		}
	}

	private void ProcessRotationMessage(JsonElement rotationEl)
	{
		var cur = rotationEl.TryGetProperty("Cur", out var curEl)&&curEl.TryGetInt32(out var c)?c:0;
		var of = rotationEl.TryGetProperty("Of", out var ofEl)&&ofEl.TryGetInt32(out var o)?o:0;
		var next = rotationEl.TryGetProperty("Next", out var nextEl)?nextEl.GetString() ?? "":"";
		var nextAt = rotationEl.TryGetProperty("NextAt", out var atEl)&&atEl.TryGetInt64(out var at)?at:0;
		var nextType = rotationEl.TryGetProperty("NextType", out var ntEl)?ntEl.GetString() ?? "":"";

		lock(this.stateLock)
		{
			this.rotation = new WarmapRotation(cur, of, next, nextAt, nextType);
		}
	}

	/// <summary>
	/// Full snapshot keyed by relic id: <c>{"1":{"or":1,"t":1,"s":86,"r":2}}</c>. <c>s</c> (the site
	/// the relic sits on) is deliberately ignored — the pad list carries the same link via its own
	/// <c>r</c> field along with the coordinates we need, so that is the single source of truth.
	/// </summary>
	private void ProcessRelicsSnapshot(JsonElement relicsEl)
	{
		if(relicsEl.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		var parsed = new Dictionary<int, WarmapRelic>();

		foreach(var kv in relicsEl.EnumerateObject())
		{
			if(!int.TryParse(kv.Name, out var id))
			{
				continue;
			}

			var r = kv.Value;
			var origin = r.TryGetProperty("or", out var orEl)&&orEl.TryGetInt32(out var o)?o:0;
			var type = r.TryGetProperty("t", out var tEl)&&tEl.TryGetInt32(out var t)?t:0;
			var owner = r.TryGetProperty("r", out var rEl)&&rEl.TryGetInt32(out var ow)?ow:origin;
			var site = ReadSite(r);

			parsed[id] = new WarmapRelic(id, origin, type, owner, site);
		}

		lock(this.stateLock)
		{
			this.relics = parsed;
		}
	}

	/// <summary>
	/// A single relic moved or changed hands: <c>{"relic":{"id":2,"s":82,"r":2}}</c>. Eden's own client
	/// treats this as the authoritative update and only ever seeds from the snapshot, so without it
	/// ownership and location freeze at whatever was true when the socket connected.
	/// </summary>
	private void ProcessRelicUpdate(JsonElement relicEl)
	{
		if(!relicEl.TryGetProperty("id", out var idEl)||!idEl.TryGetInt32(out var id))
		{
			return;
		}

		lock(this.stateLock)
		{
			if(!this.relics.TryGetValue(id, out var existing))
			{
				return;
			}

			var owner = relicEl.TryGetProperty("r", out var rEl)&&rEl.TryGetInt32(out var ow)?ow:existing.OwnerRealm;
			var site = relicEl.TryGetProperty("s", out _)?ReadSite(relicEl):existing.Site;

			this.relics[id] = existing with { OwnerRealm = owner, Site = site };
		}
	}

	/// <summary>Eden sends the site as a number in the snapshot but quotes it in some updates, so both are accepted.</summary>
	private static int ReadSite(JsonElement element)
	{
		if(!element.TryGetProperty("s", out var sEl))
		{
			return 0;
		}

		if(sEl.ValueKind == JsonValueKind.Number&&sEl.TryGetInt32(out var number))
		{
			return number;
		}

		return sEl.ValueKind == JsonValueKind.String&&int.TryParse(sEl.GetString(), out var parsed)?parsed:0;
	}

	/// <summary>
	/// Full snapshot array. <c>h</c> marks the six home relic keeps (the walled pad a relic returns
	/// to when uncontested); every other entry is an ordinary frontier keep that can host a captured
	/// relic. <c>r</c> is the relic id on the pad, 0 when empty.
	/// </summary>
	private void ProcessRelicPadsSnapshot(JsonElement padsEl)
	{
		if(padsEl.ValueKind != JsonValueKind.Array)
		{
			return;
		}

		var parsed = new List<WarmapRelicPad>();

		foreach(var item in padsEl.EnumerateArray())
		{
			var name = item.TryGetProperty("n", out var nEl)?Normalize(nEl.GetString() ?? ""):"";
			if(string.IsNullOrEmpty(name))
			{
				continue;
			}

			var x = item.TryGetProperty("x", out var xEl)&&xEl.TryGetInt32(out var xv)?xv:0;
			var y = item.TryGetProperty("y", out var yEl)&&yEl.TryGetInt32(out var yv)?yv:0;
			var rlm = item.TryGetProperty("rlm", out var rlmEl)&&rlmEl.TryGetInt32(out var rl)?rl:0;
			var origin = item.TryGetProperty("o", out var oEl)&&oEl.TryGetInt32(out var o)?o:0;
			var padType = item.TryGetProperty("pt", out var ptEl)&&ptEl.TryGetInt32(out var pt)?pt:2;
			var home = item.TryGetProperty("h", out var hEl)&&hEl.TryGetInt32(out var h)&&h != 0;
			var relicId = item.TryGetProperty("r", out var rEl)&&rEl.TryGetInt32(out var ri)?ri:0;
			var relicType = item.TryGetProperty("rt", out var rtEl)&&rtEl.TryGetInt32(out var rt)?rt:-1;
			var keepId = item.TryGetProperty("k", out var kEl)&&kEl.TryGetInt32(out var k)?k:0;

			parsed.Add(new WarmapRelicPad(name, keepId, x, y, rlm, origin, padType, home, relicId, relicType));
		}

		lock(this.stateLock)
		{
			this.relicPads = parsed;
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
				if(combat&&!this.combatStartTimes.ContainsKey(name))
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
			if(combat&&!existing.InCombat&&!this.combatStartTimes.ContainsKey(name))
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
