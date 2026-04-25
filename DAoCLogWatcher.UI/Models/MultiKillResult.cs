using System;

namespace DAoCLogWatcher.UI.Models;

/// <summary>Lean result fired by <see cref="DAoCLogWatcher.UI.Services.MultiKillDetector"/> — no UI model dependencies.</summary>
public sealed record MultiKillResult(TimeOnly Start, int TotalRp, int KillCount);
