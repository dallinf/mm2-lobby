namespace Mm2Lobby.Core.Preflight;

public sealed record CheckResult(
    string Name,
    CheckStatus Status,
    string Message,
    string? FixDescription);
