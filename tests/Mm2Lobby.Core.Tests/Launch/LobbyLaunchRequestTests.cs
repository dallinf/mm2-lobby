using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Core.Tests.Launch;

public class LobbyLaunchRequestTests
{
    [Fact]
    public void ToHost_takes_the_host_role_and_no_address()
    {
        var request = LobbyLaunchRequest.ToHost("Dallin", "Friday race");

        Assert.Equal(LobbyRole.Host, request.Role);
        Assert.Equal("Dallin", request.PlayerName);
        Assert.Equal("Friday race", request.SessionName);
        Assert.Null(request.HostAddress);
    }

    [Fact]
    public void ToHost_falls_back_to_a_default_name_and_a_default_session_name()
    {
        var request = LobbyLaunchRequest.ToHost("   ", null);

        Assert.Equal(LobbyLaunchRequest.DefaultPlayerName, request.PlayerName);
        Assert.Equal(LobbyLaunchRequest.DefaultSessionName, request.SessionName);
    }

    [Fact]
    public void ToJoin_takes_the_join_role_and_the_address()
    {
        var request = LobbyLaunchRequest.ToJoin("10.147.17.42", "Dallin");

        Assert.Equal(LobbyRole.Join, request.Role);
        Assert.Equal("10.147.17.42", request.HostAddress);
        Assert.Equal("Dallin", request.PlayerName);
    }

    [Fact]
    public void ToJoin_trims_the_address()
    {
        Assert.Equal("10.147.17.42", LobbyLaunchRequest.ToJoin("  10.147.17.42  ").HostAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToJoin_refuses_a_blank_address(string address)
    {
        // The app offers Join only after it knows the address of the host, so a
        // blank address is a programmer error and not a player error.
        var exception = Assert.Throws<ArgumentException>(() => LobbyLaunchRequest.ToJoin(address));

        Assert.Equal("hostAddress", exception.ParamName);
    }
}
