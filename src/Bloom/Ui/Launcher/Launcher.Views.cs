using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// Views of the one window: switching, the preview pane, forms and folders.
internal sealed partial class Launcher
{
    // ── views ───────────────────────────────────────────────────────────────────────────────────
    public void Go(Mode mode, string text = "")
    {
        actions.Close();
        SetMode(mode);
        suppressText = true;
        box.Text = text;
        box.CaretIndex = text.Length;
        suppressText = false;
        if (mode != Mode.Chat) Refresh(false);
        UpdateChrome();
        if (IsVisible) FocusBox();
        KeysField();
    }

    /// While a shortcut field is open, Bloom lets go of its own shortcuts so they can be pressed in (Hotkeys.Pause).
    private void KeysField() => host.Shortcuts?.Pause(IsVisible && Mode == Mode.Form && form?.Current.Keys == true);

    private void SetMode(Mode mode)
    {
        if (mode == Mode.Clipboard) EnterClipTab(Mode);
        Mode = mode;
        scroll.Visibility = mode is Mode.Chat ? Visibility.Collapsed : Visibility.Visible;
        bool split = Split, cal = mode == Mode.Calendar;
        body.ColumnDefinitions[0].Width = new GridLength(split ? 0.44 : cal ? 0.6 : 1, GridUnitType.Star);
        body.ColumnDefinitions[1].Width = split ? new GridLength(0.56, GridUnitType.Star) : cal ? new GridLength(0.4, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(scroll, cal ? 1 : 0);
        month.Visibility = cal ? Visibility.Visible : Visibility.Collapsed;
        if (cal) calDay = DateTime.Today;
        previewFrame.Visibility = preview.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        if (!split) ShowPreview(null);
        if (mode != Mode.Form) form = null;
        chat.Visibility = mode == Mode.Chat ? Visibility.Visible : Visibility.Collapsed;
        LeaveChat(host.Chats, thread, mode);
        if (mode != Mode.Chat)
        {
            chatTick.Stop();
            attachment = null;
            attachedImage = null;
            attachedFiles.Clear();
            chat.ShowPending(attachedFiles, Unattach);
        }
        work.Working = false;
        UpdateChrome();
    }

    /// Going to a view other than the chat (Esc to home, another list) ends the quick chat's unsent process, or the start
    /// still waiting: the chat is no longer likely to send.
    internal static void LeaveChat(Chats chats, ChatThread? quick, Mode now)
    {
        if (now != Mode.Chat && quick is not null) chats.CancelPreStart(quick);
    }

    public void ShowChat(ChatThread t, string? draft = null, (string title, string url, string text)? page = null, string? image = null)
    {
        if (thread is not null && !ReferenceEquals(thread, t)) host.Chats.CancelPreStart(thread);
        thread = t;
        host.Chats.PreStart(t); // likely to send: its process starts now, unsent
        quickBusy = t.Busy;
        quickUnseen = false;
        attachment = page;
        attachedImage = image;
        Go(Mode.Chat, draft ?? "");
        chat.Show(t);
        chatTick.Start();
        UpdateChrome();
    }

    /// A CLI session started or ended: the quick chat's 세션 종료 chip follows.
    public void SessionsChanged()
    {
        if (Mode == Mode.Chat) UpdateChrome();
    }

    public void ChatChanged(ChatThread t)
    {
        if (!ReferenceEquals(t, thread)) return;
        QuickChanged(t);
        if (Mode == Mode.Chat) chatDirty = true;
    }

    public void DataChanged()
    {
        if (!IsVisible) return;
        if (hold is not null) // a home block or card held: once it is let go (Launcher.HomeDrag.cs)
        {
            refreshAfterHold = true;
            return;
        }
        if (View(Mode).Live) Refresh(true);
    }

    /// Once a second (Host): the live accessories (a timer's time left) on the rows shown.
    public void TickLive()
    {
        if (!IsVisible) return;
        foreach (var r in rows) r.Tick?.Invoke();
    }

    /// The calendar on another day (click, ←→, PgUp/PgDn, Home).
    private void CalendarGo(DateTime day)
    {
        calDay = day.Date;
        Refresh(false);
    }

    public void CalendarGoForQa(DateTime day) => CalendarGo(day);

    public void ScrollForQa(double y) => scroll.ScrollToVerticalOffset(y);

    /// The ids of the rows shown, top to bottom (QA).
    public List<string> ResultIdsForQa() => rows.ConvertAll(r => r.Item?.Id ?? "");

    /// The home's week strip: the calendar on that day.
    public void OpenCalendar(DateTime day)
    {
        Go(Mode.Calendar);
        CalendarGo(day);
    }

    /// File search: only this kind (전체 · 폴더 · 문서 · 사진 · 영상 · 음악 · 실행 파일 · 압축).
    public void SetFileKind(string key)
    {
        fileKind = Engine.FileKind(key).key;
        if (Mode == Mode.Files) Refresh(false);
        UpdateChrome();
    }

    private void StepFileKind(int d)
    {
        var keys = Engine.FileKinds.Select(k => k.key).ToList();
        SetFileKind(keys[(keys.IndexOf(fileKind) + d + keys.Count) % keys.Count]);
    }

    private void PickFileKind()
    {
        var list = Engine.FileKinds.Select(k => new ActionItem
        {
            Title = k.name,
            Glyph = k.key == fileKind ? G.Check : k.glyph,
            StayOpen = true,
            Run = () => SetFileKind(k.key),
        }).ToList();
        actions.Open(Tr("파일 종류 · Ctrl+Tab으로 넘기기", "File kind · Ctrl+Tab steps"), list, Array.FindIndex(Engine.FileKinds, k => k.key == fileKind));
    }

    /// Views with the preview pane on the right.
    private bool Split => View(Mode).Split;

    private PreviewLoader? previewLoader;

    private void DisposePreview() => previewLoader?.Dispose();

    private void ShowPreview(ResultItem? item)
    {
        string? id = item is null || (item.Detail is null && item.ReadDetail is null) ? null : item.Id;
        if (id is not null && id == previewId) return; // the same row after a refresh
        previewId = id;
        previewLoader?.Invalidate();
        if (item?.ReadDetail is { } read)
        {
            preview.Show(null); // stop the previous video/decode while the selected file is read
            if (IsVisible)
            {
                previewLoader ??= new PreviewLoader(Dispatcher, () => IsVisible, d => preview.Show(d));
                _ = previewLoader.Request(read);
            }
            else previewId = null; // reopening the same row must still start its preview
            return;
        }
        Detail? d = null;
        try
        {
            d = item?.Detail?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error("preview " + item?.Id, e);
        }
        preview.Show(d);
    }

    // ── forms (a few questions in the search line) ──────────────────────────────────────────────
    /// A favorites folder's apps, as a list to pick from.
    public void OpenGroup(string id)
    {
        if (!IsVisible) host.Summon();
        groupId = id;
        Go(Mode.Group);
    }

    /// A favorites folder's icon: one of the work glyphs, a file, or back to the small icons inside.
    public void PickGroupIcon(string id)
    {
        if (Favorites.Group(id) is not { } g) return;
        void Set(string? icon)
        {
            Favorites.SetGroupIcon(id, icon);
            Refresh(true);
        }
        var list = Engine.GroupGlyphs.Select(x => new ActionItem
        {
            Title = x.name,
            Glyph = x.glyph,
            StayOpen = true,
            Run = () => Set($"glyph:{(int)x.glyph:X4}"),
        }).ToList();
        // the picture icons (and AI drawing) live here too, so the folder's own menu has one icon line
        list.Insert(0, new ActionItem { Title = Tr("아이콘 그림 고르기…", "Pick an icon picture…"), Glyph = G.Palette, StayOpen = true, Run = () => PickPicture(id, g.Title) });
        list.Add(new ActionItem { Title = Tr("파일에서 고르기… (이미지 · 아이콘 · 프로그램)", "From a file… (image, icon or program)"), Glyph = G.Folder, StayOpen = true, Run = () => PickIconFile(id) });
        if (g.Icon is not null) list.Add(new ActionItem { Title = Tr("기본으로 (안의 앱 아이콘)", "Default (the icons inside)"), Glyph = G.Refresh, StayOpen = true, Run = () => Set(null) });
        actions.Open(Tr($"‘{ClipText(g.Title, 16)}’ 아이콘", $"Icon for ‘{ClipText(g.Title, 16)}’"), list);
    }

    /// The launcher steps aside for the file dialog (it hides when another window takes the focus).
    private void PickIconFile(string id)
    {
        Dismiss();
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Tr("폴더 아이콘으로 쓸 파일", "A file for the folder icon"),
            Filter = Tr("이미지 · 아이콘 · 프로그램|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.ico;*.exe;*.lnk|모든 파일|*.*", "Images, icons, programs|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.ico;*.exe;*.lnk|All files|*.*"),
        };
        bool ok = dlg.ShowDialog() == true;
        host.Summon();
        if (!ok) return;
        Favorites.SetGroupIcon(id, dlg.FileName);
        Go(Mode.Root);
    }

