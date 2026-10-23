using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// This class turns an HRESULT into a line of text for the report.
/// <para>
/// WARNING: every DirectPlay name below comes from memory, because the owner
/// could not open dplay.h. The numbers do not come from memory. This class
/// computes the facility and the code from the HRESULT itself, and it writes
/// the computed numbers before the name. Trust the numbers. Treat each name as
/// a hint, and the text says so.
/// </para>
/// <para>
/// DirectPlay builds an error as MAKE_HRESULT(1, 0x877, code). The severity bit
/// is 1, the facility is 0x877, and the low 16 bits hold a decimal code such as
/// 30 or 300.
/// </para>
/// </summary>
internal static class DirectPlayHResults
{
    /// <summary>The DirectPlay facility, _FACDP in dplay.h.</summary>
    public const int Facility = 0x877;

    /// <summary>S_OK.</summary>
    public const int Ok = 0;

    /// <summary>S_FALSE.</summary>
    public const int False = 1;

    /// <summary>E_POINTER. The launcher reports this code for a missing slot.</summary>
    public const int Pointer = unchecked((int)0x80004003);

    /// <summary>
    /// The DirectPlay code of DPERR_BUFFERTOOSMALL. CreateCompoundAddress
    /// returns it from the size pass, with the byte count that it needs.
    /// </summary>
    public const int BufferTooSmallCode = 30;

    /// <summary>
    /// The DirectPlay code that a run on Windows 11 returned while the game
    /// carried the WINXPSP2 compatibility layer. Windows then demanded
    /// elevation, and CreateProcess cannot answer that demand.
    /// </summary>
    public const int CannotCreateProcessCode = 300;

    // The standard COM codes. These do not come from DirectPlay, and the owner
    // is confident of these numbers.
    private static readonly Dictionary<int, string> StandardNames = new()
    {
        [unchecked((int)0x80004001)] = "E_NOTIMPL",
        [unchecked((int)0x80004002)] = "E_NOINTERFACE",
        [unchecked((int)0x80004003)] = "E_POINTER",
        [unchecked((int)0x80004005)] = "E_FAIL",
        [unchecked((int)0x8000FFFF)] = "E_UNEXPECTED",
        [unchecked((int)0x8007000E)] = "E_OUTOFMEMORY",
        [unchecked((int)0x80070057)] = "E_INVALIDARG",
        [unchecked((int)0x8007007E)] = "ERROR_MOD_NOT_FOUND as an HRESULT. Windows cannot load the DLL.",
        [unchecked((int)0x8007007F)] = "ERROR_PROC_NOT_FOUND as an HRESULT. The DLL has no such export.",
        [unchecked((int)0x80040154)] = "REGDB_E_CLASSNOTREG. The CLSID is not registered.",
        [unchecked((int)0x800401F0)] = "CO_E_NOTINITIALIZED. The thread did not call CoInitialize.",
    };

    // WARNING: every name in this table comes from memory. The key is the
    // decimal code in the low 16 bits, and Describe writes that key beside the
    // name, so a reader can correct the name.
    private static readonly Dictionary<int, string> Names = new()
    {
        [5] = "DPERR_ALREADYINITIALIZED",
        [10] = "DPERR_ACCESSDENIED",
        [20] = "DPERR_ACTIVEPLAYERS",
        [30] = "DPERR_BUFFERTOOSMALL",
        [40] = "DPERR_CANTADDPLAYER",
        [50] = "DPERR_CANTCREATEGROUP",
        [60] = "DPERR_CANTCREATEPLAYER",
        [70] = "DPERR_CANTCREATESESSION",
        [80] = "DPERR_CAPSNOTAVAILABLEYET",
        [90] = "DPERR_EXCEPTION",
        [100] = "DPERR_HOSTTERMINATEDSESSION",
        [120] = "DPERR_INVALIDFLAGS",
        [130] = "DPERR_INVALIDOBJECT",
        [150] = "DPERR_INVALIDPLAYER",
        [155] = "DPERR_INVALIDGROUP",
        [160] = "DPERR_NOCAPS",
        [170] = "DPERR_NOCONNECTION",
        [200] = "DPERR_NONEWPLAYERS",
        [220] = "DPERR_NOPLAYERS",
        [230] = "DPERR_NOSESSIONS",
        [255] = "DPERR_SENDTOOBIG",
        [260] = "DPERR_TIMEOUT",
        [270] = "DPERR_UNAVAILABLE",
        [280] = "DPERR_BUSY",
        [290] = "DPERR_USERCANCEL",
        [300] = "DPERR_CANTCREATEPROCESS",
        [310] = "DPERR_APPNOTSTARTED",
        [320] = "DPERR_INVALIDINTERFACE",
        [330] = "DPERR_NOSERVICEPROVIDER",
        [340] = "DPERR_UNKNOWNAPPLICATION",
        [350] = "DPERR_NOTLOBBIED",
        [360] = "DPERR_SERVICEPROVIDERLOADED",
        [370] = "DPERR_ALREADYREGISTERED",
        [380] = "DPERR_NOTREGISTERED",
    };

    /// <summary>
    /// The names that come from memory, by the decimal code. A test reads this
    /// table and proves that no name reaches another facility.
    /// </summary>
    public static IReadOnlyDictionary<int, string> RecalledNames => Names;

    /// <summary>This method builds the HRESULT of one DirectPlay code.</summary>
    public static int MakeError(int code) =>
        unchecked((int)0x80000000) | (Facility << 16) | (code & 0xFFFF);

    /// <summary>This method reads the facility bits of an HRESULT.</summary>
    public static int FacilityOf(int hr) => (hr >> 16) & 0x1FFF;

    /// <summary>This method reads the code bits of an HRESULT.</summary>
    public static int CodeOf(int hr) => hr & 0xFFFF;

    /// <summary>True when the HRESULT reports a failure.</summary>
    public static bool Failed(int hr) => hr < 0;

    /// <summary>
    /// This method writes one line that describes an HRESULT. It always shows
    /// the hex value. For a DirectPlay error it adds the computed facility and
    /// the computed code, and only then a name from memory.
    /// </summary>
    public static string Describe(int hr)
    {
        var hex = $"0x{hr:X8}";

        if (hr == Ok) return $"{hex} (S_OK, success)";
        if (hr == False) return $"{hex} (S_FALSE, success with a remark)";
        if (hr > 0) return $"{hex} (a success code that the app does not name)";

        var facility = FacilityOf(hr);
        var code = CodeOf(hr);

        if (facility == Facility)
        {
            var computed = $"{hex} (DirectPlay facility 0x877, code {code} decimal";

            return Names.TryGetValue(code, out var name)
                ? $"{computed}, a name from memory that no run verified: {name})"
                : $"{computed}, the app has no name for this code)";
        }

        if (StandardNames.TryGetValue(hr, out var standard)) return $"{hex} ({standard})";

        return $"{hex} (facility 0x{facility:X3}, code {code} decimal, "
            + "the app has no name for this code)";
    }

    /// <summary>
    /// This method turns an HRESULT into the fault that the app reports to the
    /// player. Only the DirectPlay facility carries the meaning of a code.
    /// </summary>
    public static LobbyLaunchFault FaultFor(int hr)
    {
        if (FacilityOf(hr) != Facility) return LobbyLaunchFault.DirectPlayRefused;

        return CodeOf(hr) == CannotCreateProcessCode
            ? LobbyLaunchFault.GameWillNotStart
            : LobbyLaunchFault.DirectPlayRefused;
    }
}
