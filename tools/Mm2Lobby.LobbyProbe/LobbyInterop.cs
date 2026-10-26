using System.Runtime.InteropServices;

namespace Mm2Lobby.LobbyProbe;

/// <summary>The COM functions of ole32.dll that the probe needs.</summary>
internal static partial class Ole32
{
    public const uint CoinitApartmentThreaded = 0x2;
    public const uint ClsctxInprocServer = 0x1;

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
/// This class calls the methods of IDirectPlayLobby3A through the vtable of
/// the object.
/// <para>
/// The probe calls the vtable by hand and does not declare a ComImport
/// interface. Both routes depend on the same order of methods, so both carry
/// the same risk of a wrong slot. The hand route adds one gain: the probe can
/// print the slot number and the address in that slot before each call. The
/// owner then sees whether the slot held an address at all.
/// </para>
/// <para>
/// Four runs on Windows 11 validated slot 0, slot 2, slot 10 and slot 14. A
/// control run that registered Notepad as a lobby application returned S_OK
/// and put Notepad on the screen. Slot 13 comes from a deduction, not from a
/// run. See the remark on LobbySlots.
/// </para>
/// <para>
/// WARNING: a wrong slot number or a wrong signature calls native code with
/// the wrong arguments. The probe catches a managed exception, and it cannot
/// catch a hard crash inside dplayx.dll. If the program stops with no further
/// line, read the last slot number that it printed.
/// </para>
/// </summary>
internal static unsafe class LobbyInterop
{
    /// <summary>
    /// This method reads one address out of the vtable of a COM object. The
    /// first field of a COM object points at the vtable.
    /// </summary>
    public static IntPtr ReadSlot(IntPtr instance, int slot)
    {
        var vtable = *(IntPtr**)instance;
        return vtable[slot];
    }

    private static IntPtr Announce(IntPtr instance, int slot, string name)
    {
        var address = ReadSlot(instance, slot);
        Log.Info($"The probe calls {name}. Slot {slot} holds the address 0x{address.ToInt64():X16}.");
        return address;
    }

    /// <summary>
    /// IUnknown::QueryInterface. The probe asks the version 1 object for the
    /// version 3 interface.
    /// </summary>
    public static int QueryInterface(IntPtr instance, Guid interfaceId, out IntPtr result)
    {
        result = IntPtr.Zero;
        var address = Announce(instance, LobbySlots.QueryInterface, "QueryInterface");
        if (address == IntPtr.Zero) return unchecked((int)0x80004003);

        var call = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)address;

        // interfaceId is a copy on the stack, so the probe takes its address
        // without a fixed statement.
        IntPtr local;
        var hr = call(instance, &interfaceId, &local);

        result = local;
        return hr;
    }

    /// <summary>IUnknown::Release. It returns the new reference count.</summary>
    public static uint Release(IntPtr instance)
    {
        var address = Announce(instance, LobbySlots.Release, "Release");
        if (address == IntPtr.Zero) return 0;

        var call = (delegate* unmanaged[Stdcall]<IntPtr, uint>)address;
        return call(instance);
    }

    /// <summary>
    /// IDirectPlayLobby2::CreateCompoundAddress. A null buffer asks for the
    /// size of the address.
    /// </summary>
    public static int CreateCompoundAddress(
        IntPtr instance,
        IntPtr elements,
        uint elementCount,
        IntPtr buffer,
        ref uint size)
    {
        var address = Announce(instance, LobbySlots.CreateCompoundAddress, "CreateCompoundAddress");
        if (address == IntPtr.Zero) return unchecked((int)0x80004003);

        var call = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, uint*, int>)address;

        fixed (uint* sizePointer = &size)
        {
            return call(instance, elements, elementCount, buffer, sizePointer);
        }
    }

    /// <summary>
    /// IDirectPlayLobby::SetLobbyMessageEvent. DirectPlay signals the event
    /// when the game sends a lobby message back to the lobby.
    /// <para>
    /// The probe calls this method after a successful RunApplication, and it
    /// does not call ReceiveLobbyMessage. A read of the message needs the
    /// DPLMSG structs, and that is new interop that nobody can test here. The
    /// signal alone answers the question: did MM2 reach the lobby at all?
    /// </para>
    /// <para>
    /// WARNING: the order of the arguments comes from memory. The flags
    /// argument stands first and must be 0, the application id stands second,
    /// and the event handle stands third.
    /// </para>
    /// </summary>
    public static int SetLobbyMessageEvent(
        IntPtr instance,
        uint flags,
        uint applicationId,
        IntPtr receiveEvent)
    {
        var address = Announce(instance, LobbySlots.SetLobbyMessageEvent, "SetLobbyMessageEvent");
        if (address == IntPtr.Zero) return unchecked((int)0x80004003);

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
        var address = Announce(instance, LobbySlots.RunApplication, "RunApplication");
        if (address == IntPtr.Zero) return unchecked((int)0x80004003);

        var call = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint*, IntPtr, IntPtr, int>)address;

        uint local = 0;
        var hr = call(instance, flags, &local, connection, receiveEvent);
        applicationId = local;
        return hr;
    }
}

/// <summary>
/// This class loads dplayx.dll and creates the lobby object. It tries the
/// plain export first, and it tries COM second. It prints which route worked.
/// </summary>
internal static unsafe class LobbyFactory
{
    private const string Library = "dplayx.dll";
    private const string Export = "DirectPlayLobbyCreateA";

    /// <summary>
    /// This method loads dplayx.dll. It returns IntPtr.Zero when the load
    /// fails, and it prints the reason. A 64 bit program needs the 64 bit
    /// dplayx.dll in System32. A 32 bit program needs the 32 bit copy in
    /// SysWOW64.
    /// </summary>
    public static IntPtr LoadLibrary()
    {
        try
        {
            var handle = NativeLibrary.Load(Library);
            Log.Good($"Windows loaded {Library}. The handle is 0x{handle.ToInt64():X16}.");
            return handle;
        }
        catch (Exception ex)
        {
            Log.Failure($"The load of {Library}", ex);
            Log.Problem(
                "A failed load has two common causes. Windows has DirectPlay off, "
                + "or the bit width of the probe does not match the bit width of "
                + "the DLL. Publish the probe with -r win-x86 to test the second cause.");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// This method calls the DirectPlayLobbyCreateA export. The header says
    /// that every argument except the second must be null or zero.
    /// </summary>
    public static int CreateByExport(IntPtr library, out IntPtr lobby)
    {
        lobby = IntPtr.Zero;

        if (!NativeLibrary.TryGetExport(library, Export, out var address))
        {
            Log.Problem($"{Library} has no export called {Export}.");
            return unchecked((int)0x8007007F);
        }

        Log.Info($"The export {Export} sits at the address 0x{address.ToInt64():X16}.");

        var call = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, IntPtr, IntPtr, uint, int>)address;

        IntPtr local;
        var hr = call(IntPtr.Zero, &local, IntPtr.Zero, IntPtr.Zero, 0);
        lobby = local;
        return hr;
    }

    /// <summary>
    /// This method asks COM for the lobby object. It is the second route.
    /// </summary>
    public static int CreateByCom(Guid interfaceId, out IntPtr lobby)
    {
        var classId = DirectPlayIds.ClsidDirectPlayLobby;

        return Ole32.CoCreateInstance(
            in classId,
            IntPtr.Zero,
            Ole32.ClsctxInprocServer,
            in interfaceId,
            out lobby);
    }
}
