using System.Diagnostics;
using System.Runtime.InteropServices;
using Mm2Lobby.Core.Launch;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// This class starts MM2 straight into a multiplayer session through the
/// DirectPlay lobby. The player who joins then types no address.
/// <para>
/// Fourteen runs on Windows 11 proved the sequence: DirectPlayLobbyCreateA,
/// then QueryInterface for IID_IDirectPlayLobby3A, then CreateCompoundAddress
/// with a size pass and a fill pass, then RunApplication with a DPLCONNECTION.
/// The launcher keeps that sequence and the order of the attempts.
/// </para>
/// <para>
/// WARNING: the launcher frees nothing and releases nothing after a successful
/// launch. MM2 asks DirectPlay for its settings about three to four seconds
/// later. The result carries a session, and the app holds that session for as
/// long as the game may run.
/// </para>
/// </summary>
public sealed class DirectPlayLobbyLauncher : ILobbyLauncher
{
    /// <summary>MM2 holds up to 8 players over a network.</summary>
    private const uint MaxPlayers = 8;

    /// <summary>The name of the game process, with no file extension.</summary>
    private const string GameProcessName = "midtown2";

    private readonly INativeAllocator _allocator;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<LobbyRegistration> _readRegistration;
    private readonly Func<bool> _gameIsRunning;
    private readonly bool _isWindows;
    private readonly bool _is64BitProcess;

    /// <summary>The launcher that the app builds. It talks to the real Windows.</summary>
    public DirectPlayLobbyLauncher()
        : this(
            new ProcessNativeAllocator(),
            File.Exists,
            ReadRegistration,
            GameIsRunning,
            OperatingSystem.IsWindows(),
            Environment.Is64BitProcess)
    {
    }

    internal DirectPlayLobbyLauncher(
        INativeAllocator allocator,
        Func<string, bool> fileExists,
        Func<LobbyRegistration> readRegistration,
        Func<bool> gameIsRunning,
        bool isWindows,
        bool is64BitProcess)
    {
        _allocator = allocator;
        _fileExists = fileExists;
        _readRegistration = readRegistration;
        _gameIsRunning = gameIsRunning;
        _isWindows = isWindows;
        _is64BitProcess = is64BitProcess;
    }

    /// <inheritdoc />
    public bool CanLaunch => Blocker() == LobbyLaunchFault.None;

    /// <inheritdoc />
    public LobbyLaunchResult Launch(LobbyLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trail = new List<string>();

        try
        {
            return Run(request, trail);
        }
        catch (Exception ex)
        {
            // A failure must reach the player as a sentence. The app shows the
            // message, and the report carries the type and the text of the
            // error.
            trail.Add($"The launcher caught {ex.GetType().Name}: {ex.Message}");
            return LobbyLaunchResult.Failure(LobbyLaunchFault.Unexpected, Report(trail));
        }
    }

    private LobbyLaunchFault Blocker() => LobbyLaunchPreconditions.Check(
        _isWindows,
        _is64BitProcess,
        _fileExists(DirectPlayCheck.FeatureLibrary));

    private LobbyLaunchResult Run(LobbyLaunchRequest request, List<string> trail)
    {
        trail.Add($"The player takes the role {request.Role}.");

        var blocker = Blocker();
        if (blocker != LobbyLaunchFault.None)
        {
            trail.Add($"The launcher stopped before DirectPlay, because of {blocker}.");
            return LobbyLaunchResult.Failure(blocker, Report(trail));
        }

        var registration = _readRegistration();
        trail.Add($"The registration of the game reports {registration.Fault}.");

        if (registration.Fault != LobbyRegistrationFault.None || registration.Entry is null)
        {
            return LobbyLaunchResult.Failure(FaultOf(registration.Fault), Report(trail));
        }

        trail.Add($"The key \"{registration.Entry.KeyName}\" names the game file "
            + $"{registration.Entry.GameFilePath}.");
        trail.Add($"The application GUID is {DirectPlayIds.Format(registration.Entry.ApplicationGuid)}.");

        return CallDirectPlay(request, registration.Entry, trail);
    }

