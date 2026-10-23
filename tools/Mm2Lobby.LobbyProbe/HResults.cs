namespace Mm2Lobby.LobbyProbe;

/// <summary>
/// This class turns an HRESULT into a line of text.
/// <para>
/// WARNING: the author of this file could not open a copy of dplay.h or
/// dplobby.h. Every DirectPlay name below comes from memory, and a name may be
/// wrong. The number beside the name does not come from memory. The probe
/// computes the facility and the code from the HRESULT itself. Trust the
/// computed number. Treat the name as a hint only.
/// </para>
/// <para>
/// DirectPlay builds an error code as MAKE_HRESULT(1, 0x877, code). The
/// severity bit is 1, the facility is 0x877, and the low 16 bits hold a
/// decimal code such as 30 or 370.
/// </para>
/// </summary>
internal static class HResults
{
    /// <summary>The DirectPlay facility, _FACDP in dplay.h.</summary>
    public const int DirectPlayFacility = 0x877;

    /// <summary>The first 4 bytes of every DirectPlay error code.</summary>
    public const int DirectPlayBase = unchecked((int)0x88770000);

    public const int SOk = 0;
    public const int SFalse = 1;

    /// <summary>
    /// DPERR_BUFFERTOOSMALL. CreateCompoundAddress returns this code when the
    /// caller asks for the size of the address. The probe needs this one code
    /// to work, so it also accepts any other failure that reports a size.
    /// </summary>
    public const int DpErrBufferTooSmall = unchecked((int)0x8877001E);

    // The standard COM codes. These do not come from DirectPlay, and the
    // author is confident of these four numbers.
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

    // The DirectPlay codes, by the decimal code in the low 16 bits.
    // WARNING: every number and name in this table comes from memory. The
    // probe prints the decimal code beside the name, so the owner can look the
    // name up and correct it.
    private static readonly Dictionary<int, string> DirectPlayNames = new()
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
        [300] = "DPERR_CANTCREATEPROCESS. DirectPlay could not start the game. "
            + "The game may not be registered, or the path in the registry may be wrong.",
        [310] = "DPERR_APPNOTSTARTED",
        [320] = "DPERR_INVALIDINTERFACE",
        [330] = "DPERR_NOSERVICEPROVIDER",
        [340] = "DPERR_UNKNOWNAPPLICATION. DirectPlay does not know this application GUID.",
        [350] = "DPERR_NOTLOBBIED",
        [360] = "DPERR_SERVICEPROVIDERLOADED",
        [370] = "DPERR_ALREADYREGISTERED",
        [380] = "DPERR_NOTREGISTERED",
        [900] = "DPERR_AUTHENTICATIONFAILED",
        [910] = "DPERR_CANTLOADSSPI",
        [920] = "DPERR_ENCRYPTIONFAILED",
        [930] = "DPERR_SIGNFAILED",
        [940] = "DPERR_CANTLOADSECURITYPACKAGE",
        [950] = "DPERR_ENCRYPTIONNOTSUPPORTED",
        [960] = "DPERR_CANTLOADCAPI",
        [970] = "DPERR_NOTLOGGEDIN",
        [980] = "DPERR_LOGONDENIED",
    };

    /// <summary>
    /// This method writes one line that describes an HRESULT. It always shows
    /// the hex value. It adds the computed facility and code for a DirectPlay
    /// error, and then a name from memory.
    /// </summary>
    public static string Describe(int hr)
    {
        var hex = $"0x{hr:X8}";

        if (hr == SOk) return $"{hex} (S_OK, success)";
        if (hr == SFalse) return $"{hex} (S_FALSE, success with a remark)";
        if (hr >= 0) return $"{hex} (a success code that the probe does not name)";

        var facility = (hr >> 16) & 0x1FFF;
        var code = hr & 0xFFFF;

        if (facility == DirectPlayFacility)
        {
            var computed = $"{hex} (DirectPlay facility 0x877, code {code} decimal";
            return DirectPlayNames.TryGetValue(code, out var dpName)
                ? $"{computed}, name from memory and NOT verified: {dpName})"
                : $"{computed}, the probe has no name for code {code})";
        }

        if (StandardNames.TryGetValue(hr, out var name)) return $"{hex} ({name})";

        return $"{hex} (facility 0x{facility:X3}, code {code} decimal, "
            + "the probe has no name for this code)";
    }
}
