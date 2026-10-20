using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// AI 채팅 in a window of its own (taskbar, Alt+Tab, resizable), laid out like the settings window: the conversations on
/// the left (ChatWindow.Sessions), the open one on the right with its model and level, the composer under it. The same
/// conversations as the launcher's quick chat (Tab): what is asked there shows here, and either side goes on with it.
/// Files and pictures: the 📎 button or Ctrl+O, dropping them on the window, Ctrl+V.
internal sealed partial class ChatWindow : Window, IChatHost
{
    private readonly Host host;
    private readonly Grid root = new() { Background = Theme.P.Solid };
    private readonly ChatView chat;
    private readonly TextBox input;
    private readonly TextBlock inputHint;
    private readonly TextBlock title = Theme.T("", 15, Theme.P.Text, Theme.Semi);
    private readonly StackPanel tools = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock toast = Theme.T("", 12.5, Theme.P.Text, Theme.Medium, false);
    private readonly Border toastCard;
    private readonly List<string> files = new();
    private ChatThread thread;
    private string shownTitle = "";

    /// How wide the conversation and the composer get in a wide window.
    private const double ReadWidth = 760;

    public ChatWindow(Host host, ChatThread first)
    {
        this.host = host;
        thread = first;
        Title = Tr("Bloom AI 채팅", "Bloom AI chat");
        Width = 1060;
        Height = 720;
        MinWidth = 780;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Shots.TrackIcon(this);

        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildSide());
        var line = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetColumn(line, 1);
        root.Children.Add(line);

        // right: the conversation's bar, the conversation, the composer, the keys
        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        var bar = new Grid { Margin = new Thickness(26, 0, 18, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(title);
        Grid.SetColumn(tools, 1);
        bar.Children.Add(tools);
        main.Children.Add(bar);
        main.Children.Add(Rule(1));

        chat = new ChatView(host, this) { ReadWidth = ReadWidth };
        Grid.SetRow(chat, 2);
        main.Children.Add(chat);

        input = new TextBox
        {
            FontFamily = Theme.Sans,
            FontSize = 14,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 168,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 2, 0, 2),
            Template = Launcher.BareTextBox(),
        };
        inputHint = Theme.T(Tr("메시지 입력 — Enter로 보내고 Shift+Enter로 줄을 변경합니다", "Message — Enter sends, Shift+Enter adds a line"), 14, Theme.P.TextFaint);
        inputHint.IsHitTestVisible = false;
        inputHint.VerticalAlignment = VerticalAlignment.Center;
        var field = new Grid();
        field.Children.Add(inputHint);
        field.Children.Add(input);
        var composer = new Grid();
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var attach = IconButton('', Tr("파일 · 사진 첨부 (Ctrl+O)", "Attach files or pictures (Ctrl+O)"), PickFiles, false);
        attach.VerticalAlignment = VerticalAlignment.Bottom;
        composer.Children.Add(attach);
        field.Margin = new Thickness(6, 0, 10, 0);
        Grid.SetColumn(field, 1);
        composer.Children.Add(field);
        var send = IconButton(G.Send, Tr("보내기 (Enter)", "Send (Enter)"), SendChat, true);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(send, 2);
        composer.Children.Add(send);
        var composerCard = new SmoothBorder
        {
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(8, 8, 8, 8),
            Margin = new Thickness(22, 4, 22, 14),
            MaxWidth = ReadWidth,
            Child = composer,
            Cursor = Cursors.IBeam,
        };
        composerCard.MouseLeftButtonDown += (_, _) => input.Focus();
        Grid.SetRow(composerCard, 3);
        main.Children.Add(composerCard);
        main.Children.Add(Rule(4));
        var foot = Hints((Tr("첨부", "Attach"), "Ctrl+O"), (Tr("새 대화", "New chat"), "Ctrl+N"), (Tr("대화 찾기", "Find a chat"), "Ctrl+F"), (Tr("중지", "Stop"), "Esc"));
        Grid.SetRow(foot, 5);
        main.Children.Add(foot);

        toastCard = new SmoothBorder
        {
            Background = Theme.P.Popup,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 7, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 10),
            Child = toast,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Grid.SetRow(toastCard, 2);
        main.Children.Add(toastCard);
        Grid.SetColumn(main, 2);
        root.Children.Add(main);
        Content = root;
        Theme.Scaled(this, root); // 설정 > 일반 > 크기

        input.TextChanged += (_, _) =>
        {
            inputHint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (input.Text.Length > 0) host.Chats.PreStart(thread); // typing: the process starts before Enter
        };
        input.PreviewKeyDown += OnInputKey;
        PreviewKeyDown += OnKey;
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(ThreadFormat) ? DragThread(e, false) : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        DragLeave += (_, _) => Mark(null);
        Drop += (_, e) =>
        {
            if (e.Data.GetDataPresent(ThreadFormat)) DragThread(e, true);
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Attach(paths);
            e.Handled = true;
        };
        host.Chats.Updated += OnUpdated;
        host.Chats.SessionsChanged += OnSessions;
        host.Chats.GroupsChanged += OnSessions;
        GptCatalog.Updated += OnCatalog;
        ApiCatalog.Updated += OnCatalog;
        Theme.Changed += OnTheme;
        Closed += (_, _) =>
        {
            host.Chats.CancelPreStart(thread);
            host.Chats.Updated -= OnUpdated;
            host.Chats.SessionsChanged -= OnSessions;
            host.Chats.GroupsChanged -= OnSessions;
            GptCatalog.Updated -= OnCatalog;
            ApiCatalog.Updated -= OnCatalog;
            Theme.Changed -= OnTheme;
        };
        SourceInitialized += (_, _) => Shots.SolidCaption(this);
        Loaded += (_, _) => input.Focus();
        Open(first);
    }

