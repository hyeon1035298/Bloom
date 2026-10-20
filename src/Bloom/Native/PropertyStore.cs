using System;
using System.Runtime.InteropServices;

namespace Bloom.Native;

/// Windows' property system (what Explorer's Details pane shows): a file's or a device's properties, read one key at a
/// time. The sound devices' names (SystemControl) and the songs' tags (Music\AudioTags) read through it.
[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid Fmtid;
    public int Pid;

    public PropertyKey(string fmtid, int pid)
    {
        Fmtid = new Guid(fmtid);
        Pid = pid;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Vt;
    public ushort Reserved1, Reserved2, Reserved3;
    public IntPtr Pointer;
    public IntPtr Pointer2;
}

internal static class PropertyStore
{
    private static readonly Guid Iid = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");

    /// A file's properties, read only, or null (no such file, or no handler would open it). Release it when done.
    public static IPropertyStore? OfFile(string path) =>
        SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0 /* GPS_DEFAULT */, Iid, out var store) == 0 ? store : null;

    /// The value as text — a list ("a; b") too — or "" when it is not set or not text.
    public static string Text(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var pv) != 0) return "";
        try
        {
            if (pv.Vt == 0 || PropVariantToStringAlloc(ref pv, out IntPtr p) != 0 || p == IntPtr.Zero) return "";
            string s = Marshal.PtrToStringUni(p) ?? "";
            Marshal.FreeCoTaskMem(p);
            return s.Trim();
        }
        finally
        {
            _ = PropVariantClear(ref pv);
        }
    }

    /// The value as a whole number, or null.
    public static ulong? Number(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var pv) != 0) return null;
        try
        {
            return pv.Vt != 0 && PropVariantToUInt64(ref pv, out ulong n) == 0 ? n : null;
        }
        finally
        {
            _ = PropVariantClear(ref pv);
        }
    }

    [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant pv);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindCtx, int flags, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IPropertyStore store);
    [DllImport("propsys.dll")] private static extern int PropVariantToStringAlloc(ref PropVariant pv, out IntPtr text);
    [DllImport("propsys.dll")] private static extern int PropVariantToUInt64(ref PropVariant pv, out ulong value);
}
