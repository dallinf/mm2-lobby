using System.Runtime.InteropServices;

namespace Mm2Lobby.LobbyProbe;

/// <summary>
/// DPSESSIONDESC2 from dplay.h.
/// <para>
/// The author rebuilt this layout from memory. A run on Windows 11 then
/// validated it, because RunApplication returned S_OK and started a process.
/// </para>
/// <para>
/// dplay.h declares the two name fields as a union of a wide pointer and an
/// ANSI pointer, so one pointer field holds either kind of string. It declares
/// the six fields after the names as DWORD_PTR, which is 8 bytes in a 64 bit
/// process and 4 bytes in a 32 bit process. This struct uses nuint for those
/// six fields for that reason.
/// </para>
/// <para>
/// WARNING: do not write a number into the Size field by hand. DirectPlay
/// rejects the struct when dwSize does not match. The probe sets the field
/// from Marshal.SizeOf, so the number follows the build.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DpSessionDesc2
{
    /// <summary>dwSize. The caller sets it to the size of this struct.</summary>
    public uint Size;

    /// <summary>dwFlags. The probe sets 0, to make no further guess.</summary>
    public uint Flags;

    /// <summary>guidInstance. The GUID of one running session.</summary>
    public Guid InstanceGuid;

    /// <summary>guidApplication. The GUID of the game, from the registry.</summary>
    public Guid ApplicationGuid;

    /// <summary>dwMaxPlayers.</summary>
    public uint MaxPlayers;

    /// <summary>dwCurrentPlayers.</summary>
    public uint CurrentPlayers;

    /// <summary>lpszSessionNameA. An ANSI string, or null.</summary>
    public IntPtr SessionNameAnsi;

    /// <summary>lpszPasswordA. An ANSI string, or null.</summary>
    public IntPtr PasswordAnsi;

    /// <summary>dwReserved1, a DWORD_PTR.</summary>
    public nuint Reserved1;

    /// <summary>dwReserved2, a DWORD_PTR.</summary>
    public nuint Reserved2;

    /// <summary>dwUser1, a DWORD_PTR.</summary>
    public nuint User1;

    /// <summary>dwUser2, a DWORD_PTR.</summary>
    public nuint User2;

    /// <summary>dwUser3, a DWORD_PTR.</summary>
    public nuint User3;

    /// <summary>dwUser4, a DWORD_PTR.</summary>
    public nuint User4;
}

/// <summary>
/// DPNAME from dplay.h. The two name fields are unions of a wide pointer and
/// an ANSI pointer, the same as in DPSESSIONDESC2.
/// <para>
/// The author rebuilt this layout from memory. A run on Windows 11 then
/// validated it, because RunApplication returned S_OK and started a process.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DpName
{
    /// <summary>dwSize.</summary>
    public uint Size;

    /// <summary>dwFlags. The header says that the caller sets 0.</summary>
    public uint Flags;

    /// <summary>lpszShortNameA. The name of the player, as an ANSI string.</summary>
    public IntPtr ShortNameAnsi;

    /// <summary>lpszLongNameA. An ANSI string, or null.</summary>
    public IntPtr LongNameAnsi;
}

/// <summary>
/// DPLCONNECTION from dplobby.h. The lobby fills this struct and gives it to
/// RunApplication.
/// <para>
/// The author rebuilt this layout from memory. A run on Windows 11 then
/// validated it, because RunApplication returned S_OK and started a process.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DplConnection
{
    /// <summary>dwSize.</summary>
    public uint Size;

    /// <summary>dwFlags. Either DPLCONNECTION_CREATESESSION or _JOINSESSION.</summary>
    public uint Flags;

    /// <summary>lpSessionDesc. A pointer to one DPSESSIONDESC2.</summary>
    public IntPtr SessionDesc;

    /// <summary>lpPlayerName. A pointer to one DPNAME.</summary>
    public IntPtr PlayerName;

    /// <summary>guidSP. The GUID of the service provider.</summary>
    public Guid ServiceProviderGuid;

    /// <summary>lpAddress. The compound address, or null.</summary>
    public IntPtr Address;

    /// <summary>dwAddressSize. The byte count of lpAddress.</summary>
    public uint AddressSize;
}

/// <summary>
/// DPCOMPOUNDADDRESSELEMENT from dplobby.h. Each element is one chunk of a
/// compound address.
/// <para>
/// The author rebuilt this layout from memory. A run on Windows 11 then
/// validated it, because RunApplication returned S_OK and started a process.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DpCompoundAddressElement
{
    /// <summary>guidDataType. One of the DPAID_ values.</summary>
    public Guid DataType;

    /// <summary>dwDataSize. The byte count of lpData.</summary>
    public uint DataSize;

    /// <summary>lpData. The value of this chunk.</summary>
    public IntPtr Data;
}

/// <summary>
/// This class prints the size and the field offsets of each struct above. The
/// owner needs these numbers, because a wrong size makes DirectPlay reject the
/// struct and the probe cannot test the layout on this computer.
/// </summary>
internal static class StructReport
{
    public static void PrintAll()
    {
        Print<DpSessionDesc2>("DPSESSIONDESC2");
        Print<DpName>("DPNAME");
        Print<DplConnection>("DPLCONNECTION");
        Print<DpCompoundAddressElement>("DPCOMPOUNDADDRESSELEMENT");
    }

    private static void Print<T>(string cName) where T : struct
    {
        Log.Info($"{cName}, as this build lays it out:");
        Log.Item("size in bytes", Marshal.SizeOf<T>().ToString());

        foreach (var field in typeof(T).GetFields())
        {
            var offset = Marshal.OffsetOf<T>(field.Name);
            Log.Item($"offset of {field.Name}", offset.ToInt64().ToString());
        }
    }
}
