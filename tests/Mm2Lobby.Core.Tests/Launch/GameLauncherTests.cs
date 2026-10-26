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
        var exception = Assert.Throws<ArgumentException>(() => GameLauncher.BuildPlan(path));

        Assert.Equal("gamePath", exception.ParamName);
    }

    [Fact]
    public void Launch_hands_the_plan_to_the_process_launcher()
    {
        var launcher = new FakeProcessLauncher();
        var path = Path.Combine(Path.GetTempPath(), "mm2", "midtown2.exe");

        new GameLauncher(launcher).Launch(path);

        Assert.Equal(GameLauncher.BuildPlan(path), Assert.Single(launcher.Started));
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
        Assert.Contains("Join", steps[1]);
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
    public void The_steps_of_the_race_tell_the_host_that_he_types_no_address()
    {
        // The lobby gave the address to the game. A step that named a menu of
        // MM2 would ask the host for an action that the game does not offer.
        var steps = JoinInstructions.ForTheHostInTheRace();

        Assert.Equal(2, steps.Count);
        Assert.Contains("no address", steps[0]);
        Assert.Contains("Pick a city", steps[1]);
        Assert.DoesNotContain(steps, step => step.Contains("Multiplayer"));
    }

    [Fact]
    public void The_steps_of_the_race_tell_a_player_who_joined_to_wait()
    {
        var steps = JoinInstructions.ForThePlayerInTheRace();

        Assert.Equal(2, steps.Count);
        Assert.Contains("no address", steps[0]);
        Assert.Contains("Wait for the host", steps[1]);
        Assert.DoesNotContain(steps, step => step.Contains("Type"));
    }

    [Fact]
    public void Every_list_of_steps_differs_from_every_other_list()
    {
        // The window chooses one list for each path. Two lists that read the
        // same would hide a window that chose the wrong one.
        var lists = new[]
        {
            JoinInstructions.For("10.147.17.42"),
            JoinInstructions.ForHost(),
            JoinInstructions.ForTheHostInTheRace(),
            JoinInstructions.ForThePlayerInTheRace(),
        };

        var texts = lists.Select(list => string.Join("|", list)).ToList();

        Assert.Equal(lists.Length, texts.Distinct().Count());
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
        var plan = GameLauncher.BuildPlan(Path.Combine("mm2", "midtown2.exe"));

        Assert.True(Path.IsPathRooted(plan.ExecutablePath));
    }
}
