using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Mm2Lobby.LobbyProbe;

/// <summary>
/// This program tests one question. Can a lobby program start Midtown Madness
/// 2 through the DirectPlay lobby, and give the game the host address, so the
/// player never types an IP address by hand?
/// <para>
/// The program answers nothing by itself. It prints what each step did. The
/// owner runs it one time on Windows and sends the whole output back.
/// </para>
/// <para>
/// Runs on Windows 11 taught us four facts. DirectPlay accepts the structs of
/// the probe, and RunApplication starts a process. midtown2.exe runs in the
/// WINXPSP2 compatibility mode, so Windows demands elevation, and
/// RunApplication cannot ask for it. MM2 holds a full lobby client, which a
/// strings dump of midtown2.exe proves. MM2 reaches its network code several
/// seconds after the start, and it then asks DirectPlay for the connection
/// settings.
/// </para>
/// <para>
/// WARNING: the last fact drives the design of this program. The lobby client
/// must stay alive until MM2 asks for the settings. After a successful
/// RunApplication the probe frees nothing and releases nothing. It waits.
/// </para>
/// </summary>
internal static class Program
{
    private const string PlayerNameFallback = "Probe player";
    private const string SessionName = "MM2 Lobby probe";

    // MM2 holds up to 8 players over a network. The probe states a number,
    // because 0 may mean no limit and may mean an error.
    private const uint MaxPlayers = 8;

    /// <summary>The name of the game process, with no file extension.</summary>
    private const string GameProcessName = "midtown2";

    private const int DefaultWaitSeconds = 90;
    private const int MinimumWaitSeconds = 5;
    private const int MaximumWaitSeconds = 3600;

    /// <summary>The probe prints a line of the wait at this interval.</summary>
    private const int ReportEverySeconds = 5;

    /// <summary>
    /// The event that DirectPlay signals when MM2 sends a lobby message. The
    /// field is static, so the garbage collector keeps the event alive while
    /// DirectPlay holds its handle.
    /// </summary>
    private static ManualResetEvent? _lobbyMessageEvent;

