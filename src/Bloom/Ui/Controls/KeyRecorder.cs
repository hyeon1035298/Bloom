using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bloom.App;

namespace Bloom.Ui;

/// A shortcut pressed in instead of typed (설정's shortcut rows): a well showing the combo; a click or the row's Enter
/// starts listening, the next key pressed with Ctrl / Alt / Shift / Win held becomes the shortcut, Esc keeps the old one,
/// Backspace or Delete turns it off. While it listens Bloom lets go of its own shortcuts (`Listening` → Hotkeys.Pause),
/// so a combo Bloom already holds can be pressed again. The window's key handler leaves the keys to it (`IsListening`):
/// it reads them itself, handled or not, to get Alt's and the IME's real keys.
internal sealed class KeyRecorder : SmoothBorder
{
    private readonly TextBlock text;
    private string value;
    private string? error;
    private bool caps;

    /// At rest the combo shows as key caps ([Ctrl][Alt][↑], Keys.Caps) instead of a line of text (an add-on's page).
    public bool Caps
    {
        get => caps;
        set
        {
            caps = value;
            Show(null);
        }
    }

    /// The new combo ("ctrl+alt+m") or "off".
    public event Action<string>? Picked;

    /// Listening starts (true) or ends (false): Bloom's own shortcuts are let go meanwhile.
    public Action<bool>? Listening;

    /// After a pick or Esc: where the keyboard goes next (설정's filter line).
    public Action? Done;

    /// A combo that can't be had (another of Bloom's shortcuts holds it) → what to say; it keeps listening.
    public Func<string, string?>? Check;

    public bool IsListening { get; private set; }

    public KeyRecorder(string value)
    {
        this.value = value;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 4, 10, 5);
        MinWidth = 96;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        text = Theme.T("", 12.5, Theme.P.Text, Theme.Medium, false);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        Child = text;
        MouseLeftButtonUp += (_, e) =>
        {
            Start();
            e.Handled = true;
        };
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (!IsListening) return;
            e.Handled = true;
            if (Hotkeys.Pressed(e) is var key && key != Key.Snapshot) Press(key);
        }), true);
        AddHandler(PreviewKeyUpEvent, new KeyEventHandler((_, e) =>
        {
            if (!IsListening) return;
            e.Handled = true;
            if (Hotkeys.Pressed(e) == Key.Snapshot) Press(Key.Snapshot); // PrintScreen comes only as a key going up
            else Show(error is null ? Hotkeys.Held(Keyboard.Modifiers) : "");
        }), true);
        LostKeyboardFocus += (_, _) => Stop();
        Show(null);
    }

    public void Start()
    {
        if (IsListening) return;
        IsListening = true;
        error = null;
        Listening?.Invoke(true);
        Focus();
        Keyboard.Focus(this);
        Show("");
    }

    private void Press(Key key)
    {
        var mods = Keyboard.Modifiers;
        bool none = mods == ModifierKeys.None && !Keyboard.IsKeyDown(Key.LWin) && !Keyboard.IsKeyDown(Key.RWin);
        if (none && key == Key.Escape)
        {
            Finish(null);
            return;
        }
        if (none && key is Key.Back or Key.Delete)
        {
            Finish("off");
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            error = null;
            Show(Hotkeys.Held(mods));
            return;
        }
        if (Hotkeys.FromKey(key, mods) is { } combo)
        {
            if (combo != value && Check?.Invoke(combo) is { } taken)
            {
                error = taken;
                Show("");
                return;
            }
            Finish(combo);
            return;
        }
        error = none ? Tr("Ctrl · Alt · Shift · Win과 함께", "With Ctrl, Alt, Shift or Win") : Tr("다른 키 조합을 누르세요", "Press another key combination");
        Show("");
    }

    /// Ends listening; `picked` (a combo or "off") is told after Bloom's shortcuts are taken back, so the new one registers.
    private void Finish(string? picked)
    {
        Stop();
        if (picked is not null && picked != value)
        {
            value = picked;
            Show(null);
            Picked?.Invoke(picked);
        }
        Done?.Invoke();
    }

    private void Stop()
    {
        if (!IsListening) return;
        IsListening = false;
        error = null;
        Listening?.Invoke(false);
        Show(null);
    }

    /// null = the combo itself; otherwise the modifiers held so far ("" = nothing yet).
    private void Show(string? held)
    {
        bool on = held is not null;
        Background = on ? Theme.P.AccentSoft : Theme.P.Well;
        BorderBrush = on ? Theme.P.Accent : Theme.P.Line;
        text.Foreground = on ? Theme.P.AccentText : value == "off" ? Theme.P.TextFaint : Theme.P.Text;
        text.Text = !on ? Hotkeys.Label(value)
            : error ?? (held!.Length > 0 ? held + "…" : Tr("키를 누르세요", "Press keys"));
        if (!on && caps && value != "off")
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (string part in Hotkeys.Parts(value))
            {
                var cap = Keys.Cap(part);
                if (row.Children.Count == 0) cap.Margin = new Thickness(0);
                row.Children.Add(cap);
            }
            Child = row;
        }
        else Child = text;
        ToolTip = on ? Tr("Esc 취소 · Backspace 끄기", "Esc cancels · Backspace turns it off") : Tr("눌러서 바꾸기", "Click to change");
    }
}
