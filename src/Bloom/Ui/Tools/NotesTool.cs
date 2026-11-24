using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 메모: the list of notes (search, pinned first) and one note open to write in (MdPane: written first, 보기 draws it
/// as Markdown); every keystroke and ticked box is kept.
internal sealed class NotesTool : ITool
{
    private readonly Grid view = new();
    private readonly Grid listPage = new();
    private readonly Grid page = new();
    private readonly StackPanel list = new() { Margin = new Thickness(8, 4, 8, 8) };
    private readonly TextBox search;
    private readonly StackPanel head = new() { Orientation = Orientation.Horizontal };
    private readonly Border modeSlot = new(); // the open note's 보기 / 편집
    private readonly SmoothBorder backButton, deleteButton;
    private readonly TextBlock saved;
    private Note? note; // the open one; null: the list
    private MdPane? pane; // the open note's text

    public string Key => "notes";
    public string Name => Tr("메모", "Notes");
    public char Glyph => G.Document;
    public FrameworkElement View => view;
    public FrameworkElement? Head => head;
    public string? State => note?.Id;

    public NotesTool(string? noteId)
    {
        // the list: a search field, a new note, the notes
        listPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var (field, box) = ToolUi.Field(Tr("메모 찾기 · Enter로 새 메모", "Search notes · Press Enter for new note"));
        search = box;
        field.Margin = new Thickness(12, 10, 12, 4);
        listPage.Children.Add(field);
        var scroll = ToolUi.Scroll(list);
        Grid.SetRow(scroll, 1);
        listPage.Children.Add(scroll);
        search.TextChanged += (_, _) => Fill();
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            string q = search.Text.Trim();
            var hit = q.Length == 0 ? null : Matching(q).FirstOrDefault();
            Open(hit ?? Notes.New(q));
        };

        // one note: the text (a pane per note, Open), and when it was kept
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        saved = Theme.T("", 11.5, Theme.P.TextFaint);
        saved.Margin = new Thickness(14, 2, 14, 8);
        Grid.SetRow(saved, 1);
        page.Children.Add(saved);
        page.Visibility = Visibility.Collapsed;
        view.Children.Add(listPage);
        view.Children.Add(page);

        backButton = ToolUi.Button('', Tr("메모 목록", "Notes list"), () => Open(null));
        deleteButton = ToolUi.Button(G.Delete, Tr("이 메모 지우기 (휴지통으로)", "Delete this note (to Recycle Bin)"), () =>
        {
            if (note is { } n) Notes.Delete(n);
            note = null;
            Open(null);
        });
        head.Children.Add(modeSlot);
        head.Children.Add(backButton);
        head.Children.Add(deleteButton);
        head.Children.Add(ToolUi.Button(G.Add, Tr("새 메모", "New note"), () => Open(Notes.New())));

        Notes.Changed += OnChanged;
        Open(noteId is null ? null : Notes.Find(noteId));
    }

    /// Shows a note by its id (the search line's results open their note here); the one already open stays as it is
    /// (보기 or 편집; what was written into it is in its pane already, OnChanged).
    public void Show(string id)
    {
        if (Notes.Find(id) is { } n && !ReferenceEquals(n, note)) Open(n);
    }

    /// QA: a note being written, as typed so far (a new one on the first call).
    public void TypeForQa(string text)
    {
        if (note is null) Open(Notes.New());
        if (pane is null) return;
        pane.Text = text;
        Typed(text);
    }

    /// QA: the open note's pane (보기, the text size).
    public MdPane? PaneForQa => pane;

    private void Open(Note? n)
    {
        // an empty note left behind just goes
        if (note is { } was && !ReferenceEquals(was, n) && was.Text.Trim().Length == 0 && Notes.Find(was.Id) is not null) Notes.Delete(was);
        note = n;
        listPage.Visibility = n is null ? Visibility.Visible : Visibility.Collapsed;
        page.Visibility = n is null ? Visibility.Collapsed : Visibility.Visible;
        modeSlot.Visibility = backButton.Visibility = deleteButton.Visibility = n is null ? Visibility.Collapsed : Visibility.Visible;
        if (pane is not null) page.Children.Remove(pane);
        pane = null;
        modeSlot.Child = null;
        if (n is null)
        {
            search.Clear();
            Fill();
        }
        else
        {
            var p = new MdPane(n.Text, false, null, false, false);
            p.OpenLink = url => DocWindow.FollowLink(url, null, p); // a note has no folder: absolute links only
            pane = p;
            pane.Edited += Typed;
            page.Children.Insert(0, pane);
            modeSlot.Child = pane.ToggleButton;
            saved.Text = Tr($"자동 저장 · {ToolUi.Ago(n.UpdatedAt)}", $"Auto-saved · {ToolUi.Ago(n.UpdatedAt)}");
        }
        FocusFirst();
    }

    /// The open note's text as typed (or a box ticked in 보기): kept.
    private void Typed(string text)
    {
        if (note is null) return;
        Notes.SetText(note, text);
        saved.Text = Tr("저장됨", "Saved");
    }

    private static System.Collections.Generic.IEnumerable<Note> Matching(string q)
    {
        var query = new Query(q);
        return Notes.Ordered.Where(n => Matcher.Score(query, n.Match) > 0);
    }

    private void OnChanged(Note? changed)
    {
        // the list follows every change; the open note what was written into it elsewhere (the AI's WriteNote) — its own
        // keystrokes and ticks are in the pane already
        if (note is null) Fill();
        else if (ReferenceEquals(changed, note) && pane is { } p && p.Text != note.Text) p.Text = note.Text;
    }

    private void Fill()
    {
        list.Children.Clear();
        string q = search.Text.Trim();
        var notes = (q.Length == 0 ? Notes.Ordered : Matching(q)).ToList();
        if (notes.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(q.Length == 0 ? Tr("저장된 메모 없음 · 위에 입력하고 Enter로 저장하세요", "No saved notes · Type above and press Enter to save") : Tr("검색된 메모 없음 · Enter로 새 메모 저장", "No matching notes · Press Enter to save as new note")));
            return;
        }
        foreach (var n in notes)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Theme.T(n.Title, 13, Theme.P.Text, Theme.Semi));
            string rest = string.Join(" ", n.Text.Split('\n').Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0));
            var sub = Theme.T(ToolUi.Ago(n.UpdatedAt) + (rest.Length > 0 ? " · " + rest : ""), 11.5, Theme.P.TextFaint);
            sub.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(sub);
            g.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pin = ToolUi.Button(n.Pinned ? '' : '', n.Pinned ? Tr("고정 풀기", "Unpin") : Tr("맨 위에 고정", "Pin to top"), () => Notes.SetPinned(n, !n.Pinned), n.Pinned ? Theme.P.Accent : null);
            var delete = ToolUi.Button(G.Delete, Tr("지우기 (휴지통으로)", "Delete (to Recycle Bin)"), () => Notes.Delete(n));
            // a pinned note shows its pin; the rest of the buttons come under the mouse
            delete.Visibility = Visibility.Hidden;
            if (!n.Pinned) pin.Visibility = Visibility.Hidden;
            buttons.Children.Add(pin);
            buttons.Children.Add(delete);
            Grid.SetColumn(buttons, 1);
            g.Children.Add(buttons);
            var row = ToolUi.Row(g, () => Open(n));
            row.MouseEnter += (_, _) => pin.Visibility = delete.Visibility = Visibility.Visible;
            row.MouseLeave += (_, _) =>
            {
                delete.Visibility = Visibility.Hidden;
                if (!n.Pinned) pin.Visibility = Visibility.Hidden;
            };
            list.Children.Add(row);
        }
    }

    public void FocusFirst()
    {
        UIElement target = pane?.KeyTarget ?? search;
        target.Dispatcher.BeginInvoke(() =>
        {
            if (Window.GetWindow(target) is { IsActive: false }) return; // shown for the AI beside a chat: the keyboard stays in the chat
            target.Focus();
            Keyboard.Focus(target);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Closed()
    {
        Notes.Changed -= OnChanged;
        if (note is { } n && n.Text.Trim().Length == 0 && Notes.Find(n.Id) is not null) Notes.Delete(n);
    }
}
