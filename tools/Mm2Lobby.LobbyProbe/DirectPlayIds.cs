namespace Mm2Lobby.LobbyProbe;

/// <summary>
/// The GUID values of the legacy DirectPlay lobby API.
/// <para>
/// WARNING: every GUID in this file comes from memory. The author could not
/// open dplay.h or dplobby.h, and could not run one line of this code. A wrong
/// GUID is the most likely cause of a failure in the probe. The probe prints
/// each GUID that it uses, so the owner can compare the printed value against
/// the header or against the registry.
/// </para>
/// </summary>
internal static class DirectPlayIds
{
    // The COM class of the lobby object, CLSID_DirectPlayLobby.
    public static readonly Guid ClsidDirectPlayLobby =
        new("2FE8F810-B2A5-11D0-A787-0000F803ABFC");

    // The ANSI lobby interfaces. Version 3 adds RegisterApplication, and
    // version 2 adds CreateCompoundAddress.
    public static readonly Guid IidDirectPlayLobbyA =
        new("26C66A70-B367-11CF-A0F0-00AA00B7FAC9");

    public static readonly Guid IidDirectPlayLobby2A =
        new("1BB4AF80-A303-11D0-9C4F-00A0C905425E");

    public static readonly Guid IidDirectPlayLobby3A =
        new("2DB72491-652C-11D1-A7A8-0000F803ABFC");

    // The service provider of TCP/IP, DPSPGUID_TCPIP. ZeroTier gives the
    // computers an IP address, so the probe asks for this provider.
    public static readonly Guid DpSpGuidTcpIp =
        new("36E95EE0-8577-11CF-960C-0080C7534E82");

    // The chunk types of a compound address, the DPAID_ values.
    public static readonly Guid DpAidServiceProvider =
        new("07D916C0-E0AF-11CF-9C4E-00A0C905425E");

    // DPAID_INet takes the address as an ANSI string. DPAID_INetW takes a wide
    // string. The probe uses the ANSI chunk, because it uses the ANSI
    // interface everywhere.
    public static readonly Guid DpAidInet =
        new("C4A54DA0-E0AF-11CF-9C4E-00A0C905425E");

    public static readonly Guid DpAidInetPort =
        new("E4524541-8EA5-11D1-8A96-006097B01411");

    /// <summary>
    /// This method prints every GUID above, with the name that the header
    /// gives it. The owner can then check each value.
    /// </summary>
    public static void PrintAll()
    {
        Log.Item("CLSID_DirectPlayLobby", Format(ClsidDirectPlayLobby));
        Log.Item("IID_IDirectPlayLobbyA", Format(IidDirectPlayLobbyA));
        Log.Item("IID_IDirectPlayLobby2A", Format(IidDirectPlayLobby2A));
        Log.Item("IID_IDirectPlayLobby3A", Format(IidDirectPlayLobby3A));
        Log.Item("DPSPGUID_TCPIP", Format(DpSpGuidTcpIp));
        Log.Item("DPAID_ServiceProvider", Format(DpAidServiceProvider));
        Log.Item("DPAID_INet", Format(DpAidInet));
        Log.Item("DPAID_INetPort", Format(DpAidInetPort));
    }

    public static string Format(Guid value) => value.ToString("B").ToUpperInvariant();
}

/// <summary>
/// The flags of DPLCONNECTION.dwFlags.
/// <para>
/// WARNING: both values come from memory. dplobby.h sets
/// DPLCONNECTION_CREATESESSION to DPOPEN_CREATE and DPLCONNECTION_JOINSESSION
/// to DPOPEN_JOIN. The author believes DPOPEN_JOIN is 1 and DPOPEN_CREATE is
/// 2. If the probe reports an invalid flags error, read these two numbers
/// first.
/// </para>
/// </summary>
internal static class LobbyConnectionFlags
{
    public const uint JoinSession = 0x00000001;
    public const uint CreateSession = 0x00000002;
}

/// <summary>
/// The vtable slot of each method of IDirectPlayLobby3A.
/// <para>
/// WARNING: the whole order comes from memory. The DirectPlay headers list the
/// methods of each interface version in alphabetical order, and the author
/// used that rule to rebuild the list. A wrong slot number calls the wrong
/// function. The probe prints the slot that it reads, and the address in that
/// slot, before every call.
/// </para>
/// </summary>
internal static class LobbySlots
{
    // IUnknown.
    public const int QueryInterface = 0;
    public const int AddRef = 1;
    public const int Release = 2;

    // IDirectPlayLobby, 11 methods in alphabetical order.
    public const int Connect = 3;
    public const int CreateAddress = 4;
    public const int EnumAddress = 5;
    public const int EnumAddressTypes = 6;
    public const int EnumLocalApplications = 7;
    public const int GetConnectionSettings = 8;
    public const int ReceiveLobbyMessage = 9;
    public const int RunApplication = 10;
    public const int SendLobbyMessage = 11;
    public const int SetConnectionSettings = 12;
    public const int SetLobbyMessageEvent = 13;

    // IDirectPlayLobby2 adds one method.
    public const int CreateCompoundAddress = 14;

    // IDirectPlayLobby3 adds four methods in alphabetical order.
    public const int ConnectEx = 15;
    public const int RegisterApplication = 16;
    public const int UnregisterApplication = 17;
    public const int WaitForConnectionSettings = 18;
}
