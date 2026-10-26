using Mm2Lobby.Core.Launch;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows.Checks;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

/// <summary>
/// These tests drive the gate of the launcher and the steps that run before
/// DirectPlay. The internal constructor takes every one of these steps as a
/// function, so the tests run on macOS and touch no Windows API.
/// <para>
/// The gate feeds CanLaunch, and the window reads CanLaunch to pick the label
/// of the Host button. A gate that answers the wrong value promises the player
/// a race on a path that cannot open.
/// </para>
/// </summary>
public class DirectPlayLobbyLauncherTests
{
    private readonly FakeNativeAllocator _allocator = new();
    private readonly List<string> _filesAsked = [];

    private int _registrationReads;
    private bool _fileIsThere = true;
    private bool _isWindows = true;
    private bool _is64BitProcess;
    private LobbyRegistration _registration = Registered();
    private Exception? _registrationError;

    [Fact]
    public void CanLaunch_is_false_on_a_computer_that_does_not_run_Windows()
    {
        _isWindows = false;

        Assert.False(Build().CanLaunch);
    }

    [Fact]
    public void CanLaunch_is_false_in_a_64_bit_process()
    {
        // Windows 11 ships a 32 bit dplayx.dll only. A 64 bit process cannot
        // load it, so the gate must shut here.
        _is64BitProcess = true;

        Assert.False(Build().CanLaunch);
    }

    [Fact]
    public void CanLaunch_is_false_when_Windows_holds_no_DirectPlay_file()
    {
        _fileIsThere = false;

        Assert.False(Build().CanLaunch);
    }

    [Fact]
    public void CanLaunch_is_true_in_a_32_bit_Windows_process_that_holds_the_file()
    {
        Assert.True(Build().CanLaunch);
    }

    [Fact]
    public void CanLaunch_asks_for_the_32_bit_DirectPlay_file()
    {
        _ = Build().CanLaunch;

        Assert.Equal([DirectPlayCheck.FeatureLibrary], _filesAsked);
    }

    [Fact]
    public void CanLaunch_reads_no_registry_key()
    {
        _ = Build().CanLaunch;

        Assert.Equal(0, _registrationReads);
    }

    [Fact]
    public void A_launch_on_another_operating_system_reports_NotWindows()
    {
        _isWindows = false;

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        AssertFailure(result, LobbyLaunchFault.NotWindows);
    }

    [Fact]
    public void A_launch_in_a_64_bit_process_reports_WrongProcessBitness()
    {
        _is64BitProcess = true;

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        AssertFailure(result, LobbyLaunchFault.WrongProcessBitness);
    }

    [Fact]
    public void A_launch_without_the_DirectPlay_file_reports_DirectPlayMissing()
    {
        _fileIsThere = false;

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        AssertFailure(result, LobbyLaunchFault.DirectPlayMissing);
    }

    [Fact]
    public void A_blocked_launch_reads_no_registry_key()
    {
        _isWindows = false;

        Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Equal(0, _registrationReads);
    }

    [Fact]
    public void A_blocked_launch_takes_no_memory()
    {
        _is64BitProcess = true;

        Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Empty(_allocator.Given);
    }

    [Fact]
    public void The_report_of_a_blocked_launch_names_the_role_and_the_blocker()
    {
        _is64BitProcess = true;

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Contains("Host", result.Detail);
        Assert.Contains(nameof(LobbyLaunchFault.WrongProcessBitness), result.Detail);
    }

    [Fact]
    public void A_launch_without_a_registry_key_reports_GameNotRegistered()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        AssertFailure(result, LobbyLaunchFault.GameNotRegistered);
        Assert.Equal(1, _registrationReads);
    }

    // The enum of the reader is internal, so a public test method cannot take
    // it. Each case names its value, and Fault below reads the name.
    [Theory]
    [InlineData("NoGuid")]
    [InlineData("WrongGuid")]
    [InlineData("NoPath")]
    [InlineData("NoFile")]
    public void A_launch_with_a_wrong_value_in_the_key_reports_GameRegistrationWrong(string name)
    {
        _registration = new LobbyRegistration(null, Fault(name));

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        AssertFailure(result, LobbyLaunchFault.GameRegistrationWrong);
    }

    [Theory]
    [InlineData("FileWithoutLeadingBackslash")]
    [InlineData("LauncherValuePresent")]
    public void A_launch_stops_on_a_fault_that_still_carries_an_entry(string name)
    {
        // The reader gives an entry with these two faults. The launcher must
        // read the fault and not the entry, because DirectPlay would refuse
        // the launch.
        _registration = new LobbyRegistration(Registered().Entry, Fault(name));

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        AssertFailure(result, LobbyLaunchFault.GameRegistrationWrong);
    }

    [Fact]
    public void A_launch_stops_when_the_reader_gives_no_entry_and_no_fault()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.None);

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        AssertFailure(result, LobbyLaunchFault.GameRegistrationWrong);
    }

    [Fact]
    public void The_report_of_a_stopped_launch_names_the_fault_of_the_registration()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.WrongGuid);

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Contains(nameof(LobbyRegistrationFault.WrongGuid), result.Detail);
    }

    [Fact]
    public void A_launch_reports_Unexpected_when_a_step_throws()
    {
        _registrationError = new InvalidOperationException("The registry refused the read.");

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        AssertFailure(result, LobbyLaunchFault.Unexpected);
        Assert.Contains(nameof(InvalidOperationException), result.Detail);
        Assert.Contains("The registry refused the read.", result.Detail);
    }

    [Fact]
    public void A_launch_refuses_a_request_that_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => Build().Launch(null!));
    }

    [Fact]
    public void FaultOf_separates_a_missing_key_from_a_wrong_value()
    {
        Assert.Equal(
            LobbyLaunchFault.GameNotRegistered,
            DirectPlayLobbyLauncher.FaultOf(LobbyRegistrationFault.NoKey));

        Assert.Equal(
            LobbyLaunchFault.GameRegistrationWrong,
            DirectPlayLobbyLauncher.FaultOf(LobbyRegistrationFault.WrongGuid));
    }

    /// <summary>This method reads one fault of the reader by its name.</summary>
    private static LobbyRegistrationFault Fault(string name) =>
        Enum.Parse<LobbyRegistrationFault>(name);

    /// <summary>A registration that passes every test of the reader.</summary>
    private static LobbyRegistration Registered() => new(
        new LobbyApplicationEntry(
            LobbyApplicationReader.OwnKeyName,
            LobbyApplicationReader.ApplicationGuid,
            @"C:\Games\Midtown Madness 2",
            @"\midtown2.exe",
            null,
            null),
        LobbyRegistrationFault.None);

    private static void AssertFailure(LobbyLaunchResult result, LobbyLaunchFault fault)
    {
        var advice = LobbyLaunchMessages.For(fault);

        Assert.False(result.Started);
        Assert.Equal(fault, result.Fault);
        Assert.Null(result.Session);
        Assert.Equal(advice.Message, result.Message);
        Assert.Equal(advice.PlayerAction, result.PlayerAction);
    }

    private DirectPlayLobbyLauncher Build() => new(
        _allocator,
        path =>
        {
            _filesAsked.Add(path);
            return _fileIsThere;
        },
        () =>
        {
            _registrationReads++;

            if (_registrationError is not null) throw _registrationError;

            return _registration;
        },
        () => false,
        _isWindows,
        _is64BitProcess);
}
