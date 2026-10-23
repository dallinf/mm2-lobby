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
/// </summary>
internal static class Program
{
    private const string PlayerNameFallback = "Probe player";
    private const string SessionName = "MM2 Lobby probe";

    // MM2 holds up to 8 players over a network. The probe states a number,
    // because 0 may mean no limit and may mean an error.
    private const uint MaxPlayers = 8;

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

        var joinAddress = ReadArguments(args, out var applicationFilter);

        Log.Step("The probe reports the computer.");
        PrintEnvironment();

        Log.Step("The probe looks for dplayx.dll.");
        PrintDirectPlayFiles();

        Log.Step("The probe prints the GUID values that it will use.");
        Log.Problem(
            "Every GUID below comes from the memory of the author. The author "
            + "could not open dplay.h or dplobby.h. Check each value.");
        DirectPlayIds.PrintAll();

        Log.Step("The probe prints the struct layouts that it will use.");
        Log.Problem(
            "Every layout below comes from the memory of the author. "
            + "DirectPlay rejects a struct when the size field is wrong.");
        StructReport.PrintAll();
        Log.Item("DPLCONNECTION_CREATESESSION", $"0x{LobbyConnectionFlags.CreateSession:X8}");
        Log.Item("DPLCONNECTION_JOINSESSION", $"0x{LobbyConnectionFlags.JoinSession:X8}");
        Log.Guess(
            "DPLCONNECTION_CREATESESSION is 2 and DPLCONNECTION_JOINSESSION is 1. "
            + "dplobby.h sets them to DPOPEN_CREATE and DPOPEN_JOIN.");

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

        AddressBuffer? address = null;

        try
        {
            Log.Step("The probe builds the TCP/IP address.");
            address = BuildAddress(lobby, joinAddress);

            Log.Step("The probe calls RunApplication.");
            Attempt(lobby, applicationGuid, address, joinAddress);
        }
        finally
        {
            Log.Step("The probe frees the memory and releases the lobby object.");

            // The probe frees the address after RunApplication. DirectPlay
            // reads the address during the call and does not keep it.
            if (address is not null) Marshal.FreeHGlobal(address.Pointer);

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
        Log.Info("A success starts the game. A failure prints a code and stops.");
        Log.Info("Send the whole output back, from the first line to the last line.");
        Console.WriteLine();
        Log.Info("Use:");
        Log.Info("  Mm2Lobby.LobbyProbe.exe                 the probe hosts a session");
        Log.Info("  Mm2Lobby.LobbyProbe.exe 10.147.17.42    the probe joins that address");
        Log.Info("  Mm2Lobby.LobbyProbe.exe --app \"Name\"     the probe uses that registry key");
    }

    private static string? ReadArguments(string[] args, out string? applicationFilter)
    {
        applicationFilter = null;
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

        Log.Item("An administrator runs the probe", IsElevated());
    }