    /// Shows a conversation (from the side list, the launcher or a command) and puts the cursor in the composer.
    public void Open(ChatThread t)
    {
        if (!ReferenceEquals(thread, t)) host.Chats.CancelPreStart(thread);
        thread = t;
        host.Chats.PreStart(t); // likely to send: its process starts now, unsent
        chat.Show(t);
        Header();
        BuildList();
        input.Focus();
    }

    /// What was typed and attached in the quick chat before it moved here (Ctrl+Shift+O).
    public void Draft(string? text, IReadOnlyList<string>? more)
    {
        if (text is { Length: > 0 })
        {
            input.Text = text;
            input.CaretIndex = text.Length;
        }
        if (more is { Count: > 0 }) Attach(more);
    }

    /// Conversations were deleted (here or in the launcher): off the list, and if the open one went, the last one opens.
    public void Removed(IReadOnlyList<ChatThread> gone)
    {
        if (gone.Contains(thread)) Open(host.Chats.Latest ?? host.Chats.New());
        else BuildList();
    }

    public void Capture(string file) => Shots.Save(root, file);

    public void FindForQa(string text) => find.Text = text;

    // ── IChatHost ───────────────────────────────────────────────────────────────────────────────
    public string? AttachedImage => null;

    public void ShowChat(ChatThread t) => Open(t);

    public void ShowThreads()
    {
        find.Focus();
        find.SelectAll();
    }

    public void SendChat()
    {
        if (thread.Busy) return;
        string text = input.Text.Trim();
        if (text.Length == 0) text = ChatAttach.DefaultText(false, files.Count > 0);
        if (text.Length == 0) return;
        var sending = files.ToList();
        files.Clear();
        chat.ShowPending(files, Unattach);
        input.Clear();
        host.Chats.Send(thread, text, files: sending);
        chat.Show(thread);
        Header();
        BuildList();
    }

    public void PickFiles()
    {
        if (ChatAttach.Pick(this) is { } picked) Attach(picked);
        input.Focus();
    }

    /// Ctrl+M: the model list under its chip.
    public void PickModel()
    {
        if (tools.Children.OfType<Dropdown>().FirstOrDefault() is { } models) models.Open();
    }

    /// Ctrl+Shift+M: Claude ↔ ChatGPT, each side with the model last used there.
    public void SwitchProvider()
    {
        string now = AiModels.Normalize(thread.Model);
        if (!AiModels.IsChatGpt(now)) Settings.Current.AiProvider = "chatgpt";
        Chats.SetModel(thread, AiModels.IsChatGpt(now) ? Settings.Current.AiModel : AiModels.DefaultModel());
        ShowToast(AiModels.Who(thread.Model));
        host.Chats.PreStart(thread);
        Header();
    }

    /// Ctrl+E: the model's levels in turn.
    public void CycleEffort()
    {
        string model = AiModels.Normalize(thread.Model);
        if (!AiModels.HasEffort(model)) return;
        Chats.SetEffort(thread, AiModels.Next(AiModels.Efforts(model), Chats.EffortOf(thread)));
        ShowToast(Tr($"생각 수준: {AiModels.EffortName(Chats.EffortOf(thread))}", $"Thinking level: {AiModels.EffortName(Chats.EffortOf(thread))}"));
        host.Chats.PreStart(thread);
        Header();
    }

