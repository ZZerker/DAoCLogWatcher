using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DAoCLogWatcher.UI.Services;

public sealed record WarmapKeepState(int Realm, bool InCombat);

public sealed class WarmapWebSocketService: IDisposable
{
	private const string WsUri = "wss://ws.eden-daoc.net:60005";
	private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan PingIdleThreshold = TimeSpan.FromSeconds(25);
	private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

	private readonly Dictionary<int, string> idToName = new();
	private readonly Dictionary<string, WarmapKeepState> states = new();
	private readonly Lock stateLock = new();
	private CancellationTokenSource? cts;

	public event EventHandler? KeepsUpdated;

	public void Start()
	{
		this.cts = new CancellationTokenSource();
		_ = this.RunAsync(this.cts.Token);
	}

	public IReadOnlyDictionary<string, WarmapKeepState> GetSnapshot()
	{
		lock(this.stateLock)
		{
			return new Dictionary<string, WarmapKeepState>(this.states);
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
			// malformed message — ignore
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
		}

		this.KeepsUpdated?.Invoke(this, EventArgs.Empty);
	}

	private static string Normalize(string s)
	{
		return s.Replace('\u2019', '\'').Replace('\u2018', '\'').Trim();
	}

	public void Dispose()
	{
		this.cts?.Cancel();
		this.cts?.Dispose();
	}
}
