namespace Mm2Lobby.Core.Preflight;

public enum CheckStatus
{
    /// <summary>The condition holds.</summary>
    Pass,

    /// <summary>The condition does not hold, and the app knows how to fix it.</summary>
    Fail,

    /// <summary>The app could not decide, for example because a tool is absent.</summary>
    Unavailable,
}
