namespace Mm2Lobby.LobbyProbe;

/// <summary>
/// The GUID values of the legacy DirectPlay lobby API.
/// <para>
/// The author wrote every GUID in this file from memory, and could not open
/// dplay.h or dplobby.h. Four runs on Windows 11 then validated them. A
/// control run that registered Notepad as a lobby application returned S_OK
/// and put Notepad on the screen, so the lobby object, the compound address
/// and RunApplication all worked with these values.
/// </para>
/// <para>
/// The probe still prints each GUID that it uses. The print costs nothing and
/// it keeps the record complete.
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
    /// gives it.
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
/// dplobby.h sets DPLCONNECTION_CREATESESSION to DPOPEN_CREATE and
/// DPLCONNECTION_JOINSESSION to DPOPEN_JOIN. The author wrote both numbers
/// from memory. A run on Windows 11 then validated CREATESESSION, because
/// RunApplication returned S_OK with the value 2.
/// </para>
/// <para>
/// WARNING: no run has validated JOINSESSION. The host case needs no second
/// computer, so every run so far used the host case.
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
/// The author rebuilt the whole order from memory. The DirectPlay headers list
/// the methods of each interface version in alphabetical order, and the author
/// used that rule.
/// </para>
/// <para>
/// Runs on Windows 11 validated slot 0, slot 2, slot 10 and slot 14. Those
/// four slots bracket the list: slot 10 is RunApplication, the 8th method of
/// IDirectPlayLobby, and slot 14 is CreateCompoundAddress, the 1st method of
/// IDirectPlayLobby2. Slot 11, slot 12 and slot 13 therefore hold the last
/// three methods of IDirectPlayLobby, and the alphabetical rule puts
/// SetLobbyMessageEvent at slot 13. That is a deduction from a validated
/// bracket, and not a run.
/// </para>
/// <para>
/// WARNING: a wrong slot number calls the wrong function. The probe prints the
/// slot that it reads, and the address in that slot, before every call.
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
