using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using static Bloom.Native.WinRt;

namespace Bloom.Native;

/// The switches behind 시스템 (Engine.System.cs): Windows' light/dark mode, sound (Core Audio), the default
/// output device, the Bluetooth / Wi-Fi radios (Windows.Devices.Radios), Explorer's hidden files and extensions,
/// sleep, the display and the recycle bin. Every call answers false / null instead of throwing.
internal static unsafe class SystemControl
{
    // ── light / dark ────────────────────────────────────────────────────────────────────────────
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool DarkMode
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Personalize);
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
    }

    /// Apps and the Windows shell together, like the switch in 설정 > 개인 설정 > 색.
    public static bool SetDarkMode(bool dark)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Personalize);
            k.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            Broadcast("ImmersiveColorSet");
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Core.Log.Error("dark mode", e);
            return false;
        }
    }

    // ── Explorer ────────────────────────────────────────────────────────────────────────────────
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static bool HiddenFilesShown
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Advanced);
            return k?.GetValue("Hidden") is int v && v == 1;
        }
    }

    public static bool ExtensionsShown
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Advanced);
            return k?.GetValue("HideFileExt") is int v && v == 0;
        }
    }

    public static bool SetHiddenFiles(bool show) => SetExplorer("Hidden", show ? 1 : 2);

    public static bool SetExtensions(bool show) => SetExplorer("HideFileExt", show ? 0 : 1);

    private static bool SetExplorer(string name, int value)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Advanced);
            k.SetValue(name, value, RegistryValueKind.DWord);
            Win32.SHChangeNotify(Win32.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
            Broadcast("ShellState");
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Core.Log.Error("explorer " + name, e);
            return false;
        }
    }

    private static void Broadcast(string area) =>
        _ = SendMessageTimeout(new IntPtr(0xFFFF), 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, area, 0x2 /* SMTO_ABORTIFHUNG */, 300, out _);

    // ── sound ───────────────────────────────────────────────────────────────────────────────────
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private static IAudioEndpointVolume? Endpoint(int flow)
    {
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (en.GetDefaultAudioEndpoint(flow, 1 /* eMultimedia */, out var dev) != 0 || dev is null) return null;
            Guid iid = IID_IAudioEndpointVolume;
            return dev.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out object o) == 0 ? o as IAudioEndpointVolume : null;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            Core.Log.Error("audio", e);
            return null;
        }
    }

    /// 0 … 100, or null without an output device.
    public static int? Volume
    {
        get
        {
            var v = Endpoint(0);
            return v is not null && v.GetMasterVolumeLevelScalar(out float f) == 0 ? (int)Math.Round(f * 100) : null;
        }
    }

    public static bool SetVolume(int percent)
    {
        var v = Endpoint(0);
        if (v is null) return false;
        Guid ctx = Guid.Empty;
        if (percent > 0) _ = v.SetMute(0, ref ctx);
        return v.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx) == 0;
    }

    /// Output (render) or the microphone (capture); null without that device.
    public static bool? Muted(bool mic)
    {
        var v = Endpoint(mic ? 1 : 0);
        return v is not null && v.GetMute(out int m) == 0 ? m != 0 : null;
    }

    public static bool SetMuted(bool mic, bool muted)
    {
        var v = Endpoint(mic ? 1 : 0);
        Guid ctx = Guid.Empty;
        return v is not null && v.SetMute(muted ? 1 : 0, ref ctx) == 0;
    }

    public sealed record AudioDevice(string Id, string Name, bool Default);

    /// The active output devices, the default one marked.
    public static List<AudioDevice> Outputs()
    {
        var list = new List<AudioDevice>();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            string? def = en.GetDefaultAudioEndpoint(0, 1, out var d) == 0 && d is not null && d.GetId(out string did) == 0 ? did : null;
            if (en.EnumAudioEndpoints(0, 1 /* DEVICE_STATE_ACTIVE */, out var all) != 0 || all is null) return list;
            _ = all.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                if (all.Item(i, out var dev) != 0 || dev is null || dev.GetId(out string id) != 0) continue;
                list.Add(new AudioDevice(id, FriendlyName(dev) ?? id, id == def));
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            Core.Log.Error("audio devices", e);
        }
        return list;
    }

    private static string? FriendlyName(IMMDevice dev)
    {
        if (dev.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0 || store is null) return null;
        var key = new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 14); // PKEY_Device_FriendlyName
        if (store.GetValue(ref key, out var pv) != 0) return null;
        try
        {
            return pv.Vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(pv.Pointer) : null;
        }
        finally
        {
            _ = PropertyStore.PropVariantClear(ref pv);
        }
    }

    /// Makes `id` the default output for every role (console, multimedia, communications) — the same
    /// undocumented IPolicyConfig the Windows sound panel uses.
    public static bool SetDefaultOutput(string id)
    {
        try
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            bool ok = true;
            for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(id, role) == 0;
            return ok;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            Core.Log.Error("audio default", e);
            return false;
        }
    }

    // ── radios ──────────────────────────────────────────────────────────────────────────────────
    private static readonly Guid IID_IRadioStatics = new("5fb6a12e-67cb-46ae-aae9-65919f86eff4");

    public const int Wifi = 1, Bluetooth = 3; // Windows.Devices.Radios.RadioKind

    /// The radio's state: true = on, false = off, null = no such radio (or it can't be read).
    public static Task<bool?> Radio(int kind) => OnMta(() => WithRadio(kind, r => (bool?)(GetInt(r, First + 3) == 1)), null, "radio");

    public static Task<bool> SetRadio(int kind, bool on) => OnMta(() => WithRadio(kind, r =>
    {
        IntPtr op;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Slot(r, First))(r, on ? 1 : 2, &op)); // SetStateAsync(RadioState)
        try
        {
            if (!Await(op, 8000)) return false;
            int access; // IAsyncOperation<RadioAccessStatus>.GetResults: 1 = allowed
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(op, First + 2))(op, &access));
            return access == 1;
        }
        finally
        {
            Marshal.Release(op);
        }
    }), false, "radio set");

    private static T? WithRadio<T>(int kind, Func<IntPtr, T> use)
    {
        var release = new List<IntPtr>();
        try
        {
            IntPtr statics = Keep(release, Factory("Windows.Devices.Radios.Radio", IID_IRadioStatics));
            IntPtr op = Keep(release, Call(statics, First)); // GetRadiosAsync
            if (!Await(op, 5000)) return default;
            IntPtr view = Keep(release, Results(op));
            foreach (IntPtr r in Items(view))
            {
                Keep(release, r);
                if (GetInt(r, First + 5) == kind) return use(r); // get_Kind
            }
            return default;
        }
        finally
        {
            ReleaseAll(release);
        }
    }

    // ── power, display, recycle bin ─────────────────────────────────────────────────────────────
    public static bool Sleep() => SetSuspendState(false, false, false);

    /// The display goes dark (a key or the mouse wakes it); after a moment so the Enter that chose it is up.
    public static void DisplayOff() =>
        _ = Task.Delay(600).ContinueWith(_ => Win32.PostMessage(new IntPtr(0xFFFF), 0x0112 /* WM_SYSCOMMAND */, new IntPtr(0xF170 /* SC_MONITORPOWER */), new IntPtr(2)), TaskScheduler.Default);

    public static bool EmptyRecycleBin() => SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4 /* no confirmation, progress, sound */) is 0 or unchecked((int)0x8000FFFF) /* already empty */;

    // ── interop ─────────────────────────────────────────────────────────────────────────────────
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute(int mute, ref Guid context);
        [PreserveSig] int GetMute(out int mute);
    }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // only the slots' places matter for the ten before SetDefaultEndpoint; they are never called
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }

    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupEventsDisabled);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
