using System;
using System.Linq;
using System.Windows.Input;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Ui;

/// Keyboard: the search box's own keys, the list's, the chat's.
internal sealed partial class Launcher
{
    // ── keyboard ────────────────────────────────────────────────────────────────────────────────
    private void OnBoxKey(object sender, KeyEventArgs e)
    {
        // keys the TextBox would otherwise consume
        if (e.Key == Key.Tab && Mode == Mode.Root && PathInput.Looks(box.Text))
        {
            // a typed path: Tab goes into the selected folder (never to the AI)
            if (Engine.DigInto(box.Text, Selected) is { } into)
            {
                box.Text = into;
                box.CaretIndex = into.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && Mode == Mode.Root && Bloom.Core.Features.On("chat"))
        {
            // Tab: the conversation it picks up (if any); Shift+Tab: always a new one
            bool fresh = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            string q = box.Text.Trim();
            if (q.Length == 0 && !fresh && QuickPending) ShowChat(thread!); // back to the answer left running
            else if (q.Length > 0) host.Ask(q, null, fresh);
            else host.OpenChat(fresh ? null : host.TabThread);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Back && box.Text.Length == 0 && Mode == Mode.Form)
        {
            FormBack();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && box.Text.Length == 0 && Mode is not Mode.Root and not Mode.Chat)
        {
            Go(Mode.Root);
            e.Handled = true;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0, shift = (mods & ModifierKeys.Shift) != 0;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (actions.IsOpen)
        {
            if (ctrl && key == Key.J) OpenTools(); // the 도구 menu again: it closes
            else if (actions.HandleKey(key)) e.Handled = true;
            if (ctrl && key == Key.J) e.Handled = true;
            return;
        }
        if (Mode == Mode.Form && form?.Current.Keys == true && key is not Key.Enter and not Key.Escape)
        {
            FormKeys(e); // before Ctrl+K, Ctrl+P …, which could be the shortcut being pressed in
            return;
        }
        if (key == Key.Escape && CancelHold()) // a home block (or a card) held goes back
        {
            e.Handled = true;
            return;
        }
        if (key == Key.Escape && HomeEditing && Mode == Mode.Root && Engine.CloseCommandCards()) // “+ 명령 추가”'s cards too
        {
            e.Handled = true;
            return;
        }
        if (key == Key.Escape && HomeEditing && Mode == Mode.Root && addOpen is { } col) // the open cards close first
        {
            e.Handled = true;
            ToggleAdd(col);
            return;
        }
        if (key == Key.Escape && HomeEditing && Mode == Mode.Root) // in a form (✎ on a usage card) Esc leaves the form first
        {
            e.Handled = true;
            ToggleHomeEdit();
            return;
        }
        if (ctrl && key == Key.E && Mode == Mode.Root && box.Text.Length == 0)
        {
            ToggleHomeEdit();
            e.Handled = true;
            return;
        }
        if (key == Key.Escape)
        {
            e.Handled = true;
            if (Mode == Mode.Form) Go(form?.Back ?? Mode.Root);
            else if (Mode != Mode.Root) Go(Mode.Root);
            else if (box.Text.Length > 0) box.Clear();
            else Dismiss();
            return;
        }
        if (ctrl && key == Key.K)
        {
            OpenActions();
            e.Handled = true;
            return;
        }
        if (ctrl && key == Key.OemComma)
        {
            host.OpenSettings();
            e.Handled = true;
            return;
        }
        // 📌, the 도구 menu, and Bloom's size (Ctrl+= bigger, Ctrl+- smaller, Ctrl+0 back to 100 %)
        if (ctrl && key is Key.P or Key.J or Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract or Key.D0 or Key.NumPad0)
        {
            if (key == Key.P) SetPinned(!Pinned);
            else if (key == Key.J) OpenTools();
            else if (key is Key.OemPlus or Key.Add) Theme.StepScale(1);
            else if (key is Key.OemMinus or Key.Subtract) Theme.StepScale(-1);
            else Theme.SetScale(1);
            e.Handled = true;
            return;
        }
        if (Mode == Mode.Chat)
        {
            ChatKey(key, ctrl, shift, e);
            return;
        }
        if (Mode == Mode.Form)
        {
            if (key == Key.Enter)
            {
                FormNext();
                e.Handled = true;
            }
            return;
        }
        // Bloom Lens: 1–8 pick the action right away
        if (Mode == Mode.Lens && box.Text.Length == 0 && !ctrl && key is >= Key.D1 and <= Key.D8 && rows.Count >= key - Key.D1 + 1)
        {
            Select(key - Key.D1, false);
            RunSelected(null);
            e.Handled = true;
            return;
        }
        // 컬러 팔레트: ← → the swatch picked (its gradients below), − + one colour fewer or more
        if (Mode == Mode.Palette && box.Text.Length == 0 && !ctrl && host.Palette is { } palette)
        {
            int pick = key switch { Key.Left => -1, Key.Right => 1, _ => 0 };
            int more = key switch { Key.OemMinus or Key.Subtract => -1, Key.OemPlus or Key.Add => 1, _ => 0 };
            if (pick != 0) host.PickSwatch(palette.Selected + pick, false);
            else if (more != 0) _ = host.SetPaletteCount(palette.Count + more);
            if (pick != 0 || more != 0)
            {
                e.Handled = true;
                return;
            }
        }
        if (Mode == Mode.Files && ctrl && key == Key.Tab)
        {
            StepFileKind(shift ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (Mode == Mode.Clipboard && ctrl && key == Key.Tab)
        {
            StepClipTab(); // two tabs: either way is the other one
            e.Handled = true;
            return;
        }
        if (Mode == Mode.Calendar && box.Text.Length == 0 && !shift)
        {
            DateTime? to = key switch
            {
                Key.Left => calDay.AddDays(ctrl ? -7 : -1),
                Key.Right => calDay.AddDays(ctrl ? 7 : 1),
                Key.PageUp => calDay.AddMonths(-1),
                Key.PageDown => calDay.AddMonths(1),
                Key.Home => DateTime.Today,
                _ => null,
            };
            if (to is { } day)
            {
                CalendarGo(day);
                e.Handled = true;
                return;
            }
        }
        switch (key)
        {
            case Key.Left when inStrip && ctrl && shift:
                if (Selected is { } fl) MoveFavorite(fl, -1);
                e.Handled = true;
                break;
            case Key.Right when inStrip && ctrl && shift:
                if (Selected is { } fr) MoveFavorite(fr, 1);
                e.Handled = true;
                break;
            case Key.Left when inStrip:
                SelectStrip(stripSel - 1);
                e.Handled = true;
                break;
            case Key.Right when inStrip:
                SelectStrip(stripSel + 1);
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9 when ctrl && !shift && Mode == Mode.Root:
                RunFavorite(key - Key.D1);
                e.Handled = true;
                break;
            case Key.Down when ctrl && shift:
                if (Selected is { } d) MoveFavorite(d, 1);
                e.Handled = true;
                break;
            case Key.Up when ctrl && shift:
                if (Selected is { } u) MoveFavorite(u, -1);
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                Move(8);
                e.Handled = true;
                break;
            case Key.PageUp:
                Move(-8);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected(ctrl && shift ? "Ctrl+Shift+Enter" : ctrl ? "Ctrl+Enter" : null);
                e.Handled = true;
                break;
            case Key.D when ctrl:
                if (Selected is { Pinnable: true } f) ToggleFavorite(f);
                else RunSelected("Ctrl+D"); // clipboard: pin
                e.Handled = true;
                break;
            case Key.Delete when ctrl:
                RunSelected("Ctrl+Delete");
                e.Handled = true;
                break;
            case Key.S when ctrl:
                RunSelected("Ctrl+S");
                e.Handled = true;
                break;
            case Key.W when ctrl:
                RunSelected("Ctrl+W"); // 창 전환: close the window
                e.Handled = true;
                break;
            case Key.C when ctrl && shift:
                RunSelected("Ctrl+Shift+C");
                e.Handled = true;
                break;
            case Key.C when ctrl && box.SelectionLength == 0 && Selected is { } c:
                if (c.Kind == ItemKind.Calc) c.Run?.Invoke();
                else
                {
                    var act = ActionsFor(c).FirstOrDefault(a => a.Keys == "Ctrl+Shift+C");
                    act?.Run();
                }
                if (c.Kind == ItemKind.Calc || ActionsFor(c).Any(a => a.Keys == "Ctrl+Shift+C")) ShowToast(Tr("복사 완료", "Copied"));
                e.Handled = true;
                break;
        }
    }

    /// A shortcut field in a form (Ctrl+K › 단축키 지정): the keys pressed show as the combo instead of going in as text;
    /// Backspace clears it (and on an empty field goes back), Esc and Enter are the form's.
    private void FormKeys(KeyEventArgs e, bool up = false)
    {
        Key key = Hotkeys.Pressed(e);
        if (key is Key.Escape or Key.Tab) return;
        e.Handled = true;
        if ((key == Key.Snapshot) != up) return; // PrintScreen only as a key going up, the rest only going down
        var mods = Keyboard.Modifiers;
        if (key is Key.Back or Key.Delete && mods == ModifierKeys.None)
        {
            if (box.Text.Length == 0) FormBack();
            else SetBox("");
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (Hotkeys.FromKey(key, mods) is { } combo)
        {
            form!.Error = null;
            SetBox(Hotkeys.Label(combo));
        }
        else form!.Error = Engine.KeysHint;
        Refresh(false);
    }

    private void ChatKey(Key key, bool ctrl, bool shift, KeyEventArgs e)
    {
        switch (key)
        {
            case Key.Enter when !shift:
                SendChat();
                e.Handled = true;
                break;
            case Key.OemPeriod when ctrl:
                StopChat();
                e.Handled = true;
                break;
            case Key.N when ctrl:
                ShowChat(host.Chats.New());
                e.Handled = true;
                break;
            case Key.O when ctrl && shift:
                OpenInWindow();
                e.Handled = true;
                break;
            case Key.O when ctrl:
                PickFiles();
                e.Handled = true;
                break;
            case Key.V when ctrl && !shift:
                if (PasteAttachment()) e.Handled = true;
                break;
            case Key.L when ctrl:
                Go(Mode.Threads);
                e.Handled = true;
                break;
            case Key.M when ctrl && shift:
                SwitchProvider();
                e.Handled = true;
                break;
            case Key.M when ctrl:
                PickModel();
                e.Handled = true;
                break;
            case Key.E when ctrl:
                CycleEffort();
                e.Handled = true;
                break;
            case Key.C when ctrl && shift:
                if (thread?.Messages.LastOrDefault(m => m.Role == "assistant" && m.Text.Length > 0) is { } last)
                {
                    Launch.Copy(last.Text);
                    ShowToast(Tr("답변 복사 완료", "Answer copied"));
                }
                e.Handled = true;
                break;
            case Key.Up when box.Text.Length == 0:
            case Key.PageUp:
                chat.ScrollBy(-120);
                e.Handled = true;
                break;
            case Key.Down when box.Text.Length == 0:
            case Key.PageDown:
                chat.ScrollBy(120);
                e.Handled = true;
                break;
        }
    }
}
