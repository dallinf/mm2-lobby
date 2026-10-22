namespace Mm2Lobby.Core.Preflight;

public enum CheckStatus
{
    /// <summary>The condition is true.</summary>
    Pass,

    /// <summary>The condition is not true. The app can fix it.</summary>
    Fail,

    /// <summary>The app could not decide, for example because a tool is absent.</summary>
    Unavailable,
}
