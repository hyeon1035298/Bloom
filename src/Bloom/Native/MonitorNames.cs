using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Bloom.Native;

/// The monitors connected now, by the number in their GDI name (\\.\DISPLAY<n>, the number add-ons pick a monitor by),
/// with the name the monitor gives itself (its model name, from its EDID through Windows' display configuration) and
/// whether it is the main one; where it sits on the desktop (pixels) and its scale (1 = 96 DPI), for the add-on pages'
/// map of the monitors. Read when a settings page shows a monitor choice, never on a poll.
internal static class MonitorNames
{
    internal sealed record Screen(int Number, string Name, bool Primary)
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public double Scale { get; init; } = 1;
    }

    public static IReadOnlyList<Screen> Read()
    {
        var list = new List<Screen>();
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return list;
            var paths = new PATH_INFO[pathCount];
            var modes = new MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return list;
            for (int i = 0; i < pathCount; i++)
            {
                var p = paths[i];
                var source = new SOURCE_NAME { header = new HEADER { type = GET_SOURCE_NAME, size = Marshal.SizeOf<SOURCE_NAME>(), adapterId = p.sourceAdapter, id = p.sourceId } };
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;
                string gdi = source.viewGdiDeviceName ?? "";
                const string Prefix = @"\\.\DISPLAY";
                if (!gdi.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !int.TryParse(gdi[Prefix.Length..], out int number)) continue;
                var target = new TARGET_NAME { header = new HEADER { type = GET_TARGET_NAME, size = Marshal.SizeOf<TARGET_NAME>(), adapterId = p.targetAdapter, id = p.targetId } };
                string name = DisplayConfigGetDeviceInfo(ref target) == 0 ? (target.monitorFriendlyDeviceName ?? "").Trim() : "";
                // the main monitor is the one at the desktop's origin
                var mode = p.sourceModeIndex < modeCount && modes[p.sourceModeIndex].infoType == MODE_SOURCE ? modes[p.sourceModeIndex] : default;
                bool primary = mode is { infoType: MODE_SOURCE, x: 0, y: 0 };
                if (list.Exists(s => s.Number == number)) continue; // a cloned display: once
                list.Add(new Screen(number, name, primary) { X = mode.x, Y = mode.y, Width = (int)mode.width, Height = (int)mode.height, Scale = ScaleAt(mode) });
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            /* no display configuration API: no names */
        }
        list.Sort((a, b) => a.Number.CompareTo(b.Number));
        return list;
    }

    /// The scale Windows gives the monitor whose middle that is (1 when unknown).
    private static double ScaleAt(MODE_INFO mode)
    {
        if (mode.width == 0) return 1;
        var mon = Win32.MonitorFromPoint(new Win32.POINT { X = mode.x + (int)mode.width / 2, Y = mode.y + (int)mode.height / 2 }, Win32.MONITOR_DEFAULTTONEAREST);
        return Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
    }

    private const uint QDC_ONLY_ACTIVE_PATHS = 2, GET_SOURCE_NAME = 1, GET_TARGET_NAME = 2, MODE_SOURCE = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint low;
        public int high;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO
    {
        public LUID sourceAdapter;
        public uint sourceId, sourceModeIndex, sourceFlags;
        public LUID targetAdapter;
        public uint targetId, targetModeIndex, outputTechnology, rotation, scaling, refreshNumerator, refreshDenominator, scanLineOrdering;
        public int targetAvailable;
        public uint targetFlags, flags;
    }

    /// DISPLAYCONFIG_MODE_INFO, 64 bytes; for a source mode its size in pixels is at 16 and the desktop position of its
    /// top left at 28.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(16)] public uint width;
        [FieldOffset(20)] public uint height;
        [FieldOffset(28)] public int x;
        [FieldOffset(32)] public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HEADER
    {
        public uint type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SOURCE_NAME
    {
        public HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_NAME
    {
        public HEADER header;
        public uint flags, outputTechnology;
        public ushort edidManufactureId, edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PATH_INFO[] paths, ref uint modeCount, [Out] MODE_INFO[] modes, IntPtr topologyId);

    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME request);

    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME request);
}
