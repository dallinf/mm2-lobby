namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// Every call that the launcher makes into Windows. The seam lets a test on
/// macOS drive the whole DirectPlay sequence and prove what the launcher keeps
/// and what it frees.
/// <para>
/// Each member mirrors one function of Ole32, LobbyFactory or LobbyInterop,
/// with the same name and the same arguments. The launcher therefore keeps
/// every decision and every line of its report, and WindowsLobbyApi below
/// forwards each call and adds nothing. INativeAllocator holds the memory half
/// of the same pattern.
/// </para>
/// <para>
/// WARNING: do not fold two members into one. The launcher reads the HRESULT of
/// each call, writes it into the report, and chooses the next step from it. A
/// member that hid two calls would hide a decision from every test.
/// </para>
/// </summary>
internal interface ILobbyApi
{
    /// <summary>CoInitializeEx. The launcher asks for one apartment thread.</summary>
    int CoInitializeEx(IntPtr reserved, uint coInit);

    /// <summary>CoUninitialize.</summary>
    void CoUninitialize();

    /// <summary>This method loads dplayx.dll. It gives zero after a failed load.</summary>
    IntPtr LoadLibrary();

    /// <summary>DirectPlayLobbyCreateA, the first route to the lobby object.</summary>
    int CreateByExport(IntPtr library, out IntPtr lobby);

    /// <summary>CoCreateInstance, the second route to the lobby object.</summary>
    int CreateByCom(Guid interfaceId, out IntPtr lobby);

    /// <summary>IUnknown::QueryInterface.</summary>
    int QueryInterface(IntPtr instance, Guid interfaceId, out IntPtr result);

    /// <summary>IUnknown::Release. It gives the new reference count.</summary>
    uint Release(IntPtr instance);

    /// <summary>IDirectPlayLobby2::CreateCompoundAddress.</summary>
    int CreateCompoundAddress(
        IntPtr instance,
        IntPtr elements,
        uint elementCount,
        IntPtr buffer,
        ref uint size);

    /// <summary>IDirectPlayLobby::RunApplication. DirectPlay starts the game.</summary>
    int RunApplication(
        IntPtr instance,
        uint flags,
        out uint applicationId,
        IntPtr connection,
        IntPtr receiveEvent);

    /// <summary>IDirectPlayLobby::SetLobbyMessageEvent.</summary>
    int SetLobbyMessageEvent(
        IntPtr instance,
        uint flags,
        uint applicationId,
        IntPtr receiveEvent);
}

/// <summary>
/// The real route into Windows. Each method forwards one call and holds no
/// decision, so a test on macOS loses nothing that it could assert.
/// </summary>
internal sealed class WindowsLobbyApi : ILobbyApi
{
    public int CoInitializeEx(IntPtr reserved, uint coInit) =>
        Ole32.CoInitializeEx(reserved, coInit);

    public void CoUninitialize() => Ole32.CoUninitialize();

    public IntPtr LoadLibrary() => LobbyFactory.LoadLibrary();

    public int CreateByExport(IntPtr library, out IntPtr lobby) =>
        LobbyFactory.CreateByExport(library, out lobby);

    public int CreateByCom(Guid interfaceId, out IntPtr lobby) =>
        LobbyFactory.CreateByCom(interfaceId, out lobby);

    public int QueryInterface(IntPtr instance, Guid interfaceId, out IntPtr result) =>
        LobbyInterop.QueryInterface(instance, interfaceId, out result);

    public uint Release(IntPtr instance) => LobbyInterop.Release(instance);

    public int CreateCompoundAddress(
        IntPtr instance,
        IntPtr elements,
        uint elementCount,
        IntPtr buffer,
        ref uint size) =>
        LobbyInterop.CreateCompoundAddress(instance, elements, elementCount, buffer, ref size);

    public int RunApplication(
        IntPtr instance,
        uint flags,
        out uint applicationId,
        IntPtr connection,
        IntPtr receiveEvent) =>
        LobbyInterop.RunApplication(instance, flags, out applicationId, connection, receiveEvent);

    public int SetLobbyMessageEvent(
        IntPtr instance,
        uint flags,
        uint applicationId,
        IntPtr receiveEvent) =>
        LobbyInterop.SetLobbyMessageEvent(instance, flags, applicationId, receiveEvent);
}
