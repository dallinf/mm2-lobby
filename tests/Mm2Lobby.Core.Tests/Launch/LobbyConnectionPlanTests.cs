using Mm2Lobby.Core.Launch;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

/// <summary>
/// These tests lock the behaviour that fourteen runs on Windows 11 proved. The
/// host role and the join role both reached a game, and the owner does not know
/// which field combination carried the join, because the output file of the
/// friend was lost. So each attempt and its order stays as it ran.
/// </summary>
public class LobbyConnectionPlanTests
{
    [Fact]
    public void A_host_sends_the_flag_DPLCONNECTION_CREATESESSION()
    {
        Assert.Equal(2u, LobbyConnectionPlan.FlagsFor(LobbyRole.Host));
    }

    [Fact]
    public void A_player_who_joins_sends_the_flag_DPLCONNECTION_JOINSESSION()
    {
        Assert.Equal(1u, LobbyConnectionPlan.FlagsFor(LobbyRole.Join));
    }

    [Fact]
    public void A_host_tries_both_fields_then_the_provider_alone_then_the_address_alone()
    {
        var attempts = LobbyConnectionPlan.AttemptsFor(LobbyRole.Host, hasAddress: true);

        Assert.Equal(3, attempts.Count);

        Assert.True(attempts[0].SendServiceProvider);
        Assert.True(attempts[0].SendAddress);

        Assert.True(attempts[1].SendServiceProvider);
        Assert.False(attempts[1].SendAddress);

        Assert.False(attempts[2].SendServiceProvider);
        Assert.True(attempts[2].SendAddress);
    }

    [Fact]
    public void A_player_who_joins_tries_both_fields_then_the_address_alone()
    {
        // The second attempt of the host sends no address. A join needs the
        // address, so the probe skipped that attempt in the join role and the
        // launcher skips it too.
        var attempts = LobbyConnectionPlan.AttemptsFor(LobbyRole.Join, hasAddress: true);

        Assert.Equal(2, attempts.Count);

        Assert.True(attempts[0].SendServiceProvider);
        Assert.True(attempts[0].SendAddress);

        Assert.False(attempts[1].SendServiceProvider);
        Assert.True(attempts[1].SendAddress);
    }

    [Fact]
    public void Every_attempt_carries_the_flag_of_the_role()
    {
        foreach (var role in Enum.GetValues<LobbyRole>())
        {
            var flags = LobbyConnectionPlan.FlagsFor(role);

            foreach (var attempt in LobbyConnectionPlan.AttemptsFor(role, hasAddress: true))
            {
                Assert.Equal(flags, attempt.Flags);
            }
        }
    }

    [Fact]
    public void A_host_with_no_address_tries_the_provider_alone()
    {
        var attempts = LobbyConnectionPlan.AttemptsFor(LobbyRole.Host, hasAddress: false);

        var single = Assert.Single(attempts);
        Assert.True(single.SendServiceProvider);
        Assert.False(single.SendAddress);
    }

    [Fact]
    public void A_player_who_joins_with_no_address_tries_nothing()
    {
        // Without the address the game cannot find the host, and an attempt
        // that started the game would send the player back to the menus.
        Assert.Empty(LobbyConnectionPlan.AttemptsFor(LobbyRole.Join, hasAddress: false));
    }

    [Fact]
    public void Each_attempt_names_itself_for_the_report()
    {
        var labels = LobbyConnectionPlan.AttemptsFor(LobbyRole.Host, hasAddress: true)
            .Select(one => one.Label)
            .ToList();

        Assert.DoesNotContain(labels, string.IsNullOrWhiteSpace);
        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
    }
}
