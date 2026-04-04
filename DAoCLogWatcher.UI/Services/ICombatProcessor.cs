using System;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public interface ICombatProcessor
{
	event EventHandler<CombatLogEntry>? DamageLogged;

	event EventHandler<HealLogEntry>? HealLogged;

	event EventHandler<CombatLogEntry>? MultiHitDetected;

	void Process(LogLine logLine);

	void Reset();
}