    private LobbyLaunchResult CallDirectPlay(
        LobbyLaunchRequest request,
        LobbyApplicationEntry entry,
        List<string> trail)
    {
        var comStarted = StartCom(trail);
        var lobby = CreateLobby(trail);

        if (lobby == IntPtr.Zero)
        {
            trail.Add("The launcher has no lobby object, so it stopped.");
            StopCom(comStarted);
            return LobbyLaunchResult.Failure(LobbyLaunchFault.DirectPlayMissing, Report(trail));
        }

        // This holder owns the compound address and the chunks that built it.
        // It lives across every attempt, because each attempt sends the same
        // address.
        var addressBlocks = new NativeBlocks("the address", _allocator);

        try
        {
            var address = BuildAddress(lobby, addressBlocks, request.HostAddress, trail);
            var attempts = LobbyConnectionPlan.AttemptsFor(request.Role, address is not null);

            if (attempts.Count == 0)
            {
                trail.Add("The launcher found no attempt that it could make.");
                return Give(lobby, comStarted, LobbyLaunchFault.DirectPlayRefused, trail);
            }

            var lastResult = DirectPlayHResults.Pointer;

            foreach (var attempt in attempts)
            {
                var session = TryOne(
                    lobby, entry, request, attempt, address, addressBlocks, comStarted, trail, out var hr);

                if (session is not null) return LobbyLaunchResult.Success(session, Report(trail));

                lastResult = hr;
            }

            trail.Add("Every attempt failed.");
            return Give(lobby, comStarted, DirectPlayHResults.FaultFor(lastResult), trail);
        }
        finally
        {
            // A successful attempt kept these blocks, so this call frees
            // nothing. A failed launch frees every block, because no game reads
            // them.
            addressBlocks.Dispose();
        }
    }

    /// <summary>
    /// This method fills the three structs of one attempt and calls
    /// RunApplication. It returns the session after a success, and it returns
    /// null after a failure.
    /// </summary>
    private ILobbySession? TryOne(
        IntPtr lobby,
        LobbyApplicationEntry entry,
        LobbyLaunchRequest request,
        LobbyConnectionAttempt attempt,
        AddressBuffer? address,
        NativeBlocks addressBlocks,
        bool comStarted,
        List<string> trail,
        out int hr)
    {
        // This holder owns the three structs and the two strings of this one
        // attempt. A failed attempt frees them, and the next attempt starts
        // with new blocks.
        var blocks = new NativeBlocks(attempt.Label, _allocator);

        try
        {
            var sessionName = request.Role == LobbyRole.Host
                ? blocks.AnsiString(request.SessionName)
                : IntPtr.Zero;

            var session = new DpSessionDesc2
            {
                Size = (uint)Marshal.SizeOf<DpSessionDesc2>(),
                Flags = 0,
                InstanceGuid = Guid.Empty,
                ApplicationGuid = entry.ApplicationGuid,
                MaxPlayers = MaxPlayers,
                CurrentPlayers = 0,
                SessionNameAnsi = sessionName,
                PasswordAnsi = IntPtr.Zero,
            };

            var name = new DpName
            {
                Size = (uint)Marshal.SizeOf<DpName>(),
                Flags = 0,
                ShortNameAnsi = blocks.AnsiString(request.PlayerName),
                LongNameAnsi = IntPtr.Zero,
            };

            var sessionBlock = blocks.Allocate(Marshal.SizeOf<DpSessionDesc2>());
            Marshal.StructureToPtr(session, sessionBlock, false);

            var nameBlock = blocks.Allocate(Marshal.SizeOf<DpName>());
            Marshal.StructureToPtr(name, nameBlock, false);

            var connection = new DplConnection
            {
                Size = (uint)Marshal.SizeOf<DplConnection>(),
                Flags = attempt.Flags,
                SessionDesc = sessionBlock,
                PlayerName = nameBlock,
                ServiceProviderGuid = attempt.SendServiceProvider
                    ? DirectPlayIds.ServiceProviderTcpIp
                    : Guid.Empty,
                Address = attempt.SendAddress ? address?.Pointer ?? IntPtr.Zero : IntPtr.Zero,
                AddressSize = attempt.SendAddress ? address?.Size ?? 0 : 0,
            };

            var connectionBlock = blocks.Allocate(Marshal.SizeOf<DplConnection>());
            Marshal.StructureToPtr(connection, connectionBlock, false);

            hr = LobbyInterop.RunApplication(lobby, 0, out var applicationId, connectionBlock, IntPtr.Zero);
            trail.Add($"RunApplication, {attempt.Label}, with the flag 0x{attempt.Flags:X8}: "
                + DirectPlayHResults.Describe(hr));

            if (DirectPlayHResults.Failed(hr)) return null;

            trail.Add($"DirectPlay started the game. The application id is {applicationId}.");

            // The Keep calls happen here, in the same statement block as the
            // success. DirectPlay reads these blocks seconds later, and no type
            // in this file can free them after a Keep.
            var kept = new[] { addressBlocks.Keep(), blocks.Keep() };

            return OpenSession(lobby, applicationId, kept, comStarted, trail);
        }
        finally
        {
            blocks.Dispose();
        }
    }

