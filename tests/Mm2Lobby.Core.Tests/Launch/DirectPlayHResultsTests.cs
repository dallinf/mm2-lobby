using Mm2Lobby.Core.Launch;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

/// <summary>
/// The owner could not open dplay.h, so every DirectPlay name in the decoder
/// comes from memory. The numbers do not. These tests prove that the text
/// always carries the computed facility and the computed code, and that a name
/// from memory never reaches a code that it does not belong to.
/// </summary>
public class DirectPlayHResultsTests
{
    [Fact]
    public void The_decoder_holds_names_to_test()
    {
        Assert.NotEmpty(DirectPlayHResults.RecalledNames);
    }

    [Fact]
    public void Describe_names_a_success()
    {
        Assert.Contains("S_OK", DirectPlayHResults.Describe(0));
        Assert.Contains("S_FALSE", DirectPlayHResults.Describe(1));
    }

    [Fact]
    public void Describe_computes_the_facility_and_the_code_of_a_DirectPlay_error()
    {
        var text = DirectPlayHResults.Describe(DirectPlayHResults.MakeError(30));

        Assert.Contains("0x877", text);
        Assert.Contains("30", text);
    }

    [Fact]
    public void Every_recalled_name_stands_after_the_computed_code_and_carries_a_warning()
    {
        foreach (var code in DirectPlayHResults.RecalledNames.Keys)
        {
            var name = DirectPlayHResults.RecalledNames[code];
            var text = DirectPlayHResults.Describe(DirectPlayHResults.MakeError(code));

            var codeIndex = text.IndexOf($"code {code} decimal", StringComparison.Ordinal);
            var nameIndex = text.IndexOf(name, StringComparison.Ordinal);

            Assert.True(codeIndex >= 0, $"Code {code} lost its computed code: {text}");
            Assert.True(nameIndex > codeIndex, $"Code {code} put the name before the code: {text}");
            Assert.Contains("no run verified", text);
        }
    }

    [Fact]
    public void A_name_from_memory_never_reaches_another_facility()
    {
        // 0x8007012C holds the same low 16 bits as the DirectPlay code 300. The
        // facility differs, so the DirectPlay name must not appear.
        var code = 300;
        var name = DirectPlayHResults.RecalledNames[code];
        var other = unchecked((int)0x8007012C);

        Assert.Equal(code, DirectPlayHResults.CodeOf(other));
        Assert.DoesNotContain(name, DirectPlayHResults.Describe(other));
    }

    [Fact]
    public void An_unknown_DirectPlay_code_reports_the_code_and_claims_no_name()
    {
        var unknown = 4242;
        Assert.DoesNotContain(unknown, DirectPlayHResults.RecalledNames.Keys);

        var text = DirectPlayHResults.Describe(DirectPlayHResults.MakeError(unknown));

        Assert.Contains($"code {unknown} decimal", text);
        Assert.Contains("no name", text);
    }

    [Fact]
    public void CodeOf_and_FacilityOf_read_the_bits_of_the_HRESULT()
    {
        var hr = DirectPlayHResults.MakeError(370);

        Assert.Equal(370, DirectPlayHResults.CodeOf(hr));
        Assert.Equal(DirectPlayHResults.Facility, DirectPlayHResults.FacilityOf(hr));
    }

    [Fact]
    public void MakeError_builds_the_number_that_DirectPlay_returns()
    {
        // DPERR_BUFFERTOOSMALL reached the owner as 0x8877001E on Windows 11.
        Assert.Equal(unchecked((int)0x8877001E), DirectPlayHResults.MakeError(30));
    }

    [Fact]
    public void Describe_names_a_standard_COM_code()
    {
        Assert.Contains("E_INVALIDARG", DirectPlayHResults.Describe(unchecked((int)0x80070057)));
    }

    [Fact]
    public void Describe_always_shows_the_hex_value()
    {
        Assert.Contains("0x80004005", DirectPlayHResults.Describe(unchecked((int)0x80004005)));
        Assert.Contains("0x8877001E", DirectPlayHResults.Describe(unchecked((int)0x8877001E)));
    }

    [Fact]
    public void The_code_300_reports_that_Windows_did_not_start_the_game()
    {
        // A run on Windows 11 returned the DirectPlay code 300 while the game
        // carried the WINXPSP2 compatibility layer. Windows then demanded
        // elevation, and CreateProcess cannot answer that demand.
        Assert.Equal(
            LobbyLaunchFault.GameWillNotStart,
            DirectPlayHResults.FaultFor(DirectPlayHResults.MakeError(300)));
    }

    [Fact]
    public void Another_DirectPlay_code_reports_that_DirectPlay_refused_the_call()
    {
        Assert.Equal(
            LobbyLaunchFault.DirectPlayRefused,
            DirectPlayHResults.FaultFor(DirectPlayHResults.MakeError(170)));
    }

    [Fact]
    public void The_code_300_of_another_facility_does_not_report_the_game()
    {
        // Only the DirectPlay facility carries the meaning of the code 300.
        Assert.Equal(
            LobbyLaunchFault.DirectPlayRefused,
            DirectPlayHResults.FaultFor(unchecked((int)0x8007012C)));
    }
}
