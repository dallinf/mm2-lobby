using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

/// <summary>
/// These tests cover the one command of the app that changes a permission on a
/// secret. The command must give the read permission to one account, and it
/// must give no other permission to anybody.
/// </summary>
public class ZeroTierTokenGrantTests
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    private const string SharedData = @"C:\ProgramData";

    private static readonly string Token =
        Path.Combine(SharedData, "ZeroTier", "One", "authtoken.secret");

    private readonly FakeElevatedProcessRunner _elevated = new();

    private string? _accountId = Sid;

    private ZeroTierTokenGrant Grant()
        => new(_elevated, () => _accountId, () => SharedData);

    [Fact]
    public void The_token_file_sits_under_the_shared_data_folder_of_the_computer()
    {
        Assert.Equal(Token, Grant().TokenPath);
    }

    [Fact]
    public void The_arguments_give_the_read_permission_to_one_account()
    {
        Assert.Equal(
            $"\"{Token}\" /grant *{Sid}:(R)",
            ZeroTierTokenGrant.ArgumentsFor(Token, Sid));
    }

    [Fact]
    public void The_arguments_give_no_permission_but_read()
    {
        var arguments = ZeroTierTokenGrant.ArgumentsFor(Token, Sid);

        Assert.Contains("(R)", arguments);
        Assert.DoesNotContain("(F)", arguments);
        Assert.DoesNotContain("(M)", arguments);
        Assert.DoesNotContain("(W)", arguments);
    }

    [Fact]
    public void The_arguments_name_no_group_of_accounts()
    {
        var arguments = ZeroTierTokenGrant.ArgumentsFor(Token, Sid);

        Assert.DoesNotContain("Everyone", arguments);
        Assert.DoesNotContain("Users", arguments);
        Assert.DoesNotContain("Authenticated", arguments);
    }

    [Fact]
    public void The_arguments_add_the_permission_and_replace_no_permission()
    {
        // The /grant switch of icacls adds one entry. The /grant:r switch
        // throws the other entries away, and it would take the permission of
        // the administrator away from the token file.
        var arguments = ZeroTierTokenGrant.ArgumentsFor(Token, Sid);

        Assert.Contains("/grant *", arguments);
        Assert.DoesNotContain("/grant:r", arguments);
    }

    [Fact]
    public void The_arguments_name_the_account_by_its_ID_and_not_by_its_name()
    {
        // The * prefix tells icacls that the name is a SID. A SID has one form
        // on every computer. Windows translates an account name, so a name
        // would fail on a computer with another language.
        Assert.Contains($"*{Sid}:", ZeroTierTokenGrant.ArgumentsFor(Token, Sid));
    }

    [Fact]
    public async Task RunAsync_runs_icacls_with_the_elevated_runner()
    {
        _elevated.Returns(ZeroTierTokenGrant.Program, ZeroTierTokenGrant.ArgumentsFor(Token, Sid), 0);

        var run = await Grant().RunAsync(default);

        Assert.NotNull(run);
        Assert.Equal(0, run.Result.ExitCode);
        Assert.Equal(
            new[] { $"icacls.exe \"{Token}\" /grant *{Sid}:(R)" },
            _elevated.Calls);
    }

    [Fact]
    public async Task RunAsync_names_the_whole_command_for_a_player_who_runs_it_by_hand()
    {
        var run = await Grant().RunAsync(default);

        Assert.NotNull(run);
        Assert.Equal($"icacls \"{Token}\" /grant *{Sid}:(R)", run.Command);
    }

    [Fact]
    public async Task RunAsync_reports_the_exit_code_of_a_command_that_failed()
    {
        _elevated.Returns(
            ZeroTierTokenGrant.Program,
            ZeroTierTokenGrant.ArgumentsFor(Token, Sid),
            SystemProcessRunner.NotRunExitCode);

        var run = await Grant().RunAsync(default);

        Assert.NotNull(run);
        Assert.Equal(SystemProcessRunner.NotRunExitCode, run.Result.ExitCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunAsync_runs_nothing_when_the_app_cannot_read_the_account(string? accountId)
    {
        // Without the ID of the account the app has no safe principal to name.
        // It must not fall back to a group, so it runs nothing at all.
        _accountId = accountId;

        Assert.Null(await Grant().RunAsync(default));
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public void The_class_names_the_program_and_the_file_of_the_token()
    {
        Assert.Equal("icacls.exe", ZeroTierTokenGrant.Program);
        Assert.Equal("authtoken.secret", ZeroTierTokenGrant.TokenFileName);
    }

    [Fact]
    public async Task The_class_asks_Windows_for_the_account_one_time()
    {
        // The ID of the account cannot change while the app runs. So the app
        // asks Windows one time, in the constructor, and every run after that
        // uses the one answer.
        var reads = 0;
        var grant = new ZeroTierTokenGrant(
            _elevated,
            () =>
            {
                reads++;
                return Sid;
            },
            () => SharedData);

        Assert.Equal(1, reads);

        await grant.RunAsync(default);
        await grant.RunAsync(default);

        Assert.Equal(1, reads);
    }
}