    /// <summary>
    /// This method asks DirectPlay to signal an event when the game reaches the
    /// lobby, and it builds the session that owns the interface and the memory.
    /// </summary>
    private ILobbySession OpenSession(
        IntPtr lobby,
        uint applicationId,
        IReadOnlyList<KeptBlocks> kept,
        bool comStarted,
        List<string> trail)
    {
        // The closure below holds this event, and the session holds the
        // closure. The event therefore lives as long as the session, and the
        // garbage collector cannot take the handle away from DirectPlay.
        var reached = new ManualResetEvent(false);

        var hr = LobbyInterop.SetLobbyMessageEvent(
            lobby, 0, applicationId, reached.SafeWaitHandle.DangerousGetHandle());

        trail.Add("SetLobbyMessageEvent: " + DirectPlayHResults.Describe(hr));
        trail.Add($"The session keeps {kept.Sum(one => one.Count)} blocks of memory.");

        return new LobbyLifetime(
            lobby,
            kept,
            LobbyInterop.Release,
            _gameIsRunning,
            reached.WaitOne,
            comStarted ? Ole32.CoUninitialize : null);
    }

    /// <summary>The compound address, and its byte count.</summary>
    private sealed record AddressBuffer(IntPtr Pointer, uint Size);

    /// <summary>
    /// This method builds a TCP/IP address with CreateCompoundAddress. The
    /// first call asks for the size, and the second call fills the buffer.
    /// </summary>
    private static AddressBuffer? BuildAddress(
        IntPtr lobby,
        NativeBlocks blocks,
        string? hostAddress,
        List<string> trail)
    {
        var elements = new List<DpCompoundAddressElement>();

        var provider = DirectPlayIds.ServiceProviderTcpIp;
        var providerData = blocks.Allocate(Marshal.SizeOf<Guid>());
        Marshal.StructureToPtr(provider, providerData, false);

        elements.Add(new DpCompoundAddressElement
        {
            DataType = DirectPlayIds.ChunkServiceProvider,
            DataSize = (uint)Marshal.SizeOf<Guid>(),
            Data = providerData,
        });

        if (hostAddress is not null)
        {
            // The byte count holds the terminating zero byte.
            elements.Add(new DpCompoundAddressElement
            {
                DataType = DirectPlayIds.ChunkInet,
                DataSize = (uint)(hostAddress.Length + 1),
                Data = blocks.AnsiString(hostAddress),
            });

            trail.Add($"The address chunk holds \"{hostAddress}\".");
        }

        var elementSize = Marshal.SizeOf<DpCompoundAddressElement>();
        var block = blocks.Allocate(elementSize * elements.Count);

        for (var i = 0; i < elements.Count; i++)
        {
            Marshal.StructureToPtr(elements[i], block + (i * elementSize), false);
        }

        uint needed = 0;
        var first = LobbyInterop.CreateCompoundAddress(
            lobby, block, (uint)elements.Count, IntPtr.Zero, ref needed);

        trail.Add($"CreateCompoundAddress, the size pass: {DirectPlayHResults.Describe(first)}, "
            + $"and DirectPlay asked for {needed} bytes.");

        if (needed == 0) return null;

        var buffer = blocks.Allocate((int)needed);
        var second = LobbyInterop.CreateCompoundAddress(
            lobby, block, (uint)elements.Count, buffer, ref needed);

        trail.Add($"CreateCompoundAddress, the fill pass: {DirectPlayHResults.Describe(second)}.");

        return DirectPlayHResults.Failed(second) ? null : new AddressBuffer(buffer, needed);
    }