    public void StartForm(FormSpec f)
    {
        if (!IsVisible) host.Summon();
        f.Step = 0;
        f.Error = null;
        f.Prepare?.Invoke(f, 0);
        form = f;
        Go(Mode.Form, f.Current.Value);
    }

    private void FormNext()
    {
        if (form is not { } f) return;
        var field = f.Current;
        var (value, error) = field.Check?.Invoke(box.Text) ?? (box.Text.Trim(), null);
        if (error is not null || value is null)
        {
            f.Error = error ?? Tr("다시 시도하세요", "Try again");
            Refresh(false);
            return;
        }
        field.Value = value;
        f.Error = null;
        if (f.Step + 1 >= f.Fields.Count)
        {
            form = null;
            KeysField(); // Bloom's shortcuts back before Done registers the new one
            f.Done(f);
            return;
        }
        f.Step++;
        f.Prepare?.Invoke(f, f.Step);
        SetBox(f.Current.Value);
        Refresh(false);
        KeysField();
    }

    private void FormBack()
    {
        if (form is not { } f) return;
        if (f.Step == 0)
        {
            Go(f.Back);
            return;
        }
        f.Current.Value = box.Text;
        f.Step--;
        f.Error = null;
        SetBox(f.Current.Value);
        Refresh(false);
        KeysField();
    }

    private void SetBox(string text)
    {
        suppressText = true;
        box.Text = text;
        box.CaretIndex = text.Length;
        suppressText = false;
        UpdateChrome();
    }
}