    [SupportedOSPlatform("windows")]
    private static string IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator).ToString();
        }
        catch (Exception ex)
        {
            return $"unknown, because the check threw {ex.GetType().Name}";
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
    /// </summary>
    private static AddressBuffer? BuildAddress(IntPtr lobby, string? joinAddress)
    {
        var holders = new List<IntPtr>();

        try
        {
            var elements = new List<DpCompoundAddressElement>();

            var providerGuid = DirectPlayIds.DpSpGuidTcpIp;
            var providerData = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
            holders.Add(providerData);
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
                var text = Marshal.StringToHGlobalAnsi(joinAddress);
                holders.Add(text);

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
            var block = Marshal.AllocHGlobal(elementSize * elements.Count);
            holders.Add(block);

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

            var buffer = Marshal.AllocHGlobal((int)needed);
            var second = LobbyInterop.CreateCompoundAddress(
                lobby, block, (uint)elements.Count, buffer, ref needed);

            Log.Result("CreateCompoundAddress, the fill pass", second);

            if (second < 0)
            {
                Log.Problem("DirectPlay did not fill the address. The probe frees the buffer.");
                Marshal.FreeHGlobal(buffer);
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
        finally
        {
            foreach (var holder in holders) Marshal.FreeHGlobal(holder);
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
    private static void Attempt(
        IntPtr lobby,
        Guid applicationGuid,
        AddressBuffer? address,
        string? joinAddress)
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
                lobby, flags, applicationGuid, DirectPlayIds.DpSpGuidTcpIp, address, joinAddress))
        {
            return;
        }

        if (joinAddress is null)
        {
            if (Call("attempt 2, guidSP alone and no address",
                lobby, flags, applicationGuid, DirectPlayIds.DpSpGuidTcpIp, null, null))
            {
                return;
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
                lobby, flags, applicationGuid, Guid.Empty, address, joinAddress))
        {
            return;
        }

        Log.Problem("Every attempt failed. Read the codes above, from attempt 1 down.");
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
        string? joinAddress)
    {
        Console.WriteLine();
        Log.Info($"--- {label} ---");

        var holders = new List<IntPtr>();

        try
        {
            var playerName = Environment.UserName;
            if (string.IsNullOrWhiteSpace(playerName)) playerName = PlayerNameFallback;

            var playerText = Marshal.StringToHGlobalAnsi(playerName);
            holders.Add(playerText);

            var sessionText = IntPtr.Zero;
            if (joinAddress is null)
            {
                sessionText = Marshal.StringToHGlobalAnsi(SessionName);
                holders.Add(sessionText);
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

            var sessionBlock = Marshal.AllocHGlobal(Marshal.SizeOf<DpSessionDesc2>());
            holders.Add(sessionBlock);
            Marshal.StructureToPtr(session, sessionBlock, false);

            var nameBlock = Marshal.AllocHGlobal(Marshal.SizeOf<DpName>());
            holders.Add(nameBlock);
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

            var connectionBlock = Marshal.AllocHGlobal(Marshal.SizeOf<DplConnection>());
            holders.Add(connectionBlock);
            Marshal.StructureToPtr(connection, connectionBlock, false);

            Log.Item("DPLCONNECTION.dwSize", connection.Size.ToString());
            Log.Item("DPLCONNECTION.dwFlags", $"0x{connection.Flags:X8}");
            Log.Item("DPLCONNECTION.guidSP", DirectPlayIds.Format(connection.ServiceProviderGuid));
            Log.Item("DPLCONNECTION.dwAddressSize", connection.AddressSize.ToString());
            Log.Item("DPSESSIONDESC2.dwSize", session.Size.ToString());
            Log.Item("DPSESSIONDESC2.dwMaxPlayers", session.MaxPlayers.ToString());
            Log.Item("DPNAME.dwSize", name.Size.ToString());
            Log.Item("The player name", playerName);
            Log.Guess("The player name is the Windows user name, as an ANSI string.");

            var hr = LobbyInterop.RunApplication(lobby, 0, out var applicationId, connectionBlock, IntPtr.Zero);
            Log.Result($"RunApplication, {label}", hr);
            Log.Guess("The first argument of RunApplication is 0. The header says that it must be 0.");

            if (hr >= 0)
            {
                Log.Good($"DirectPlay started the application. The application id is {applicationId}.");
                Log.Good("Look at the screen. MM2 should run, and it should skip the multiplayer menus.");
                Log.Info(
                    "A success here does not prove that MM2 read the settings. "
                    + "Report what MM2 showed on the screen.");
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
            foreach (var holder in holders) Marshal.FreeHGlobal(holder);
        }
    }

    private static void PrintClosing()
    {
        Log.Heading("The guesses that the probe made");
        Log.Info(
            "The author could not run one line of this program, and could not "
            + "read dplay.h or dplobby.h. Each line below is a choice, not a fact.");
        Console.WriteLine();

        var guesses = Log.AllGuesses;
        if (guesses.Count == 0)
        {
            Log.Info("The probe printed no guess, because it stopped early.");
        }

        for (var i = 0; i < guesses.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {guesses[i]}");
        }

        Log.Heading("The end of the probe");
        Log.Info("The probe reached the last line. It did not crash.");
        Log.Info("Send every line of this output back, with what the screen showed.");
    }
}