    /// <summary>
    /// This method creates the lobby object. Route 1 is the plain export, which
    /// needs no COM class registration. Route 2 is CoCreateInstance.
    /// </summary>
    private static IntPtr CreateLobby(List<string> trail)
    {
        var library = LobbyFactory.LoadLibrary();
        trail.Add(library == IntPtr.Zero
            ? $"Windows did not load {LobbyFactory.Library}."
            : $"Windows loaded {LobbyFactory.Library}.");

        if (library != IntPtr.Zero)
        {
            var hr = LobbyFactory.CreateByExport(library, out var version1);
            trail.Add("DirectPlayLobbyCreateA: " + DirectPlayHResults.Describe(hr));

            if (!DirectPlayHResults.Failed(hr) && version1 != IntPtr.Zero)
            {
                var upgrade = LobbyInterop.QueryInterface(
                    version1, DirectPlayIds.IidDirectPlayLobby3A, out var version3);

                trail.Add("QueryInterface(IID_IDirectPlayLobby3A): " + DirectPlayHResults.Describe(upgrade));
                LobbyInterop.Release(version1);

                if (!DirectPlayHResults.Failed(upgrade) && version3 != IntPtr.Zero) return version3;
            }
        }

        var com = LobbyFactory.CreateByCom(DirectPlayIds.IidDirectPlayLobby3A, out var lobby);
        trail.Add("CoCreateInstance(IID_IDirectPlayLobby3A): " + DirectPlayHResults.Describe(com));

        return DirectPlayHResults.Failed(com) ? IntPtr.Zero : lobby;
    }

    private static bool StartCom(List<string> trail)
    {
        var hr = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.ApartmentThreaded);
        trail.Add("CoInitializeEx(COINIT_APARTMENTTHREADED): " + DirectPlayHResults.Describe(hr));

        // A failure means that the thread already runs in another apartment.
        // The launcher then calls no CoUninitialize, because it started
        // nothing.
        return !DirectPlayHResults.Failed(hr);
    }

    private static void StopCom(bool comStarted)
    {
        if (comStarted) Ole32.CoUninitialize();
    }

    /// <summary>
    /// This method gives the lobby object back after a failure. No game runs,
    /// so nothing reads the interface.
    /// </summary>
    private static LobbyLaunchResult Give(
        IntPtr lobby,
        bool comStarted,
        LobbyLaunchFault fault,
        List<string> trail)
    {
        LobbyInterop.Release(lobby);
        StopCom(comStarted);

        return LobbyLaunchResult.Failure(fault, Report(trail));
    }

    /// <summary>
    /// This method turns a fault of the registration into the fault that the
    /// app reports. A missing key and a wrong value need different steps from
    /// the player.
    /// </summary>
    internal static LobbyLaunchFault FaultOf(LobbyRegistrationFault fault) =>
        fault == LobbyRegistrationFault.NoKey
            ? LobbyLaunchFault.GameNotRegistered
            : LobbyLaunchFault.GameRegistrationWrong;

    private static string Report(List<string> trail) => string.Join(Environment.NewLine, trail);

    private static LobbyRegistration ReadRegistration()
        => LobbyApplicationReader.ReadFromThisComputer();

    /// <summary>
    /// This method is true while a process called midtown2 runs.
    /// <para>
    /// WARNING: a failed check answers true. The session releases the
    /// DirectPlay interface only on a false answer, and a release during a race
    /// sends the player back to the menus of the game.
    /// </para>
    /// </summary>
    private static bool GameIsRunning()
    {
        try
        {
            var found = Process.GetProcessesByName(GameProcessName);

            try
            {
                return found.Length > 0;
            }
            finally
            {
                foreach (var one in found) one.Dispose();
            }
        }
        catch (Exception)
        {
            return true;
        }
    }
}