    public void FocusComposer()
    {
        input.Focus();
        Keyboard.Focus(input);
    }

    public void ShowToast(string text)
    {
        toast.Text = text;
        toast.TextWrapping = TextWrapping.Wrap;
        toastCard.Visibility = Visibility.Visible;
        toastCard.Opacity = 1;
        var fade = Motion.Paced(new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(Motion.Fades ? 220 : 0)) { BeginTime = TimeSpan.FromMilliseconds(Motion.Fades ? 1300 : 1500) });
        fade.Completed += (_, _) =>
        {
            if (toastCard.Opacity == 0) toastCard.Visibility = Visibility.Collapsed;
        };
        toastCard.BeginAnimation(OpacityProperty, fade);
    }

    // ── the conversation's bar: its name, the model and level, 세션 종료 ──────────────────────
    private void Header()
    {
        // the group in front, faint: 업무 / 3분기 매출 정리
        shownTitle = (host.Chats.GroupOf(thread)?.Name ?? "") + "\n" + NameOf(thread);
        title.Inlines.Clear();
        if (host.Chats.GroupOf(thread) is { } group)
        {
            title.Inlines.Add(new Run(group.Name) { Foreground = Theme.P.TextFaint, FontWeight = Theme.Medium });
            title.Inlines.Add(new Run("  /  ") { Foreground = Theme.P.TextFaint, FontWeight = Theme.Regular });
        }
        title.Inlines.Add(new Run(NameOf(thread)));
        tools.Children.Clear();
        string model = AiModels.Normalize(thread.Model);
        var ids = AiModels.All(); // grouped by side and provider; each label starts with it ("OpenAI · …")
        if (!ids.Contains(model)) ids.Add(model);
        var models = new Dropdown(ids.Select(m => (m, AiModels.Who(m), (string?)null)).ToArray(), model) { ToolTip = Tr("모델 (Ctrl+M)", "Model (Ctrl+M)") };
        models.Picked += m =>
        {
            Chats.SetModel(thread, m);
            host.Chats.PreStart(thread);
            Header();
        };
        tools.Children.Add(models);
        if (AiModels.HasEffort(model))
        {
            var levels = new Dropdown(AiModels.Efforts(model).Select(e => (e, AiModels.EffortName(e), (string?)null)).ToArray(), Chats.EffortOf(thread)) { ToolTip = Tr("생각 수준 (Ctrl+E)", "Thinking level (Ctrl+E)"), Margin = new Thickness(8, 0, 0, 0) };
            levels.Picked += e =>
            {
                Chats.SetEffort(thread, e);
                host.Chats.PreStart(thread);
                Header();
            };
            tools.Children.Add(levels);
        }
        if (host.Chats.IsLive(thread))
        {
            var end = TextButton(G.Stop, Tr("세션 종료", "End session"), () => EndSession(thread));
            end.ToolTip = Tr("대화 메모리를 해제하고 새 대화를 엽니다. 대화 기록은 유지됩니다.", "Frees chat memory and opens a new chat. History stays.");
            end.Margin = new Thickness(8, 0, 0, 0);
            tools.Children.Add(end);
        }
    }

    /// 세션 종료 on a conversation: its CLI closes; when it is the open one, a new chat takes its place (it stays in the
    /// list, and its next message resumes the session).
    private void EndSession(ChatThread t)
    {
        host.Chats.EndSession(t);
        if (ReferenceEquals(t, thread)) Open(host.Chats.New());
    }

    // ── keys ────────────────────────────────────────────────────────────────────────────────────
    private void OnInputKey(object sender, KeyEventArgs e)
    {
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0, ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.Enter && !shift && !ctrl)
        {
            SendChat();
            e.Handled = true;
        }
        else if (e.Key == Key.V && ctrl && !shift && ChatAttach.FromClipboard() is { } pasted)
        {
            Attach(pasted);
            e.Handled = true;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (renaming is { IsKeyboardFocused: true }) return; // its own Enter and Esc
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        // an open menu or model list takes the keys
        if (menu.HandleKey(key) || tools.Children.OfType<Dropdown>().FirstOrDefault(d => d.IsOpen) is { } open && open.HandleKey(key))
        {
            e.Handled = true;
            return;
        }
        if (SelectKey(key, ctrl))
        {
            e.Handled = true;
            return;
        }
        Action? run = key switch
        {
            Key.Escape when thread.Busy => () => host.Chats.Stop(thread),
            Key.Escape when find.IsKeyboardFocused && find.Text.Length > 0 => () => find.Clear(),
            Key.Escape when find.IsKeyboardFocused => () => input.Focus(),
            Key.N when ctrl => () => Open(host.Chats.New()),
            Key.O when ctrl => PickFiles,
            Key.F when ctrl => ShowThreads,
            Key.L when ctrl => ShowThreads,
            Key.M when ctrl && shift => SwitchProvider,
            Key.M when ctrl => PickModel,
            Key.E when ctrl => CycleEffort,
            Key.W when ctrl => Close,
            _ => null,
        };
        if (run is null) return;
        run();
        e.Handled = true;
    }

    // ── attachments ─────────────────────────────────────────────────────────────────────────────
    private void Attach(IEnumerable<string> paths)
    {
        int skipped = ChatAttach.Add(files, paths);
        if (skipped > 0) ShowToast(ChatAttach.Skipped(skipped));
        chat.ShowPending(files, Unattach);
        input.Focus();
    }

    private void Unattach(string path)
    {
        files.Remove(path);
        chat.ShowPending(files, Unattach);
    }

    // ── what the conversations do ───────────────────────────────────────────────────────────────
    private void OnUpdated(ChatThread t)
    {
        if (ReferenceEquals(t, thread)) chat.Update(t);
        if (ListChanged()) BuildList();
        if (ReferenceEquals(t, thread) && shownTitle != (host.Chats.GroupOf(t)?.Name ?? "") + "\n" + NameOf(t)) Header();
    }

    private void OnSessions()
    {
        Header();
        BuildList();
    }

    private void OnCatalog()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible) return;
            bool reopen = tools.Children.OfType<Dropdown>().FirstOrDefault()?.IsOpen == true;
            foreach (var menu in tools.Children.OfType<Dropdown>()) menu.Close();
            Header();
            if (reopen) PickModel();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnTheme()
    {
        Shots.SolidCaption(this);
        root.Background = Background = Theme.P.Solid;
        chat.Show(thread);
        Header();
        BuildList();
    }

    // ── small parts ─────────────────────────────────────────────────────────────────────────────
    private static SmoothBorder Rule(int row)
    {
        var line = new SmoothBorder { Background = Theme.P.Line };
        Grid.SetRow(line, row);
        return line;
    }

    /// The keys under the conversation, as in the settings window.
    private static FrameworkElement Hints(params (string label, string keys)[] hints)
    {
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        for (int i = 0; i < hints.Length; i++)
        {
            var t = Theme.T(hints[i].label, 12, Theme.P.TextDim, Theme.Medium, false);
            t.Margin = new Thickness(0, 0, 3, 0);
            foot.Children.Add(t);
            foot.Children.Add(Keys.Caps(hints[i].keys));
            if (i < hints.Length - 1) foot.Children.Add(new SmoothBorder { Width = 1, Height = 14, Background = Theme.P.Line, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        return foot;
    }

    /// A round glyph button (📎, send); the accent one is the main action.
    private static SmoothBorder IconButton(char glyph, string tip, Action click, bool accent)
    {
        var icon = Theme.Glyph(glyph, 15, accent ? Theme.P.AccentInk : Theme.P.TextDim);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var b = new SmoothBorder
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            Background = accent ? Theme.P.Accent : Brushes.Transparent,
            Child = icon,
            Cursor = Cursors.Hand,
            ToolTip = tip,
        };
        if (!accent)
        {
            b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        }
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    /// A small labelled button (세션 종료, 모두 종료); `tint` colours its words and glyph (삭제 in rose-red).
    private static SmoothBorder TextButton(char? glyph, string label, Action click, Brush? tint = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is { } c)
        {
            var g = Theme.Glyph(c, 11, tint ?? Theme.P.TextDim);
            g.VerticalAlignment = VerticalAlignment.Center;
            g.Margin = new Thickness(0, 1, 6, 0);
            sp.Children.Add(g);
        }
        sp.Children.Add(Theme.T(label, 12, tint ?? Theme.P.Text, Theme.Medium, false));
        var b = new SmoothBorder
        {
            Background = Theme.P.Key,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 5, 11, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = sp,
        };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Theme.P.Key;
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }
}
