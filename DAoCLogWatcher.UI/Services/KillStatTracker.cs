using System.Collections.Generic;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.UI.Services;

/// <summary>
/// Tracks kill and death counts for the detected character, backed by a
/// rolling buffer of <see cref="KillEvent"/>s so stats can be recomputed
/// when the character name is first identified.
/// </summary>
internal sealed class KillStatTracker
{
	private const int KILL_BUFFER_MAX_SIZE = 500;

	private readonly List<KillEvent> killEventBuffer = new();

	public int Kills { get; private set; }

	public int Deaths { get; private set; }

	/// <summary>
	/// Call when a <see cref="KillEvent"/> is seen in the log.
	/// Buffers the event and, if the character name is already known,
	/// immediately increments <see cref="Kills"/> or <see cref="Deaths"/>.
	/// </summary>
	public void OnKillEvent(KillEvent ev, string? characterName, ref bool killStatsChanged)
	{
		this.killEventBuffer.Add(ev);
		if(this.killEventBuffer.Count > KILL_BUFFER_MAX_SIZE)
		{
			this.killEventBuffer.RemoveAt(0);
		}

		if(characterName == null)
		{
			return;
		}

		if(ev.Killer == characterName)
		{
			this.Kills++;
			killStatsChanged = true;
		}

		if(ev.Victim == characterName)
		{
			this.Deaths++;
			killStatsChanged = true;
		}
	}

	/// <summary>
	/// Call when the detected character name changes. Recomputes
	/// <see cref="Kills"/> and <see cref="Deaths"/> from the buffered events.
	/// </summary>
	public void OnCharacterChanged(string? characterName, ref bool killStatsChanged)
	{
		if(characterName == null)
		{
			if(this.Kills != 0||this.Deaths != 0)
			{
				this.Kills = 0;
				this.Deaths = 0;
				killStatsChanged = true;
			}

			return;
		}

		var kills = 0;
		var deaths = 0;
		foreach(var ev in this.killEventBuffer)
		{
			if(ev.Killer == characterName)
			{
				kills++;
			}

			if(ev.Victim == characterName)
			{
				deaths++;
			}
		}

		if(kills != this.Kills||deaths != this.Deaths)
		{
			this.Kills = kills;
			this.Deaths = deaths;
			killStatsChanged = true;
		}
	}

	public void Reset()
	{
		this.killEventBuffer.Clear();
		this.Kills = 0;
		this.Deaths = 0;
	}
}