    /// <summary>
    /// The entry point. It returns 0 at all times, even after a failure. A
    /// non zero exit code could make a wrapper hide the output, and the output
    /// is the whole point of the probe.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            Log.Failure("The probe", ex);
        }

        try
        {
            PrintClosing();
        }
        catch (Exception ex)
        {
            Log.Failure("The last report of the probe", ex);
        }

        return 0;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Console.WriteLine();
        Console.WriteLine("PROBLEM: a thread of the probe did not catch an exception.");
        if (e.ExceptionObject is Exception ex) Log.Failure("A thread of the probe", ex);
        else Console.WriteLine($"The object was {e.ExceptionObject}.");
    }

    private static void Run(string[] args)
    {
        Log.Heading("MM2 Lobby: the DirectPlay lobby probe");
        PrintPurpose();

        var joinAddress = ReadArguments(args, out var applicationFilter, out var waitSeconds);
        var selfTest = args.Any(one => one.Equals("--selftest", StringComparison.OrdinalIgnoreCase));

        Log.Step("The probe reports the computer.");
        PrintEnvironment();

        if (selfTest)
        {
            SelfTest(waitSeconds);
            return;
        }

        Log.Step("The probe looks for dplayx.dll.");
        PrintDirectPlayFiles();

        Log.Step("The probe prints the GUID values that it will use.");
        Log.Info("Runs on Windows 11 validated these values. The probe prints them for the record.");
        DirectPlayIds.PrintAll();

        Log.Step("The probe prints the struct layouts that it will use.");
        Log.Info(
            "Runs on Windows 11 validated these layouts. The probe sets each size "
            + "field from Marshal.SizeOf, so the numbers follow the build.");
        StructReport.PrintAll();
        Log.Item("DPLCONNECTION_CREATESESSION", $"0x{LobbyConnectionFlags.CreateSession:X8}");
        Log.Item("DPLCONNECTION_JOINSESSION", $"0x{LobbyConnectionFlags.JoinSession:X8}");

        if (joinAddress is not null)
        {
            Log.Guess(
                "DPLCONNECTION_JOINSESSION is 1. No run has validated that number, "
                + "because every run so far used the host case.");
        }

        Log.Step("The probe dumps the DirectPlay Applications key of the registry.");
        var applications = ReadRegistry();

        Log.Step("The probe chooses the application key of MM2.");
        var game = ChooseApplication(applications, applicationFilter);
        var applicationGuid = ReadApplicationGuid(game);
        ReportGameFile(game);

        Log.Step("The probe starts COM.");
        StartCom();

        Log.Step("The probe creates the lobby object.");
        var library = LobbyFactory.LoadLibrary();
        var lobby = CreateLobby(library);

        if (lobby == IntPtr.Zero)
        {
            Log.Problem(
                "The probe has no lobby object, so it stops here. Every later "
                + "step needs the object. Send this output back.");
            Log.Problem("The probe did not reach CreateCompoundAddress and did not reach RunApplication.");
            StopCom();
            return;
        }

        // This holder owns the compound address and the chunks that built it.
        // It lives across every attempt, because each attempt sends the same
        // address.
        var addressMemory = new MemoryHolder("the address");
        var started = false;

        try
        {
            Log.Step("The probe builds the TCP/IP address.");
            var address = BuildAddress(lobby, addressMemory, joinAddress);

            Log.Step("The probe calls RunApplication.");
            started = Attempt(lobby, applicationGuid, address, joinAddress, waitSeconds);
        }
        finally
        {
            if (started)
            {
                Log.Step("The probe ends the lobby session.");

                // The probe keeps the memory even here. Windows reclaims it
                // when the process ends, a free buys nothing, and a free could
                // take the connection settings away from a running game.
                addressMemory.Keep("Windows reclaims the memory when the probe ends.");
                addressMemory.FreeAll();

                if (IsGameRunning())
                {
                    Log.Problem(
                        "MM2 still runs, so the probe does not release the lobby "
                        + "object and does not call CoUninitialize. MM2 may hold a "
                        + "lobby session. The end of this process releases both.");
                }
                else
                {
                    Log.Info("MM2 does not run, so the probe can release the lobby object.");

                    try
                    {
                        var count = LobbyInterop.Release(lobby);
                        Log.Info($"Release left a reference count of {count}.");
                    }
                    catch (Exception ex)
                    {
                        Log.Failure("Release", ex);
                    }

                    StopCom();
                }
            }
            else
            {
                Log.Step("The probe frees the memory and releases the lobby object.");
                Log.Info("RunApplication started no game, so nothing reads this memory.");
                addressMemory.FreeAll();

                try
                {
                    var count = LobbyInterop.Release(lobby);
                    Log.Info($"Release left a reference count of {count}.");
                }
                catch (Exception ex)
                {
                    Log.Failure("Release", ex);
                }

                StopCom();
            }
        }
    }

    private static void StopCom()
    {
        try
        {
            Ole32.CoUninitialize();
            Log.Info("The probe called CoUninitialize.");
        }
        catch (Exception ex)
        {
            Log.Failure("CoUninitialize", ex);
        }
    }

    private static void PrintPurpose()
    {
        Log.Info("The probe tries to start MM2 through the DirectPlay lobby.");
        Log.Info("The probe changes nothing. It reads the registry and it calls DirectPlay.");
        Log.Info("A failure prints a code and stops.");
        Log.Info("A success starts the game, and then the probe waits and stays alive.");
        Log.Info("MM2 asks DirectPlay for the connection settings seconds after it starts.");
        Log.Info("The lobby client must still hold those settings at that moment.");
        Log.Info("Watch the screen during the wait. Report what MM2 showed.");
        Log.Info("Send the whole output back, from the first line to the last line.");
        Console.WriteLine();
        Log.Info("Use:");
        Log.Info("  Mm2Lobby.LobbyProbe.exe                 the probe hosts a session");
        Log.Info("  Mm2Lobby.LobbyProbe.exe 10.147.17.42    the probe joins that address");
        Log.Info("  Mm2Lobby.LobbyProbe.exe --app \"Name\"     the probe uses that registry key");
        Log.Info($"  Mm2Lobby.LobbyProbe.exe --wait 180      the probe waits 180 seconds, not {DefaultWaitSeconds}");
        Log.Info("  Mm2Lobby.LobbyProbe.exe --selftest     the probe tests itself and calls no DirectPlay");
        Console.WriteLine();
        Log.Problem(
            "Start the probe as an administrator. Right click the exe and choose "
            + "Run as administrator. midtown2.exe runs in the WINXPSP2 "
            + "compatibility mode, so Windows demands elevation.");
    }

    /// <summary>
    /// This method reads the wait in seconds. It keeps the default after bad
    /// text, and it holds the number inside the limits.
    /// </summary>
    private static int ReadWait(string text, int fallback)
    {
        if (!int.TryParse(text, out var seconds))
        {
            Log.Problem($"The probe cannot read \"{text}\" as a number of seconds.");
            return fallback;
        }

        if (seconds < MinimumWaitSeconds)
        {
            Log.Problem($"The probe raises the wait to {MinimumWaitSeconds} seconds.");
            return MinimumWaitSeconds;
        }

        if (seconds > MaximumWaitSeconds)
        {
            Log.Problem($"The probe lowers the wait to {MaximumWaitSeconds} seconds.");
            return MaximumWaitSeconds;
        }

        return seconds;
    }

    private static string? ReadArguments(
        string[] args,
        out string? applicationFilter,
        out int waitSeconds)
    {
        applicationFilter = null;
        waitSeconds = DefaultWaitSeconds;
        string? joinAddress = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--app", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                applicationFilter = args[++i];
                continue;
            }

            if (arg.StartsWith("--app=", StringComparison.OrdinalIgnoreCase))
            {
                applicationFilter = arg["--app=".Length..];
                continue;
            }

            if (arg.Equals("--wait", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                waitSeconds = ReadWait(args[++i], waitSeconds);
                continue;
            }

            if (arg.StartsWith("--wait=", StringComparison.OrdinalIgnoreCase))
            {
                waitSeconds = ReadWait(arg["--wait=".Length..], waitSeconds);
                continue;
            }

            if (arg.Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            {
                // Run reads this switch on its own.
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                Log.Problem($"The probe does not know the switch \"{arg}\". It ignores the switch.");
                continue;
            }

            joinAddress = arg;
        }

        Console.WriteLine();
        if (joinAddress is null)
        {
            Log.Info("Mode: the probe hosts a session. It needs no second computer.");
        }
        else
        {
            Log.Info($"Mode: the probe joins the address \"{joinAddress}\".");
            if (!IPAddress.TryParse(joinAddress, out _))
            {
                Log.Problem(
                    "That text is not an IP address. DirectPlay takes a name too, "
                    + "so the probe sends the text as it stands.");
            }
        }

        if (applicationFilter is not null)
        {
            Log.Info($"The probe looks for the registry key \"{applicationFilter}\".");
        }

        Log.Info(
            $"After a success the probe waits {waitSeconds} seconds, and it frees nothing.");

        return joinAddress;
    }

    private static void PrintEnvironment()
    {
        Log.Item("The operating system", RuntimeInformation.OSDescription);
        Log.Item("The version", Environment.OSVersion.ToString());
        Log.Item("The architecture of the computer", RuntimeInformation.OSArchitecture.ToString());
        Log.Item("The architecture of this process", RuntimeInformation.ProcessArchitecture.ToString());
        Log.Item("A 64 bit process", Environment.Is64BitProcess.ToString());
        Log.Item("The .NET version", RuntimeInformation.FrameworkDescription);
        Log.Item("The pointer size in bytes", IntPtr.Size.ToString());
        Log.Item("The user", Environment.UserName);
        Log.Item("The computer", Environment.MachineName);
        Log.Item("The program file", Environment.ProcessPath ?? "(unknown)");

        if (!OperatingSystem.IsWindows())
        {
            Log.Problem(
                "This computer does not run Windows. DirectPlay exists on Windows "
                + "only, so every later step will fail. Run the probe on the game computer.");
            return;
        }

        var elevated = IsElevated();
        Log.Item("An administrator runs the probe", elevated?.ToString() ?? "unknown");

        // WARNING: this test guards every later step. An unelevated run cannot
        // start the game, so the owner must stop and start the probe again.
        if (elevated == false)
        {
            Console.WriteLine();
            Log.Problem("STOP. An administrator does not run the probe.");
            Log.Problem(
                "midtown2.exe runs in the WINXPSP2 compatibility mode, so Windows "
                + "demands elevation for the game.");
            Log.Problem(
                "RunApplication calls CreateProcess, and CreateProcess cannot ask "
                + "the user for permission. The call will fail.");
            Log.Problem(
                "Close this window. Right click Mm2Lobby.LobbyProbe.exe and choose "
                + "Run as administrator.");
            Log.Problem("The probe goes on, and it will report the failure code.");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// This method is true when an administrator runs the probe. It is null
    /// when the check itself failed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool? IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            Log.Failure("The check of the administrator rights", ex);
            return null;
        }
    }

    /// <summary>
    /// This method is true when a process called midtown2 runs. The probe
    /// reports the answer and does not act on it.
    /// </summary>
    private static bool IsGameRunning()
    {
        try
        {
            var found = Process.GetProcessesByName(GameProcessName);
            foreach (var one in found) one.Dispose();

            return found.Length > 0;
        }
        catch (Exception ex)
        {
            Log.Failure($"The search for the process {GameProcessName}", ex);
            return false;
        }
    }

    private static void PrintDirectPlayFiles()
    {
        // A 64 bit program loads the copy in System32. A 32 bit program loads
        // the copy in SysWOW64. Windows redirects the path of a 32 bit program.
        string[] paths =
        [
            @"C:\Windows\System32\dplayx.dll",
            @"C:\Windows\SysWOW64\dplayx.dll",
            @"C:\Windows\System32\dpnet.dll",
        ];

        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Log.Item(path, "the file does not exist");
                    continue;
                }

                var info = new FileInfo(path);
                var version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "(no version)";
                Log.Item(path, $"{info.Length} bytes, version {version}");
            }
            catch (Exception ex)
            {
                Log.Item(path, $"the check threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Log.Info(
            "The probe loads dplayx.dll by its plain name, so Windows picks the "
            + "copy that matches the bit width of the probe.");
    }

    private static List<LobbyApplication> ReadRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            Log.Problem("This computer has no Windows registry. The probe skips the dump.");
            return [];
        }

        Log.Info("The probe prints every value name and every value, verbatim.");
        var found = RegistryDump.DumpAll();
        Log.Info($"The probe found {found.Count} application keys in total.");
        return found;
    }

    private static LobbyApplication? ChooseApplication(
        List<LobbyApplication> applications,
        string? filter)
    {
        if (applications.Count == 0)
        {
            Log.Problem("The probe found no application key, so it has no application GUID.");
            return null;
        }

        if (filter is not null)
        {
            var named = applications.FirstOrDefault(
                one => one.KeyName.Equals(filter, StringComparison.OrdinalIgnoreCase));

            if (named is not null)
            {
                Log.Good($"The probe uses the key \"{named.KeyName}\", because you named it.");
                return named;
            }

            Log.Problem($"The probe found no key called \"{filter}\".");
        }

        var match = applications.FirstOrDefault(
            one => one.KeyName.Contains("midtown", StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            Log.Guess($"The key \"{match.KeyName}\" is MM2, because the name holds \"midtown\".");
            Log.Info($"The full path is {match.KeyPath}.");
            return match;
        }

        Log.Problem(
            "No key name holds the word \"midtown\". Read the dump above, find the "
            + "key of MM2, and run the probe again with --app \"that name\".");
        return null;
    }

    private static Guid ReadApplicationGuid(LobbyApplication? game)
    {
        if (game is null)
        {
            Log.Problem(
                "The probe has no application GUID. It sends an empty GUID, and "
                + "DirectPlay will probably reject it. The call still teaches us the code.");
            return Guid.Empty;
        }

        // The registration writes the GUID under a value called Guid. The
        // author did not verify that name, so the probe tries the name first
        // and then tries every value.
        if (game.Values.TryGetValue("Guid", out var direct) && Guid.TryParse(direct, out var parsed))
        {
            Log.Good($"The value \"Guid\" holds {DirectPlayIds.Format(parsed)}.");
            return parsed;
        }

        Log.Problem("No value called \"Guid\" holds a GUID. The probe tries every value.");

        foreach (var pair in game.Values)
        {
            if (!Guid.TryParse(pair.Value, out var any)) continue;

            Log.Good($"The value \"{pair.Key}\" holds the GUID {DirectPlayIds.Format(any)}.");
            Log.Guess($"The application GUID comes from the value \"{pair.Key}\", not from \"Guid\".");
            return any;
        }

        Log.Problem("No value of the key holds a GUID. The probe sends an empty GUID.");
        return Guid.Empty;
    }

    /// <summary>
    /// This method checks the game file that the registry names. DirectPlay
    /// reports a process error when the file is missing, so the owner needs to
    /// know whether the path is right.
    /// </summary>
    private static void ReportGameFile(LobbyApplication? game)
    {
        if (game is null) return;

        string[] names = ["Path", "File", "CommandLine", "CurrentDirectory"];
        Log.Guess(
            "The value names of the key are Path, File, CommandLine and "
            + "CurrentDirectory. The dump above shows the real names.");

        foreach (var name in names)
        {
            if (!game.Values.TryGetValue(name, out var value))
            {
                Log.Item(name, "the key has no such value");
                continue;
            }

            Log.Item(name, $"\"{value}\"");
        }

        try
        {
            if (game.Values.TryGetValue("Path", out var folder)
                && game.Values.TryGetValue("File", out var file)
                && !string.IsNullOrWhiteSpace(folder)
                && !string.IsNullOrWhiteSpace(file))
            {
                var full = Path.Combine(folder, file);
                Log.Item($"Does {full} exist", File.Exists(full).ToString());
            }
        }
        catch (Exception ex)
        {
            Log.Failure("The check of the game file", ex);
        }
    }

    private static void StartCom()
    {
        try
        {
            // Main carries STAThread, so .NET may have started COM already.
            // S_FALSE then means that COM was already up on this thread.
            var hr = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.CoinitApartmentThreaded);
            Log.Result("CoInitializeEx(COINIT_APARTMENTTHREADED)", hr);
        }
        catch (Exception ex)
        {
            Log.Failure("CoInitializeEx", ex);
        }
    }

    /// <summary>
    /// This method creates the lobby object. It tries the plain export first,
    /// because the export needs no class registration and no COM activation.
    /// It tries CoCreateInstance second, so one run tests both routes.
    /// </summary>
    private static IntPtr CreateLobby(IntPtr library)
    {
        if (library != IntPtr.Zero)
        {
            try
            {
                Log.Info(
                    "Route 1 is the export DirectPlayLobbyCreateA. The probe tries "
                    + "this route first, because it needs no COM class registration.");

                var hr = LobbyFactory.CreateByExport(library, out var version1);
                Log.Result("DirectPlayLobbyCreateA", hr);

                if (hr >= 0 && version1 != IntPtr.Zero)
                {
                    Log.Good($"Route 1 gave an IDirectPlayLobbyA at 0x{version1.ToInt64():X16}.");
                    var upgraded = Upgrade(version1);
                    if (upgraded != IntPtr.Zero) return upgraded;
                }
            }
            catch (Exception ex)
            {
                Log.Failure("Route 1, the export DirectPlayLobbyCreateA", ex);
            }
        }

        try
        {
            Log.Info("Route 2 is CoCreateInstance of CLSID_DirectPlayLobby for IID_IDirectPlayLobby3A.");
            var hr = LobbyFactory.CreateByCom(DirectPlayIds.IidDirectPlayLobby3A, out var lobby);
            Log.Result("CoCreateInstance(IID_IDirectPlayLobby3A)", hr);

            if (hr >= 0 && lobby != IntPtr.Zero)
            {
                Log.Good($"Route 2 gave an IDirectPlayLobby3A at 0x{lobby.ToInt64():X16}.");
                return lobby;
            }
        }
        catch (Exception ex)
        {
            Log.Failure("Route 2, CoCreateInstance", ex);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// This method asks the version 1 object for the version 3 interface. The
    /// probe needs version 3, because CreateCompoundAddress arrived in version
    /// 2. It releases the version 1 object either way.
    /// </summary>
    private static IntPtr Upgrade(IntPtr version1)
    {
        var result = IntPtr.Zero;

        try
        {
            var hr = LobbyInterop.QueryInterface(version1, DirectPlayIds.IidDirectPlayLobby3A, out var version3);
            Log.Result("QueryInterface(IID_IDirectPlayLobby3A)", hr);

            if (hr >= 0 && version3 != IntPtr.Zero)
            {
                Log.Good($"The object gave an IDirectPlayLobby3A at 0x{version3.ToInt64():X16}.");
                result = version3;
            }
            else
            {
                Log.Problem(
                    "The object has no version 3 interface. Check the GUID "
                    + "IID_IDirectPlayLobby3A above. The probe will try route 2.");
            }
        }
        catch (Exception ex)
        {
            Log.Failure("QueryInterface", ex);
        }

        try
        {
            LobbyInterop.Release(version1);
        }
        catch (Exception ex)
        {
            Log.Failure("The release of the version 1 object", ex);
        }

        return result;
    }

    /// <summary>The compound address, and its byte count.</summary>
    private sealed record AddressBuffer(IntPtr Pointer, uint Size);

    /// <summary>
    /// This method builds a TCP/IP address with CreateCompoundAddress. The
    /// first call asks for the size. The second call fills the buffer.
    /// <para>
    /// Every block goes into the holder of the caller. The caller frees the
    /// blocks after a failure, and it keeps them after a success.
    /// </para>
    /// </summary>
    private static AddressBuffer? BuildAddress(
        IntPtr lobby,
        MemoryHolder memory,
        string? joinAddress)
    {
        try
        {
            var elements = new List<DpCompoundAddressElement>();

            var providerGuid = DirectPlayIds.DpSpGuidTcpIp;
            var providerData = memory.Alloc(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(providerGuid, providerData, false);

            elements.Add(new DpCompoundAddressElement
            {
                DataType = DirectPlayIds.DpAidServiceProvider,
                DataSize = (uint)Marshal.SizeOf<Guid>(),
                Data = providerData,
            });
            Log.Info("Chunk 1 is DPAID_ServiceProvider, and it holds DPSPGUID_TCPIP, 16 bytes.");

            if (joinAddress is not null)
            {
                var text = memory.AnsiString(joinAddress);

                // The byte count holds the terminating zero byte.
                var size = (uint)(joinAddress.Length + 1);

                elements.Add(new DpCompoundAddressElement
                {
                    DataType = DirectPlayIds.DpAidInet,
                    DataSize = size,
                    Data = text,
                });

                Log.Info($"Chunk 2 is DPAID_INet, and it holds \"{joinAddress}\", {size} bytes.");
                Log.Guess(
                    "The byte count of the DPAID_INet chunk holds the terminating "
                    + "zero byte. A count without the zero byte is the other choice.");
                Log.Guess(
                    "The probe sends no DPAID_INetPort chunk. MM2 then uses the "
                    + "default DirectPlay port.");
            }

            var elementSize = Marshal.SizeOf<DpCompoundAddressElement>();
            var block = memory.Alloc(elementSize * elements.Count);

            for (var i = 0; i < elements.Count; i++)
            {
                Marshal.StructureToPtr(elements[i], block + (i * elementSize), false);
            }

            uint needed = 0;
            var first = LobbyInterop.CreateCompoundAddress(
                lobby, block, (uint)elements.Count, IntPtr.Zero, ref needed);

            Log.Result("CreateCompoundAddress, the size pass", first);
            Log.Info($"DirectPlay asked for {needed} bytes.");
            Log.Info(
                "The size pass should report DPERR_BUFFERTOOSMALL, "
                + $"{HResults.Describe(HResults.DpErrBufferTooSmall)}.");

            if (needed == 0)
            {
                Log.Problem(
                    "DirectPlay reported no size, so the probe cannot build the "
                    + "address. Check the chunk GUID values and the element layout.");
                return null;
            }

            var buffer = memory.Alloc((int)needed);
            var second = LobbyInterop.CreateCompoundAddress(
                lobby, block, (uint)elements.Count, buffer, ref needed);

            Log.Result("CreateCompoundAddress, the fill pass", second);

            if (second < 0)
            {
                Log.Problem("DirectPlay did not fill the address. The caller frees the buffer.");
                return null;
            }

            Log.Good($"The address holds {needed} bytes.");
            PrintBytes(buffer, needed);
            return new AddressBuffer(buffer, needed);
        }
        catch (Exception ex)
        {
            Log.Failure("The build of the compound address", ex);
            return null;
        }
    }

    private static void PrintBytes(IntPtr buffer, uint size)
    {
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, (int)size);
            Log.Info("The address, as hex:");

            for (var offset = 0; offset < bytes.Length; offset += 16)
            {
                var line = bytes.Skip(offset).Take(16).ToArray();
                Log.Item($"{offset:X4}", Convert.ToHexString(line));
            }
        }
        catch (Exception ex)
        {
            Log.Failure("The print of the address bytes", ex);
        }
    }

    /// <summary>
    /// This method calls RunApplication up to three times. Each call changes
    /// one choice, so one run tests three choices. The method stops at the
    /// first success, because a success starts the game.
    /// </summary>
    private static bool Attempt(
        IntPtr lobby,
        Guid applicationGuid,
        AddressBuffer? address,
        string? joinAddress,
        int waitSeconds)
    {
        var flags = joinAddress is null
            ? LobbyConnectionFlags.CreateSession
            : LobbyConnectionFlags.JoinSession;

        var flagName = joinAddress is null
            ? "DPLCONNECTION_CREATESESSION"
            : "DPLCONNECTION_JOINSESSION";

        Log.Info($"The probe sends the flag {flagName}, 0x{flags:X8}.");
        Log.Item("The application GUID", DirectPlayIds.Format(applicationGuid));

        if (applicationGuid == Guid.Empty)
        {
            Log.Problem(
                "The application GUID is empty. DirectPlay will probably report "
                + "an unknown application.");
        }

        if (address is null)
        {
            Log.Problem("The probe has no compound address. Attempt 1 and attempt 3 will skip.");
        }

        // Attempt 1 sends both the service provider GUID and the address.
        // Attempt 2 sends the GUID alone. Attempt 3 sends the address alone.
        // The documentation is not clear on which of the two fields wins, so
        // the probe tries all three.
        Log.Guess(
            "DPLCONNECTION takes guidSP and lpAddress together. The probe tries "
            + "both fields, then each field alone.");

        if (address is not null
            && Call("attempt 1, guidSP and lpAddress together",
                lobby, flags, applicationGuid, DirectPlayIds.DpSpGuidTcpIp, address, joinAddress, waitSeconds))
        {
            return true;
        }

        if (joinAddress is null)
        {
            if (Call("attempt 2, guidSP alone and no address",
                lobby, flags, applicationGuid, DirectPlayIds.DpSpGuidTcpIp, null, null, waitSeconds))
            {
                return true;
            }
        }
        else
        {
            Log.Info(
                "The probe skips attempt 2 in the join mode. Attempt 2 drops the "
                + "address, and a join needs the address.");
        }

        if (address is not null
            && Call("attempt 3, lpAddress alone and an empty guidSP",
                lobby, flags, applicationGuid, Guid.Empty, address, joinAddress, waitSeconds))
        {
            return true;
        }

        Log.Problem("Every attempt failed. Read the codes above, from attempt 1 down.");
        return false;
    }

    /// <summary>
    /// This method fills the three structs and calls RunApplication one time.
    /// It returns true when DirectPlay reports success.
    /// </summary>
    private static bool Call(
        string label,
        IntPtr lobby,
        uint flags,
        Guid applicationGuid,
        Guid serviceProvider,
        AddressBuffer? address,
        string? joinAddress,
        int waitSeconds)
    {
        Console.WriteLine();
        Log.Info($"--- {label} ---");

        // This holder owns the three structs and the two strings of this one
        // attempt. A failed attempt frees them, and the next attempt starts
        // with new blocks. A successful attempt keeps them.
        var memory = new MemoryHolder(label);

        try
        {
            var playerName = Environment.UserName;
            if (string.IsNullOrWhiteSpace(playerName)) playerName = PlayerNameFallback;

            var playerText = memory.AnsiString(playerName);

            var sessionText = IntPtr.Zero;
            if (joinAddress is null)
            {
                sessionText = memory.AnsiString(SessionName);
            }
            else
            {
                Log.Info("The probe sends no session name, because a join needs none.");
            }

            var session = new DpSessionDesc2
            {
                Size = (uint)Marshal.SizeOf<DpSessionDesc2>(),
                Flags = 0,
                InstanceGuid = Guid.Empty,
                ApplicationGuid = applicationGuid,
                MaxPlayers = MaxPlayers,
                CurrentPlayers = 0,
                SessionNameAnsi = sessionText,
                PasswordAnsi = IntPtr.Zero,
            };

            var name = new DpName
            {
                Size = (uint)Marshal.SizeOf<DpName>(),
                Flags = 0,
                ShortNameAnsi = playerText,
                LongNameAnsi = IntPtr.Zero,
            };

            var sessionBlock = memory.Alloc(Marshal.SizeOf<DpSessionDesc2>());
            Marshal.StructureToPtr(session, sessionBlock, false);

            var nameBlock = memory.Alloc(Marshal.SizeOf<DpName>());
            Marshal.StructureToPtr(name, nameBlock, false);

            var connection = new DplConnection
            {
                Size = (uint)Marshal.SizeOf<DplConnection>(),
                Flags = flags,
                SessionDesc = sessionBlock,
                PlayerName = nameBlock,
                ServiceProviderGuid = serviceProvider,
                Address = address?.Pointer ?? IntPtr.Zero,
                AddressSize = address?.Size ?? 0,
            };

            var connectionBlock = memory.Alloc(Marshal.SizeOf<DplConnection>());
            Marshal.StructureToPtr(connection, connectionBlock, false);

            Log.Item("DPLCONNECTION.dwSize", connection.Size.ToString());
            Log.Item("DPLCONNECTION.dwFlags", $"0x{connection.Flags:X8}");
            Log.Item("DPLCONNECTION.guidSP", DirectPlayIds.Format(connection.ServiceProviderGuid));
            Log.Item("DPLCONNECTION.dwAddressSize", connection.AddressSize.ToString());
            Log.Item("DPSESSIONDESC2.dwSize", session.Size.ToString());
            Log.Item("DPSESSIONDESC2.dwMaxPlayers", session.MaxPlayers.ToString());
            Log.Item("DPNAME.dwSize", name.Size.ToString());
            Log.Item("The player name", playerName);

            var hr = LobbyInterop.RunApplication(lobby, 0, out var applicationId, connectionBlock, IntPtr.Zero);
            Log.Result($"RunApplication, {label}", hr);

            if (hr >= 0)
            {
                Log.Good($"DirectPlay started the application. The application id is {applicationId}.");

                // WARNING: do not free this memory now. MM2 reaches its
                // network code several seconds later, and DirectPlay may read
                // these structs at that moment.
                memory.Keep("MM2 has not asked for the connection settings yet.");

                RegisterMessageEvent(lobby, applicationId);
                Wait(waitSeconds, afterLaunch: true);
                return true;
            }

            Log.Problem($"DirectPlay did not start the application. The application id stands at {applicationId}.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Failure($"RunApplication, {label}", ex);
            return false;
        }
        finally
        {
            memory.FreeAll();
        }
    }

    /// <summary>
    /// This method tests the parts of the probe that need no DirectPlay: the
    /// wait, the report of the game process and the memory holder. The author
    /// runs it on macOS, because the author cannot run DirectPlay.
    /// <para>
    /// WARNING: this method calls nothing in dplayx.dll. A pass here says
    /// nothing about DirectPlay and nothing about MM2.
    /// </para>
    /// </summary>
    private static void SelfTest(int waitSeconds)
    {
        Log.Heading("The self test of the probe");
        Log.Problem(
            "This run tests the probe and not DirectPlay. It calls nothing in "
            + "dplayx.dll, and it starts no game.");

        Log.Step("The probe tests the memory holder.");
        var keeper = new MemoryHolder("a kept block");
        keeper.Alloc(64);
        keeper.AnsiString("a test string");
        keeper.Keep("The self test asked the probe to keep them.");
        keeper.FreeAll();

        var freer = new MemoryHolder("a freed block");
        freer.Alloc(64);
        freer.AnsiString("a test string");
        freer.FreeAll();

        Wait(waitSeconds, afterLaunch: false);
    }

    /// <summary>
    /// This method asks DirectPlay to signal an event when MM2 sends a lobby
    /// message. The probe does not read the message. The signal alone answers
    /// one question: did MM2 reach the lobby?
    /// <para>
    /// The method runs after RunApplication reported success, so it cannot
    /// change that result. A failure here prints a code and the wait goes on.
    /// </para>
    /// </summary>
    private static void RegisterMessageEvent(IntPtr lobby, uint applicationId)
    {
        try
        {
            // The probe takes the handle from a .NET event, so it needs no
            // call to CreateEvent and no new P/Invoke.
            _lobbyMessageEvent = new ManualResetEvent(false);
            var handle = _lobbyMessageEvent.SafeWaitHandle.DangerousGetHandle();
            Log.Info($"The probe made an event. The handle is 0x{handle.ToInt64():X16}.");

            var hr = LobbyInterop.SetLobbyMessageEvent(lobby, 0, applicationId, handle);
            Log.Result("SetLobbyMessageEvent", hr);

            if (hr >= 0)
            {
                Log.Good("DirectPlay took the event. A signal means that MM2 reached the lobby.");
            }
            else
            {
                Log.Problem(
                    "DirectPlay did not take the event. The wait goes on, and the "
                    + "probe reports no message. Read the note on slot 13 and on "
                    + "the order of the arguments.");
                _lobbyMessageEvent = null;
            }
        }
        catch (Exception ex)
        {
            Log.Failure("SetLobbyMessageEvent", ex);
            _lobbyMessageEvent = null;
        }
    }

    /// <summary>
    /// This method keeps the lobby client alive. MM2 reaches its network code
    /// several seconds after the start, and it then asks DirectPlay for the
    /// connection settings. A lobby that ends first gives the game nothing.
    /// <para>
    /// The method prints a line at an interval, and it reports the game
    /// process and the lobby message event at each line.
    /// </para>
    /// <param name="afterLaunch">
    /// True after RunApplication started a game. The self test sets it false,
    /// and the method then expects no game process.
    /// </param>
    /// </summary>
    private static void Wait(int seconds, bool afterLaunch)
    {
        Console.WriteLine();
        Log.Step($"The probe stays alive for {seconds} seconds.");

        if (afterLaunch)
        {
            Log.Info("The probe holds the lobby object, the connection settings and the address.");
            Log.Info("Look at the screen now. Watch what MM2 does, and report it.");
        }
        else
        {
            Log.Info("The probe holds no lobby object on this run, because the self test made none.");
        }

        Log.Info("Press Ctrl and C together to end the probe early.");
        Console.WriteLine();

        var clock = Stopwatch.StartNew();
        var nextReport = 0;
        var sawGame = false;
        var sawMessage = false;

        try
        {
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                var running = IsGameRunning();
                if (running) sawGame = true;

                var signalled = _lobbyMessageEvent?.WaitOne(0) ?? false;

                // The probe reports a new signal at once, and it reports the
                // time at the interval.
                if (signalled && !sawMessage)
                {
                    sawMessage = true;
                    Log.Good(
                        $"At {clock.Elapsed.TotalSeconds:F0} seconds MM2 sent a lobby "
                        + "message. MM2 reached the DirectPlay lobby.");
                }

                var elapsed = (int)clock.Elapsed.TotalSeconds;
                if (elapsed >= nextReport)
                {
                    var game = running ? "runs" : "does not run";
                    var message = sawMessage ? "yes" : "no";
                    Log.Info(
                        $"{elapsed,4} s of {seconds} s. The process {GameProcessName} "
                        + $"{game}. A lobby message arrived: {message}.");
                    nextReport = elapsed + ReportEverySeconds;
                }

                Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            Log.Failure("The wait", ex);
        }

        Console.WriteLine();
        Log.Step("The probe ends the wait.");
        Log.Item("The length of the wait in seconds", $"{clock.Elapsed.TotalSeconds:F0}");
        Log.Item($"The process {GameProcessName} runs now", IsGameRunning().ToString());
        Log.Item($"The process {GameProcessName} ran at some time", sawGame.ToString());
        Log.Item("MM2 sent a lobby message", sawMessage.ToString());

        if (!sawGame && afterLaunch)
        {
            Log.Problem(
                "The probe never saw the game process. RunApplication reported "
                + "success, so DirectPlay started something else, or the process "
                + "carries another name. Report the name that Task Manager shows.");
        }
    }

    private static void PrintClosing()
    {
        Log.Heading("The choices that no run has validated yet");
        Log.Info(
            "Runs on Windows 11 validated the GUID values, the vtable slots, the "
            + "struct layouts and the host flag. Each line below is a choice that "
            + "no run has tested.");
        Console.WriteLine();

        var guesses = Log.AllGuesses;
        if (guesses.Count == 0)
        {
            Log.Info("The probe printed no untested choice on this run.");
        }

        for (var i = 0; i < guesses.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {guesses[i]}");
        }

        Console.WriteLine();
        Log.Info("These three parts also wait for a first run:");
        Log.Info("  1. The wait itself. Does a live lobby client give MM2 its settings?");
        Log.Info("  2. The slot of SetLobbyMessageEvent, slot 13, and the order of its arguments.");
        Log.Info("  3. The error code numbers. A success teaches us none of them.");

        Log.Heading("The end of the probe");
        Log.Info("The probe reached the last line. It did not crash.");
        Log.Info("Send every line of this output back, with what the screen showed.");
    }
}
