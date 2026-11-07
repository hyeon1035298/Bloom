using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using Bloom.Native;

namespace Bloom.App;

/// Global shortcuts picked in the settings ("ctrl+alt+o" …), registered on one message window.
/// Each has a status for the settings line: ok | taken (another app has it) | off.
internal sealed class Hotkeys : IDisposable
{
    private readonly HwndSource window;
    private readonly Dictionary<int, Action> actions = new();
    private readonly Dictionary<string, (int id, string status, string combo)> byName = new();
    private int nextId = 0x200;

    public Hotkeys()
    {
        window = new HwndSource(new HwndSourceParameters("BloomHotkeys") { ParentWindow = Win32.HWND_MESSAGE, Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == (int)Win32.WM_HOTKEY && actions.TryGetValue((int)w.ToInt64(), out var a))
            {
                a();
                handled = true;
            }
            return IntPtr.Zero;
        });
    }

    /// (Re)binds `name` to `combo`; "off" or an unknown combo frees it.
    public void Set(string name, string combo, Action pressed)
    {
        if (byName.TryGetValue(name, out var old))
        {
            if (!paused.Remove(old.id)) Win32.UnregisterHotKey(window.Handle, old.id);
            actions.Remove(old.id);
            byName.Remove(name);
        }
        if (Parse(combo) is not { } k)
        {
            byName[name] = (0, "off", "off");
            return;
        }
        int id = nextId++;
        bool ok = Win32.RegisterHotKey(window.Handle, id, k.mods | Win32.MOD_NOREPEAT, k.vk);
        if (ok) actions[id] = pressed;
        byName[name] = (ok ? id : 0, ok ? "ok" : "taken", combo);
    }

    private readonly HashSet<int> paused = new(); // ids let go while a shortcut is pressed in

    /// While a shortcut is being pressed in (KeyRecorder), Bloom lets go of its own so a combo it holds reaches the
    /// window instead of running; afterwards they are taken again (one another app grabbed meanwhile reads as taken).
    public void Pause(bool on)
    {
        if (on == paused.Count > 0) return;
        foreach (var (name, (id, _, combo)) in new List<KeyValuePair<string, (int id, string status, string combo)>>(byName))
        {
            if (on && id != 0)
            {
                Win32.UnregisterHotKey(window.Handle, id);
                paused.Add(id);
            }
            else if (!on && paused.Contains(id) && (Parse(combo) is not { } k || !Win32.RegisterHotKey(window.Handle, id, k.mods | Win32.MOD_NOREPEAT, k.vk)))
            {
                actions.Remove(id);
                byName[name] = (0, "taken", combo);
            }
        }
        if (!on) paused.Clear();
    }

    public string Status(string name) => byName.TryGetValue(name, out var s) ? s.status : "off";

    /// The name holding `combo` in Bloom (registered or wanted), or null.
    public string? Owner(string combo)
    {
        foreach (var (name, (_, status, c)) in byName)
            if (status != "off" && c == combo) return name;
        return null;
    }

    /// Lets go of every name starting with `prefix` that `keep` doesn't list (item shortcuts that were taken off).
    public void Drop(string prefix, ICollection<string> keep)
    {
        foreach (string name in new List<string>(byName.Keys))
            if (name.StartsWith(prefix, StringComparison.Ordinal) && !keep.Contains(name)) Set(name, "off", () => { });
    }

    /// "Ctrl + Alt + M", "alt+shift+f5", "컨트롤+알트+M", "PrtSc" → "ctrl+alt+m" (modifiers in one order); null when it
    /// isn't a combo Windows can register (one key, and a modifier unless the key types nothing: KeyNames.Alone).
    public static string? Normal(string typed)
    {
        var parts = new List<string>();
        foreach (string raw in typed.ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal).Split('+', StringSplitOptions.RemoveEmptyEntries))
            parts.Add(raw switch
            {
                "control" or "ctl" or "컨트롤" => "ctrl",
                "알트" => "alt",
                "시프트" => "shift",
                "windows" or "윈도우" or "윈도" => "win",
                "스페이스" or "spacebar" => "space",
                "prtsc" or "prtscn" or "printscr" or "print" => "printscreen",
                "del" => "delete",
                "ins" => "insert",
                "pgup" => "pageup",
                "pgdn" => "pagedown",
                "escape" => "esc",
                "return" => "enter",
                _ => raw,
            });
        string[] order = { "ctrl", "alt", "shift", "win" };
        var mods = Array.FindAll(order, parts.Contains);
        var keys = parts.FindAll(p => Array.IndexOf(order, p) < 0);
        if (keys.Count != 1) return null;
        string combo = string.Join("+", mods.Append(keys[0]));
        return Parse(combo) is null ? null : combo;
    }

    /// A key pressed with the modifiers held → "ctrl+alt+m", "printscreen", or null when Windows can't register it (a
    /// modifier alone, a typing key without a modifier). Win is read from the keys themselves (WPF's modifiers leave it out).
    public static string? FromKey(Key key, ModifierKeys mods)
    {
        if (KeyNames.Name(key) is not { } k) return null;
        var parts = new List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("shift");
        if ((mods & ModifierKeys.Windows) != 0 || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("win");
        if (parts.Count == 0 && !KeyNames.Alone(k)) return null;
        parts.Add(k);
        return string.Join("+", parts);
    }

    /// The key a key event is really about (Alt combos come as System, the IME's as ImeProcessed). PrintScreen reaches
    /// WPF only as a key going up, so a shortcut being pressed in takes it there (and only there, never twice).
    public static Key Pressed(KeyEventArgs e) => e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, Key.DeadCharProcessed => e.DeadCharProcessedKey, _ => e.Key };

    /// The modifiers held right now as a label to show while a shortcut is pressed in: "Ctrl+Alt+".
    public static string Held(ModifierKeys mods)
    {
        string s = "";
        if ((mods & ModifierKeys.Control) != 0) s += "Ctrl+";
        if ((mods & ModifierKeys.Alt) != 0) s += "Alt+";
        if ((mods & ModifierKeys.Shift) != 0) s += "Shift+";
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) s += "Win+";
        return s;
    }

    /// "ctrl+alt+o" → (MOD_CONTROL | MOD_ALT, 'O'), "printscreen" → (0, VK_SNAPSHOT): any key KeyNames knows, with a
    /// modifier unless it types nothing.
    public static (uint mods, uint vk)? Parse(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo) || combo == "off") return null;
        uint mods = 0, vk = 0;
        string? key = null;
        foreach (string part in combo.ToLowerInvariant().Split('+'))
        {
            switch (part)
            {
                case "ctrl": mods |= Win32.MOD_CONTROL; break;
                case "alt": mods |= Win32.MOD_ALT; break;
                case "shift": mods |= Win32.MOD_SHIFT; break;
                case "win": mods |= Win32.MOD_WIN; break;
                default:
                    if (key is not null || KeyNames.Of(part) is not { } k) return null;
                    key = part;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                    break;
            }
        }
        if (key is null || vk == 0 || mods == 0 && !KeyNames.Alone(key)) return null;
        return (mods, vk);
    }

    /// Parse's other way: (MOD_CONTROL | MOD_ALT, VK_UP) → "ctrl+alt+up"; "off" for vk 0 or a key KeyNames doesn't know.
    public static string Combo(uint mods, uint vk)
    {
        if (vk == 0 || KeyNames.Name(KeyInterop.KeyFromVirtualKey((int)vk)) is not { } k) return "off";
        var parts = new List<string>();
        if ((mods & Win32.MOD_CONTROL) != 0) parts.Add("ctrl");
        if ((mods & Win32.MOD_ALT) != 0) parts.Add("alt");
        if ((mods & Win32.MOD_SHIFT) != 0) parts.Add("shift");
        if ((mods & Win32.MOD_WIN) != 0) parts.Add("win");
        parts.Add(k);
        return string.Join("+", parts);
    }

    /// "ctrl+alt+o" → "Ctrl+Alt+O", "printscreen" → "PrintScreen"
    public static string Label(string combo) => combo == "off" ? Tr("끔", "Off") : string.Join("+", Parts(combo));

    /// Each key of a combo as it is labelled ("ctrl+numplus" → Ctrl, Num +): one key cap each.
    public static string[] Parts(string combo) =>
        Array.ConvertAll(combo.Split('+'), p => p switch { "ctrl" => "Ctrl", "alt" => "Alt", "shift" => "Shift", "win" => "Win", _ => KeyNames.Label(p) });

    public void Dispose()
    {
        foreach (var (_, (id, _, _)) in byName)
            if (id != 0) Win32.UnregisterHotKey(window.Handle, id);
        window.Dispose();
    }
}
