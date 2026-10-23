using System.Runtime.InteropServices;

namespace Mm2Lobby.Windows.Lobby;

/// <summary>The COM functions of ole32.dll that the launcher needs.</summary>
internal static partial class Ole32
{
    public const uint ApartmentThreaded = 0x2;
    public const uint InprocServer = 0x1;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(
        in Guid classId,
        IntPtr outer,
        uint context,
        in Guid interfaceId,
        out IntPtr result);
}

/// <summary>
/// This class calls the methods of IDirectPlayLobby3A through the vtable of the
/// object. The first field of a COM object points at the vtable.
/// <para>
/// The launcher calls the vtable by hand and declares no ComImport interface.
/// Both routes depend on the same order of methods. The hand route came from
/// the spike, fourteen runs on Windows 11 validated it, and a rewrite to
/// ComImport would throw that evidence away.
/// </para>
/// <para>
/// WARNING: a wrong slot number or a wrong signature calls native code with
/// the wrong arguments. The process can then stop with no managed exception.
/// Change nothing here without a run on real Windows.
/// </para>
/// </summary>
internal static unsafe class LobbyInterop
{
    /// <summary>This method reads one address out of the vtable of an object.</summary>
    public static IntPtr ReadSlot(IntPtr instance, int slot)
    {
        var vtable = *(IntPtr**)instance;
        return vtable[slot];
    }

    /// <summary>
    /// IUnknown::QueryInterface. The launcher asks the version 1 object for the
    /// version 3 interface.
    /// </summary>
    public static int QueryInterface(IntPtr instance, Guid interfaceId, out IntPtr result)
    {
        result = IntPtr.Zero;

        var address = ReadSlot(instance, LobbySlots.QueryInterface);
        if (address == IntPtr.Zero) return DirectPlayHResults.Pointer;

        var call = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)address;

        // interfaceId is a copy on the stack, so this code takes its address
        // without a fixed statement.
        IntPtr local;
        var hr = call(instance, &interfaceId, &local);

        result = local;
        return hr;
    }

    /// <summary>IUnknown::Release. It returns the new reference count.</summary>
    public static uint Release(IntPtr instance)
    {
        var address = ReadSlot(instance, LobbySlots.Release);
        if (address == IntPtr.Zero) return 0;

        var call = (delegate* unmanaged[Stdcall]<IntPtr, uint>)address;
        return call(instance);
    }

    /// <summary>
    /// IDirectPlayLobby2::CreateCompoundAddress. A null buffer asks for the
    /// size of the address, and DirectPlay then returns DPERR_BUFFERTOOSMALL
    /// with the byte count that it needs.
    /// </summary>
    public static int CreateCompoundAddress(
        IntPtr instance,
        IntPtr elements,
        uint elementCount,
        IntPtr buffer,
        ref uint size)
    {
        var address = ReadSlot(instance, LobbySlots.CreateCompoundAddress);
        if (address == IntPtr.Zero) return DirectPlayHResults.Pointer;

        var call = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, uint*, int>)address;

        fixed (uint* sizePointer = &size)
        {
            return call(instance, elements, elementCount, buffer, sizePointer);
        }
    }

    /// <summary>
    /// IDirectPlayLobby::SetLobbyMessageEvent. DirectPlay signals the event
    /// when the game sends a lobby message back to the lobby. A run on Windows
    /// 11 signalled the event as MM2 reached the lobby.
    /// <para>
    /// The flags argument stands first and must be 0. The application id
    /// stands second, and the event handle stands third.
    /// </para>
    /// </summary>
    public static int SetLobbyMessageEvent(
        IntPtr instance,
        uint flags,
        uint applicationId,
        IntPtr receiveEvent)
    {
        var address = ReadSlot(instance, LobbySlots.SetLobbyMessageEvent);
        if (address == IntPtr.Zero) return DirectPlayHResults.Pointer;

        var call = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr, int>)address;
        return call(instance, flags, applicationId, receiveEvent);
    }

    /// <summary>
    /// IDirectPlayLobby::RunApplication. DirectPlay starts the game and gives
    /// the game the settings in the DPLCONNECTION struct.
    /// </summary>
    public static int RunApplication(
        IntPtr instance,
        uint flags,
        out uint applicationId,
        IntPtr connection,
        IntPtr receiveEvent)
    {
        applicationId = 0;

        var address = ReadSlot(instance, LobbySlots.RunApplication);
        if (address == IntPtr.Zero) return DirectPlayHResults.Pointer;

        var call = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint*, IntPtr, IntPtr, int>)address;

        uint local = 0;
        var hr = call(instance, flags, &local, connection, receiveEvent);
        applicationId = local;
        return hr;
    }
}

/// <summary>
/// This class loads dplayx.dll and creates the lobby object. It tries the plain
/// export first, because the export needs no COM class registration, and it
/// tries COM second.
/// </summary>
internal static unsafe class LobbyFactory
{
    /// <summary>
    /// The launcher loads the library by its plain name, so Windows picks the
    /// copy that matches the bit width of the process.
    /// </summary>
    public const string Library = "dplayx.dll";

    private const string Export = "DirectPlayLobbyCreateA";

    /// <summary>
    /// This method loads dplayx.dll. It returns IntPtr.Zero when the load
    /// fails. A 32 bit process needs the 32 bit copy in SysWOW64, and Windows
    /// 11 ships that copy only.
    /// </summary>
    public static IntPtr LoadLibrary() =>
        NativeLibrary.TryLoad(Library, out var handle) ? handle : IntPtr.Zero;

    /// <summary>
    /// This method calls the DirectPlayLobbyCreateA export. The header says
    /// that every argument except the second must be null or zero.
    /// </summary>
    public static int CreateByExport(IntPtr library, out IntPtr lobby)
    {
        lobby = IntPtr.Zero;

        if (!NativeLibrary.TryGetExport(library, Export, out var address))
        {
            return unchecked((int)0x8007007F);
        }

        var call = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, IntPtr, IntPtr, uint, int>)address;

        IntPtr local;
        var hr = call(IntPtr.Zero, &local, IntPtr.Zero, IntPtr.Zero, 0);
        lobby = local;
        return hr;
    }

    /// <summary>This method asks COM for the lobby object. It is the second route.</summary>
    public static int CreateByCom(Guid interfaceId, out IntPtr lobby)
    {
        var classId = DirectPlayIds.ClsidDirectPlayLobby;

        return Ole32.CoCreateInstance(
            in classId,
            IntPtr.Zero,
            Ole32.InprocServer,
            in interfaceId,
            out lobby);
    }
}
