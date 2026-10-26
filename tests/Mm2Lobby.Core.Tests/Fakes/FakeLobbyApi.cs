using System.Runtime.InteropServices;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>One recorded call of CreateCompoundAddress.</summary>
/// <param name="Buffer">The buffer, which is zero on the size pass.</param>
/// <param name="ElementCount">The count of chunks that the launcher sent.</param>
internal sealed record AddressCall(IntPtr Buffer, uint ElementCount);

/// <summary>
/// One recorded call of RunApplication. The fake reads the three structs at
/// once, so a test asserts what the launcher sent and not what the memory holds
/// later.
/// </summary>
internal sealed record RunCall(
    IntPtr Lobby,
    DplConnection Connection,
    DpSessionDesc2 Session,
    DpName Name,
    string? SessionName,
    string? PlayerName);

/// <summary>One recorded call of SetLobbyMessageEvent.</summary>
internal sealed record LobbyEventCall(IntPtr Lobby, uint ApplicationId, IntPtr Handle);

/// <summary>
/// This class stands in for every call that the launcher makes into Windows. It
/// answers the HRESULT that a test asks for, and it records each call.
/// <para>
/// The defaults describe the run that worked on Windows 11: COM starts, the
/// export route gives the version 1 object, QueryInterface gives the version 3
/// interface, CreateCompoundAddress asks for 48 bytes and fills them, and
/// RunApplication starts the game.
/// </para>
/// </summary>
internal sealed class FakeLobbyApi : ILobbyApi
{
    /// <summary>The apartment model of each CoInitializeEx call, in order.</summary>
    public List<uint> ComStarts { get; } = [];

    /// <summary>The count of CoUninitialize calls.</summary>
    public int ComStops { get; private set; }

    /// <summary>The handle that LoadLibrary gives. Zero stands for a failed load.</summary>
    public IntPtr Library { get; set; } = new(0x7000);

    /// <summary>The HRESULT of DirectPlayLobbyCreateA.</summary>
    public int ExportResult { get; set; } = DirectPlayHResults.Ok;

    /// <summary>The version 1 object that the export route gives.</summary>
    public IntPtr ExportLobby { get; set; } = new(0x7100);

    /// <summary>The HRESULT of QueryInterface.</summary>
    public int UpgradeResult { get; set; } = DirectPlayHResults.Ok;

    /// <summary>The version 3 interface that QueryInterface gives.</summary>
    public IntPtr UpgradeLobby { get; set; } = new(0x7200);

    /// <summary>The interface id of each QueryInterface call, in order.</summary>
    public List<Guid> InterfacesAsked { get; } = [];

    /// <summary>The HRESULT of CoCreateInstance.</summary>
    public int ComCreateResult { get; set; } = DirectPlayHResults.Ok;

    /// <summary>The interface that the COM route gives.</summary>
    public IntPtr ComLobby { get; set; } = new(0x7300);

    /// <summary>Every interface that the launcher released, in order.</summary>
    public List<IntPtr> Released { get; } = [];

    /// <summary>The byte count that the size pass of CreateCompoundAddress asks for.</summary>
    public uint AddressSize { get; set; } = 48;

    /// <summary>The HRESULT of the fill pass of CreateCompoundAddress.</summary>
    public int FillResult { get; set; } = DirectPlayHResults.Ok;

    /// <summary>Every call of CreateCompoundAddress, in order.</summary>
    public List<AddressCall> AddressCalls { get; } = [];

    /// <summary>The chunks of the last call of CreateCompoundAddress.</summary>
    public List<DpCompoundAddressElement> Elements { get; } = [];

    /// <summary>
    /// The HRESULT of each RunApplication call, in order. An empty queue
    /// answers S_OK, so a test that wants a success queues nothing.
    /// </summary>
    public Queue<int> RunResults { get; } = new();

    /// <summary>Every call of RunApplication, in order.</summary>
    public List<RunCall> Runs { get; } = [];

    /// <summary>The application id that RunApplication gives.</summary>
    public uint ApplicationId { get; set; } = 77;

    /// <summary>Every call of SetLobbyMessageEvent, in order.</summary>
    public List<LobbyEventCall> LobbyEvents { get; } = [];

    /// <summary>The HRESULT of SetLobbyMessageEvent.</summary>
    public int EventResult { get; set; } = DirectPlayHResults.Ok;

    /// <summary>The HRESULT of CoInitializeEx.</summary>
    public int ComStartResult { get; set; } = DirectPlayHResults.Ok;

    public int CoInitializeEx(IntPtr reserved, uint coInit)
    {
        ComStarts.Add(coInit);
        return ComStartResult;
    }

    public void CoUninitialize() => ComStops++;

    public IntPtr LoadLibrary() => Library;

    public int CreateByExport(IntPtr library, out IntPtr lobby)
    {
        lobby = DirectPlayHResults.Failed(ExportResult) ? IntPtr.Zero : ExportLobby;
        return ExportResult;
    }

    public int CreateByCom(Guid interfaceId, out IntPtr lobby)
    {
        InterfacesAsked.Add(interfaceId);
        lobby = DirectPlayHResults.Failed(ComCreateResult) ? IntPtr.Zero : ComLobby;
        return ComCreateResult;
    }

    public int QueryInterface(IntPtr instance, Guid interfaceId, out IntPtr result)
    {
        InterfacesAsked.Add(interfaceId);
        result = DirectPlayHResults.Failed(UpgradeResult) ? IntPtr.Zero : UpgradeLobby;
        return UpgradeResult;
    }

    public uint Release(IntPtr instance)
    {
        Released.Add(instance);
        return 0;
    }

    public int CreateCompoundAddress(
        IntPtr instance,
        IntPtr elements,
        uint elementCount,
        IntPtr buffer,
        ref uint size)
    {
        AddressCalls.Add(new AddressCall(buffer, elementCount));
        ReadElements(elements, elementCount);

        if (buffer == IntPtr.Zero)
        {
            size = AddressSize;
            return DirectPlayHResults.MakeError(DirectPlayHResults.BufferTooSmallCode);
        }

        return FillResult;
    }

    public int RunApplication(
        IntPtr instance,
        uint flags,
        out uint applicationId,
        IntPtr connection,
        IntPtr receiveEvent)
    {
        var sent = Marshal.PtrToStructure<DplConnection>(connection);
        var session = Marshal.PtrToStructure<DpSessionDesc2>(sent.SessionDesc);
        var name = Marshal.PtrToStructure<DpName>(sent.PlayerName);

        Runs.Add(new RunCall(
            instance,
            sent,
            session,
            name,
            Marshal.PtrToStringAnsi(session.SessionNameAnsi),
            Marshal.PtrToStringAnsi(name.ShortNameAnsi)));

        var hr = RunResults.Count > 0 ? RunResults.Dequeue() : DirectPlayHResults.Ok;

        applicationId = DirectPlayHResults.Failed(hr) ? 0 : ApplicationId;
        return hr;
    }

    public int SetLobbyMessageEvent(
        IntPtr instance,
        uint flags,
        uint applicationId,
        IntPtr receiveEvent)
    {
        LobbyEvents.Add(new LobbyEventCall(instance, applicationId, receiveEvent));
        return EventResult;
    }

    private void ReadElements(IntPtr elements, uint count)
    {
        Elements.Clear();

        var size = Marshal.SizeOf<DpCompoundAddressElement>();

        for (var i = 0; i < count; i++)
        {
            Elements.Add(Marshal.PtrToStructure<DpCompoundAddressElement>(elements + (i * size)));
        }
    }
}
