using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Core.Tests.Launch;

public class GameLauncherTests
{
    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public List<GameLaunchPlan> Started { get; } = new();

        public void Start(GameLaunchPlan plan) => Started.Add(plan);
    }

    [Fact]
    public void BuildPlan_sets_the_working_directory_to_the_game_folder()
    {
        var path = Path.Combine(Path.GetTempPath(), "mm2", "midtown2.exe");

        var plan = GameLauncher.BuildPlan(path);

        Assert.Equal(Path.GetFullPath(path), plan.ExecutablePath);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), plan.WorkingDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildPlan_rejects_an_empty_path(string path)
    {
        Assert.Throws<ArgumentException>(() => GameLauncher.BuildPlan(path));
    }

    [Fact]
    public void Launch_hands_the_plan_to_the_process_launcher()
    {
        var launcher = new FakeProcessLauncher();
        var path = Path.Combine(Path.GetTempPath(), "mm2", "midtown2.exe");

        new GameLauncher(launcher).Launch(path);

        Assert.Equal(Path.GetFullPath(path), Assert.Single(launcher.Started).ExecutablePath);
    }

    [Fact]
    public void Launch_starts_nothing_when_the_path_is_empty()
    {
        var launcher = new FakeProcessLauncher();

        Assert.Throws<ArgumentException>(() => new GameLauncher(launcher).Launch(""));
        Assert.Empty(launcher.Started);
    }

    [Fact]
    public void JoinInstructions_name_the_host_address_in_the_last_step()
    {
        var steps = JoinInstructions.For("10.147.17.42");

        Assert.Equal(3, steps.Count);
        Assert.Contains("Multiplayer", steps[0]);
        Assert.Contains("TCP/IP", steps[1]);
        Assert.Contains("10.147.17.42", steps[2]);
    }

    [Fact]
    public void JoinInstructions_ForHost_name_the_host_choice_in_the_second_step()
    {
        var steps = JoinInstructions.ForHost();

        Assert.Equal(3, steps.Count);
        Assert.Contains("Multiplayer", steps[0]);
        Assert.Contains("TCP/IP", steps[1]);
        Assert.Contains("Host", steps[1]);
    }

    [Fact]
    public void BuildPlan_rejects_a_path_with_no_folder()
    {
        Assert.Throws<ArgumentException>(
            () => GameLauncher.BuildPlan(Path.GetPathRoot(Path.GetTempPath())!));
    }

    [Fact]
    public void BuildPlan_makes_a_relative_path_absolute()
    {
        var plan = GameLauncher.BuildPlan("midtown2.exe");

        Assert.True(Path.IsPathRooted(plan.ExecutablePath));
    }
}
