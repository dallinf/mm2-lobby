namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// The GUID values of the legacy DirectPlay lobby API.
/// <para>
/// Fourteen runs on Windows 11 validated every value here. The spike started
/// MM2 as a host and as a player who joins, with these values. Do not rebuild
/// a value from memory: copy it from this file.
/// </para>
/// </summary>
internal static class DirectPlayIds
{
    /// <summary>CLSID_DirectPlayLobby, the COM class of the lobby object.</summary>
    public static readonly Guid ClsidDirectPlayLobby =
        new("2FE8F810-B2A5-11D0-A787-0000F803ABFC");

    /// <summary>
    /// IID_IDirectPlayLobby3A. Version 3 of the ANSI interface. The launcher
    /// needs version 2 or later, because CreateCompoundAddress arrived in
    /// version 2.
    /// </summary>
    public static readonly Guid IidDirectPlayLobby3A =
        new("2DB72491-652C-11D1-A7A8-0000F803ABFC");

    /// <summary>
    /// DPSPGUID_TCPIP, the service provider of TCP/IP. ZeroTier gives each
    /// computer an IP address, so the launcher asks for this provider.
    /// </summary>
    public static readonly Guid ServiceProviderTcpIp =
        new("36E95EE0-8577-11CF-960C-0080C7534E82");

    /// <summary>DPAID_ServiceProvider, the chunk that names the provider.</summary>
    public static readonly Guid ChunkServiceProvider =
        new("07D916C0-E0AF-11CF-9C4E-00A0C905425E");

    /// <summary>
    /// DPAID_INet, the chunk that carries the address of the host as ANSI text.
    /// </summary>
    public static readonly Guid ChunkInet =
        new("C4A54DA0-E0AF-11CF-9C4E-00A0C905425E");

    /// <summary>This method writes a GUID the way the headers write it.</summary>
    public static string Format(Guid value) => value.ToString("B").ToUpperInvariant();
}

/// <summary>
/// The values of DPLCONNECTION.dwFlags. Runs on Windows 11 validated both
/// values: the host case with 2, and the join case with 1.
/// </summary>
internal static class LobbyConnectionFlags
{
    /// <summary>DPLCONNECTION_JOINSESSION.</summary>
    public const uint JoinSession = 0x00000001;

    /// <summary>DPLCONNECTION_CREATESESSION.</summary>
    public const uint CreateSession = 0x00000002;
}

/// <summary>
/// The vtable slot of each method of IDirectPlayLobby3A that the launcher
/// calls.
/// <para>
/// Runs on Windows 11 validated slot 0, slot 2, slot 10, slot 13 and slot 14.
/// Slot 13 fired the lobby message event when MM2 reached the lobby, so the
/// run validated it.
/// </para>
/// <para>
/// WARNING: a wrong slot number calls native code with the wrong arguments,
/// and the process can stop with no managed exception. Change nothing here.
/// </para>
/// </summary>
internal static class LobbySlots
{
    // IUnknown.
    public const int QueryInterface = 0;
    public const int Release = 2;

    // IDirectPlayLobby, 11 methods in alphabetical order.
    public const int RunApplication = 10;
    public const int SetLobbyMessageEvent = 13;

    // IDirectPlayLobby2 adds one method.
    public const int CreateCompoundAddress = 14;
}
