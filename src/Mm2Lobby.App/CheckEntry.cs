using Mm2Lobby.Core.Preflight;

namespace Mm2Lobby.App;

/// <summary>
/// One preflight check, and the hint that the window shows beside a Repair
/// button.
/// </summary>
/// <param name="Check">The check that the window runs.</param>
/// <param name="RepairHint">
/// The sentence that tells the player why a check that passes still offers a
/// repair. A null value means that the check offers no action while it passes.
/// </param>
public sealed record CheckEntry(ICheck Check, string? RepairHint = null);
