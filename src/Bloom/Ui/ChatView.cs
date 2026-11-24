using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// A conversation: in the launcher under the search line (the composer there), or in the AI chat window. Only the
/// message being streamed is re-rendered; the rest stays as built.
internal sealed class ChatView : Grid, IComposerHost
{
    private readonly Host host;
    private readonly IChatHost owner;
    private readonly ScrollViewer scroll;
    private readonly StackPanel stack = new() { Margin = new Thickness(26, 16, 26, 18) };
    private ChatThread? shown;
    private readonly Dictionary<ChatMessage, FrameworkElement> built = new();
    private readonly Dictionary<ChatMessage, string> builtText = new();

    public ChatView(Host host, IChatHost owner)
    {
        this.host = host;
        this.owner = owner;
        scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        scroll.Template = Launcher.ThinScroll();
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(scroll);
        SetRow(pending, 1);
        Children.Add(pending);
    }

    public void FocusComposer() => owner.FocusComposer();

    /// What an empty conversation shows in place of 무엇이든 물어보세요, when the window has its own (a new T3 chat's
    /// folder cards); null = the usual.
    public Func<ChatThread, FrameworkElement?>? EmptyFor { get; set; }

    public void ShowToast(string text) => owner.ShowToast(text);

    /// The AI chat window's conversation keeps a readable line length however wide the window gets (centered).
    public double ReadWidth
    {
        set
        {
            stack.MaxWidth = value;
            pending.MaxWidth = value;
        }
    }

    /// The files waiting for the next message, under the conversation (✕ takes one out).
    private readonly WrapPanel pending = new() { Margin = new Thickness(22, 0, 22, 8), Visibility = Visibility.Collapsed };

    public void ShowPending(IReadOnlyList<string> files, Action<string> remove)
    {
        pending.Children.Clear();
        pending.Visibility = files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (string f in files)
        {
            var chip = FileChip(f, false);
            var x = Theme.Glyph(G.Close, 9, Theme.P.TextFaint);
            x.Margin = new Thickness(8, 1, 0, 0);
            x.Cursor = System.Windows.Input.Cursors.Hand;
            x.ToolTip = Tr("빼기", "Remove");
            string file = f;
            x.MouseLeftButtonUp += (_, e) =>
            {
                remove(file);
                e.Handled = true;
            };
            ((StackPanel)chip.Child).Children.Add(x);
            chip.Margin = new Thickness(0, 6, 6, 0);
            pending.Children.Add(chip);
        }
    }

    /// A file as a small chip: its icon, its name, its size; a click opens it.
    private static SmoothBorder FileChip(string path, bool open)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Icons.Build(IconSpec.Shell(path), 16);
        icon.Margin = new Thickness(0, 0, 7, 0);
        sp.Children.Add(icon);
        var name = Theme.T(Path.GetFileName(path), 12, Theme.P.Text, Theme.Medium);
        name.MaxWidth = 220;
        name.VerticalAlignment = VerticalAlignment.Center;
        sp.Children.Add(name);
        long size = File.Exists(path) ? new FileInfo(path).Length : -1;
        if (size >= 0)
        {
            var sz = Theme.T(Engine.Size(size), 11, Theme.P.TextFaint, Theme.Regular, false);
            sz.Margin = new Thickness(7, 0, 0, 0);
            sz.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(sz);
        }
        var chip = new SmoothBorder
        {
            Child = sp,
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 5, 10, 5),
            ToolTip = path,
        };
        if (open)
        {
            chip.Cursor = System.Windows.Input.Cursors.Hand;
            chip.MouseLeftButtonUp += (_, e) =>
            {
                Launch.Open(path);
                e.Handled = true;
            };
        }
        return chip;
    }

    /// 열기 on a file the AI made: a document opens; a file that would run something (a script, a program, a shortcut) is
    /// only shown in its folder, so a single click never runs what the AI wrote.
    private static void OpenMade(string path)
    {
        if (!RunnableFiles.Is(path))
        {
            Launch.Open(path);
            return;
        }
        Launch.Reveal(path);
        Notices.Result(false, Tr("Bloom · AI 파일", "Bloom · AI File"), Tr($"‘{Path.GetFileName(path)}’ 폴더에서 표시", $"Showing ‘{Path.GetFileName(path)}’ in folder"),
            Tr("열면 실행되는 파일이라 바로 열지 않습니다. 내용을 확인한 뒤 직접 여세요.", "Executable files are not opened directly. Verify the file and open it manually."));
    }

    /// A link in an AI answer: a web address in the browser, a file or folder on this PC shown in its folder (never run);
    /// a path on another computer is refused without looking at it (Windows would sign in there with the user's account),
    /// and every other address goes to Web.Open, which refuses it.
    private static void FollowLink(string url)
    {
        var (kind, target) = DocLink.Resolve(url, null);
        if (kind == DocLink.Kind.Web || (kind == DocLink.Kind.None && !url.TrimStart().StartsWith('#')))
        {
            Browser.Web.Open(kind == DocLink.Kind.Web ? target : url);
            return;
        }
        if (kind == DocLink.Kind.None) return;
        if (DocLink.IsNetwork(target))
        {
            Log.Write("chat: refused a link to another computer");
            Notices.Result(false, Tr("Bloom · 링크", "Bloom · Link"), Tr("링크 열지 않음", "Link blocked"), Tr("다른 컴퓨터의 파일 링크는 열지 않습니다", "File links pointing to other computers are blocked"));
            return;
        }
        if (File.Exists(target) || Directory.Exists(target)) Launch.Reveal(target);
        else Notices.Result(false, Tr("Bloom · 링크", "Bloom · Link"), Tr($"‘{Path.GetFileName(target)}’ 파일 없음", $"File ‘{Path.GetFileName(target)}’ not found"), null);
    }

    /// A file the AI made: a card with 열기 · 저장 · 폴더.
    private static FrameworkElement MadeFile(string path)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Icons.Build(IconSpec.Shell(path), 26);
        icon.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(Path.GetFileName(path), 13, Theme.P.Text, Theme.Medium));
        text.Children.Add(Theme.T(File.Exists(path) ? Tr($"{Engine.Size(new FileInfo(path).Length)} · AI 생성 파일", $"{Engine.Size(new FileInfo(path).Length)} · AI-generated file") : Tr("파일이 없습니다. 다시 생성하거나 위치를 확인하세요.", "File not found. Generate it again or check its location."), 11.5, Theme.P.TextFaint));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        acts.Children.Add(Small(Tr("열기", "Open"), () => OpenMade(path)));
        acts.Children.Add(Small(Tr("저장…", "Save as…"), () => SaveCopy(path)));
        acts.Children.Add(Small(Tr("폴더", "Folder"), () => Launch.Reveal(path)));
        Grid.SetColumn(acts, 2);
        g.Children.Add(acts);
        return new SmoothBorder
        {
            Child = g,
            Background = Theme.P.Card,
            BorderBrush = Theme.P.CardLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 8, 8),
            Margin = new Thickness(0, 8, 0, 0),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    private static SmoothBorder Small(string label, Action click)
    {
        var b = new SmoothBorder
        {
            Child = Theme.T(label, 12, Theme.P.TextDim, Theme.Medium, false),
            Background = Theme.P.Key,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 3, 9, 4),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    /// 저장…: a copy where the user picks (Downloads first).
    private static void SaveCopy(string path)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Tr("파일 저장", "Save File"),
            FileName = Path.GetFileName(path),
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.Copy(path, dlg.FileName, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("save made file", e);
            MessageBox.Show(Tr("파일을 저장하지 못했습니다. 저장 위치와 권한을 확인하고 다시 시도하세요.", "Could not save the file. Check the destination and permissions, then try again."), "Bloom", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ScrollBy(double dy) => scroll.ScrollToVerticalOffset(scroll.VerticalOffset + dy);

    public void Show(ChatThread t)
    {
        // messages that just arrived rise in (the question flowing in from the search line)
        int fresh = ReferenceEquals(t, shown) ? builtCount : t.Messages.Count;
        shown = t;
        updateFor = null; // a change still waiting belongs to what was shown before (or is drawn now)
        built.Clear();
        builtText.Clear();
        stack.Children.Clear();
        if (t.Messages.Count == 0)
        {
            builtCount = 0;
            stack.Children.Add(EmptyFor?.Invoke(t) ?? Empty());
            return;
        }
        for (int i = 0; i < t.Messages.Count; i++)
        {
            var m = t.Messages[i];
            var el = Build(m, t);
            built[m] = el;
            builtText[m] = Key(m);
            stack.Children.Add(el);
            if (i >= fresh) Rise(el, (i - fresh) * 60);
        }
        builtCount = t.Messages.Count;
        Dispatcher.BeginInvoke(() => scroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private int builtCount;

    // the answer being streamed keeps its document between updates: only its last, still open blocks are made again
    private ChatMessage? streamed;
    private MarkdownStream? stream;

    private FrameworkElement Answer(ChatMessage m)
    {
        if (!m.Streaming)
        {
            if (ReferenceEquals(m, streamed)) (streamed, stream) = (null, null); // done: drawn whole, as any answer
            return Markdown.Render(m.Text, FollowLink);
        }
        if (!ReferenceEquals(m, streamed)) (streamed, stream) = (m, new MarkdownStream());
        var view = Markdown.Render(m.Text, FollowLink, stream: stream);
        if (view.Parent is Panel before) before.Children.Remove(view); // out of the message drawn before this one
        return view;
    }

    private static void Rise(FrameworkElement el, int delayMs)
    {
        if (!Motion.Moves) return;
        var move = new TranslateTransform(0, 8);
        el.RenderTransform = move;
        el.Opacity = 0;
        var ease = new System.Windows.Media.Animation.QuinticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var begin = TimeSpan.FromMilliseconds(delayMs);
        move.BeginAnimation(TranslateTransform.YProperty, Motion.Paced(new System.Windows.Media.Animation.DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease, BeginTime = begin }));
        el.BeginAnimation(OpacityProperty, Motion.Paced(new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease, BeginTime = begin }));
    }

    private static string Key(ChatMessage m) => $"{m.Text.Length}|{m.Status}|{m.Streaming}|{m.Error}|{m.Files?.Count}|{m.Asks.Count}|{m.Done?.Count}|{m.Tokens}|{m.Question?.RequestId}|{m.ApprovalWait}";

    /// An API answer's tokens, quietly under it; the whole chat's so far in its tooltip.
    private static FrameworkElement TokensLine(ApiTokens used, ChatThread t)
    {
        var all = t.Messages.Aggregate(default(ApiTokens), (sum, x) => x.Tokens is { } n ? sum + n : sum);
        var line = Theme.T(Tr($"토큰 입력 {ApiTokens.Short(used.In)} · 출력 {ApiTokens.Short(used.Out)}", $"Tokens: {ApiTokens.Short(used.In)} in · {ApiTokens.Short(used.Out)} out"),
            11, Theme.P.TextFaint, Theme.Regular, false);
        line.Margin = new Thickness(0, 6, 0, 0);
        line.ToolTip = Tr($"이 대화 전체: 입력 {all.In:N0} · 출력 {all.Out:N0} 토큰", $"Chat total: {all.In:N0} in · {all.Out:N0} out tokens");
        return line;
    }

    /// An attached picture, small, opening in the default viewer.
    private static FrameworkElement Thumb(string path)
    {
        var pic = new BitmapImage();
        pic.BeginInit();
        pic.CacheOption = BitmapCacheOption.OnLoad;
        pic.DecodePixelWidth = 300;
        pic.UriSource = new Uri(path);
        pic.EndInit();
        pic.Freeze();
        var view = new Image { Source = pic, Stretch = Stretch.Uniform, MaxWidth = 150, MaxHeight = 110 };
        RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
        var b = new SmoothBorder { CornerRadius = new CornerRadius(10), BorderBrush = Theme.P.CardLine, BorderThickness = new Thickness(1), ClipToBounds = true, Child = view, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Path.GetFileName(path) };
        b.MouseLeftButtonUp += (_, e) =>
        {
            Launch.Open(path);
            e.Handled = true;
        };
        return b;
    }

    /// Re-renders the messages whose text or status changed (in practice: the last one).
    /// A streamed answer changes with every few words: the changes of one moment are drawn once, after the dispatcher
    /// has taken in all the pieces that arrived (Background priority), not once per piece. The launcher already paces
    /// its calls (chatTick, 50 ms); the chat window calls on every piece.
    public void Update(ChatThread t)
    {
        updateFor = t;
        if (updateQueued) return;
        updateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            updateQueued = false;
            if (updateFor is { } u) UpdateNow(u);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private ChatThread? updateFor;
    private bool updateQueued;

    private void UpdateNow(ChatThread t)
    {
        if (!ReferenceEquals(t, shown) || stack.Children.Count == 0 || t.Messages.Count != built.Count)
        {
            Show(t);
            return;
        }
        bool atEnd = scroll.VerticalOffset >= scroll.ScrollableHeight - 48;
        for (int i = 0; i < t.Messages.Count; i++)
        {
            var m = t.Messages[i];
            if (builtText.TryGetValue(m, out string? k) && k == Key(m)) continue;
            var el = Build(m, t);
            int at = stack.Children.IndexOf(built[m]);
            if (at < 0) continue;
            stack.Children.RemoveAt(at);
            stack.Children.Insert(at, el);
            built[m] = el;
            builtText[m] = Key(m);
        }
        if (atEnd) Dispatcher.BeginInvoke(() => scroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement Empty()
    {
        // Bloom Lens: the picture that goes with the first message, where the sparkle would be
        if (owner.AttachedImage is { } img && File.Exists(img))
        {
            var lens = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
            var pic = new BitmapImage();
            pic.BeginInit();
            pic.CacheOption = BitmapCacheOption.OnLoad;
            pic.DecodePixelWidth = 640;
            pic.UriSource = new Uri(img);
            pic.EndInit();
            pic.Freeze();
            var view = new Image { Source = pic, Stretch = Stretch.Uniform, MaxWidth = 380, MaxHeight = 190 };
            RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
            lens.Children.Add(new SmoothBorder { CornerRadius = new CornerRadius(12), BorderBrush = Theme.P.CardLine, BorderThickness = new Thickness(1), ClipToBounds = true, Child = view, HorizontalAlignment = HorizontalAlignment.Center });
            var lt = Theme.T(Tr("이 화면에 대해 물어보세요", "Ask about this screen"), 16, Theme.P.Text, Theme.Medium, false);
            lt.HorizontalAlignment = HorizontalAlignment.Center;
            lt.Margin = new Thickness(0, 16, 0, 6);
            lens.Children.Add(lt);
            var ls = Theme.T(Tr("빈 입력에서 Enter로 화면 설명 · 질문마다 사용량 1회", "Press Enter while empty to describe the screen · Uses 1 request per question"), 12.5, Theme.P.TextFaint, Theme.Regular, false);
            ls.HorizontalAlignment = HorizontalAlignment.Center;
            lens.Children.Add(ls);
            return lens;
        }
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) };
        var mark = Theme.Sparkle(30, Theme.P.Accent);
        sp.Children.Add(mark);
        var t = Theme.T(Tr("무엇이든 물어보세요", "Ask anything"), 17, Theme.P.Text, Theme.Medium, false);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 16, 0, 6);
        sp.Children.Add(t);
        var s = Theme.T(Tr("최신 정보는 웹에서 찾아 답합니다 · 파일은 끌어다 놓거나 Ctrl+O", "Searches the web for up-to-date answers · Drag files here or press Ctrl+O"), 12.5, Theme.P.TextFaint, Theme.Regular, false);
        s.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(s);
        return sp;
    }

    private FrameworkElement Build(ChatMessage m, ChatThread t)
    {
        if (m.Role == "user")
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(80, 4, 0, 14) };
            if (m.Image is { } img && File.Exists(img))
            {
                var pic = new BitmapImage();
                pic.BeginInit();
                pic.CacheOption = BitmapCacheOption.OnLoad;
                pic.DecodePixelWidth = 440;
                pic.UriSource = new Uri(img);
                pic.EndInit();
                pic.Freeze();
                var view = new Image { Source = pic, Stretch = Stretch.Uniform, MaxWidth = 220, MaxHeight = 150 };
                RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
                col.Children.Add(new SmoothBorder
                {
                    CornerRadius = new CornerRadius(10),
                    BorderBrush = Theme.P.CardLine,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 0, 5),
                    ClipToBounds = true,
                    Child = view,
                    ToolTip = Tr("Bloom Lens 화면", "Bloom Lens capture"),
                });
            }
            if (m.Files is { Count: > 0 } files)
            {
                var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 5) };
                foreach (string f in files)
                {
                    FrameworkElement el;
                    if (Chats.ImageType(f) is not null && File.Exists(f)) el = Thumb(f);
                    else el = FileChip(f, true);
                    el.Margin = new Thickness(6, 0, 0, 5);
                    wrap.Children.Add(el);
                }
                col.Children.Add(wrap);
            }
            if (m.Page is { } page)
            {
                var chip = new SmoothBorder
                {
                    Background = Theme.P.Tile,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 2, 8, 3),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 0, 5),
                    Child = Theme.T(Tr($"페이지 · {(page.Length > 40 ? page[..40] + "…" : page)}", $"Page · {(page.Length > 40 ? page[..40] + "…" : page)}"), 11.5, Theme.P.TextDim, Theme.Medium),
                };
                col.Children.Add(chip);
            }
            var tb = ReadText.Plain(m.Text, 14, Theme.Regular, fit: true);
            col.Children.Add(new SmoothBorder
            {
                Background = Theme.P.UserBubble,
                CornerRadius = new CornerRadius(14, 14, 4, 14),
                Padding = new Thickness(14, 12, 14, 7), // the font's line box sits low: this centres the ink (a Hangul line measured 23 px over, 35 under at 2×)
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = tb,
            });
            return col;
        }
        var answer = new StackPanel { Margin = new Thickness(0, 2, 40, 18) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Theme.Sparkle(12, Theme.P.Accent));
        var who = Theme.T(AiModels.Who(t.Model), 11.5, Theme.P.TextFaint, Theme.Medium, false);
        who.Margin = new Thickness(7, 0, 0, 0);
        head.Children.Add(who);
        answer.Children.Add(head);
        if (m.Text.Length > 0)
        {
            if (m.Error)
            {
                answer.Children.Add(ReadText.Plain(m.Text, 13.5, Theme.Regular, Theme.P.Danger));
            }
            else answer.Children.Add(Answer(m));
        }
        if (m.Files is { Count: > 0 } made)
            foreach (string f in made) answer.Children.Add(MadeFile(f));
        if (m.Done is { Count: > 0 } done)
            foreach (string d in done) answer.Children.Add(DoneLine(d));
        foreach (var ask in m.Asks) answer.Children.Add(AskCard(t, ask));
        if (m.Question is { } question) answer.Children.Add(QuestionBox(t, question));
        else if (m.ApprovalWait) answer.Children.Add(ApprovalWait(t));
        if (!m.Streaming && !m.Error && m.Text.Length > 0) answer.Children.Add(CopyButton(m.Text));
        if (!m.Streaming && m.Tokens is { } used) answer.Children.Add(TokensLine(used, t));
        if (m.Streaming)
        {
            var st = Theme.T(m.Status ?? (m.Text.Length == 0 ? Tr("생각하는 중…", "Thinking…") : Tr("쓰는 중…", "Writing…")), 12.5, Theme.P.TextFaint, Theme.Regular, false);
            st.Margin = new Thickness(0, m.Text.Length > 0 ? 8 : 0, 0, 0);
            answer.Children.Add(st);
        }
        return answer;
    }

    /// What the AI did (or was told not to) on the PC in this answer: ✓ allowed, ✕ declined; ℹ a note about its tools
    /// (an MCP server left out).
    private static FrameworkElement DoneLine(string line)
    {
        bool ok = line.StartsWith('✓');
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var g = Theme.Glyph(ok ? G.Check : line.StartsWith('ℹ') ? G.Info : G.Close, 10, ok ? Theme.P.Accent : Theme.P.TextFaint);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 1, 7, 0);
        sp.Children.Add(g);
        sp.Children.Add(Theme.T(line.Length > 2 ? line[2..] : line, 12, ok ? Theme.P.TextDim : Theme.P.TextFaint, Theme.Regular));
        return sp;
    }

    /// A tool call waiting for the user: what it will do (all of it: a command or path whole, AskDetail), 허용 · 이 대화에서
    /// 항상 허용 · 거절.
    private SmoothBorder AskCard(ChatThread t, ToolAsk ask)
    {
        var col = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var shield = Theme.Glyph(G.Shield, 12, Theme.P.Accent);
        shield.VerticalAlignment = VerticalAlignment.Center;
        shield.Margin = new Thickness(0, 1, 7, 0);
        top.Children.Add(shield);
        string title = ToolPermissions.IsMcp(ask.Key) ? Tr("MCP 도구 승인", "MCP Tool Approval")
            : ToolPermissions.IsFileOrCommand(ask.Key) ? Tr("파일 · 명령 승인", "File & Command Approval") : Tr("PC 작업 승인", "PC Action Approval");
        top.Children.Add(Theme.T(title, 12, Theme.P.TextDim, Theme.Medium, false));
        col.Children.Add(top);
        int colon = ask.Says.IndexOf(": ", StringComparison.Ordinal); // "옮기기: a.pdf → …" → 항상 허용 names just 옮기기
        string verb = colon > 0 ? ask.Says[..colon] : "";
        bool detail = ask.Detail.Length > 0;
        var what = Markdown.Para(14, Theme.Medium);
        what.Margin = new Thickness(0, 6, 0, detail ? 6 : 10);
        what.Text = detail && verb.Length > 0 ? verb : ask.Says; // with the whole below, the action's name is enough
        col.Children.Add(what);
        if (detail) col.Children.Add(AskDetail(ask.Detail));
        var buttons = new WrapPanel();
        buttons.Children.Add(AskButton(Tr("허용", "Allow"), true, () => host.Chats.Answer(t, ask, true)));
        // a question that is always put (MustAsk) has no "always": it would not be kept
        if (!ask.MustAsk)
            buttons.Children.Add(AskButton(verb.Length > 0 ? Tr($"이 대화에서 ‘{verb}’ 항상 허용", $"Always allow ‘{verb}’ in this chat") : Tr("이 대화에서 항상 허용", "Always allow in this chat"),
                false, () => host.Chats.Answer(t, ask, true, true)));
        buttons.Children.Add(AskButton(Tr("거절", "Deny"), false, () => host.Chats.Answer(t, ask, false)));
        col.Children.Add(buttons);
        return new SmoothBorder
        {
            Background = Theme.P.Card,
            BorderBrush = Theme.P.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 11, 14, 12),
            Margin = new Thickness(0, 10, 0, 2),
            Child = col,
        };
    }

    /// A T3 chat's question (AskUserQuestion in its thread) in the orange box: its choices and 기타 as the notice cards
    /// ask them (NoticeQuestionsView), 답하기 sending the answers to T3, and T3에서 열기.
    private SmoothBorder QuestionBox(ChatThread t, T3Question question)
    {
        var col = new StackPanel();
        col.Children.Add(WaitHead('\uE897', Tr("질문 · 답을 기다리는 중", "Question · Waiting for answer")));
        col.Children.Add(NoticeQuestionsView.Build(new CardQuestions(question.Items, given => host.Chats.Answer(t, given)),
            [(Tr("T3에서 열기", "Open in T3"), () => T3Chats.OpenInT3(t))]));
        return WaitBox(col);
    }

    /// A T3 chat whose thread waits on an approval (T3 lists none of those to its agents, so only T3 can give it), or an
    /// Antigravity thread that waits on anything (view-only: answered in T3).
    private static SmoothBorder ApprovalWait(ChatThread t)
    {
        var col = new StackPanel();
        bool viewOnly = t.T3 is { ViewOnly: true };
        col.Children.Add(WaitHead(G.Shield, viewOnly ? Tr("T3에서 답을 기다려요", "Waiting for answer in T3") : Tr("T3에서 승인을 기다려요", "Waiting for approval in T3")));
        var about = Theme.T(Hangul.KeepAll(viewOnly ? T3Threads.ViewOnlyNote : Tr("권한 승인은 T3 앱에서만 할 수 있어요.", "Permission requests can only be approved in the T3 app.")), 12.5, Theme.P.TextDim, Theme.Regular, false);
        about.TextWrapping = TextWrapping.Wrap;
        about.Margin = new Thickness(0, 6, 0, 10);
        col.Children.Add(about);
        col.Children.Add(ToolUi.TextButton(Tr("T3에서 열기", "Open in T3"), () => T3Chats.OpenInT3(t)));
        return WaitBox(col);
    }

    private static FrameworkElement WaitHead(char glyph, string text)
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Theme.Glyph(glyph, 12, Theme.P.Warn);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.Margin = new Thickness(0, 1, 7, 0);
        top.Children.Add(g);
        top.Children.Add(Theme.T(text, 12, Theme.P.Warn, Theme.Semi, false));
        return top;
    }

    /// The orange box (Theme.P.Warn, its line strong and its fill faint).
    private static SmoothBorder WaitBox(UIElement content)
    {
        var warn = Theme.P.Warn.Color;
        var line = new SolidColorBrush(Color.FromArgb(140, warn.R, warn.G, warn.B));
        var fill = new SolidColorBrush(Color.FromArgb(16, warn.R, warn.G, warn.B));
        line.Freeze();
        fill.Freeze();
        return new SmoothBorder
        {
            Background = fill,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 11, 14, 12),
            Margin = new Thickness(0, 10, 0, 2),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = content,
        };
    }

    /// A card's command or path, never cut: monospace on the code tint, wrapped, scrolling inside past AskDetailHeight
    /// (the wheel goes on to the conversation once it is at its end, or when there's nothing to scroll).
    private static SmoothBorder AskDetail(string detail)
    {
        var scroll = new ScrollViewer
        {
            Content = new TextBlock { Text = detail, FontFamily = Theme.Mono, FontSize = 12.5, LineHeight = 19, Foreground = Theme.P.Text, TextWrapping = TextWrapping.Wrap },
            MaxHeight = AskDetailHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Template = Launcher.ThinScroll(),
        };
        scroll.PreviewMouseWheel += (_, e) =>
        {
            bool inside = e.Delta > 0 ? scroll.VerticalOffset > 0 : scroll.VerticalOffset < scroll.ScrollableHeight;
            if (inside) return;
            e.Handled = true;
            if (VisualTreeHelper.GetParent(scroll) is UIElement parent)
                parent.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = parent });
        };
        return new SmoothBorder
        {
            Background = Theme.P.Code,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = scroll,
        };
    }

    /// Ten lines of AskDetail before it scrolls.
    private const double AskDetailHeight = 10 * 19;

    private static SmoothBorder AskButton(string label, bool main, Action click)
    {
        var b = new SmoothBorder
        {
            Background = main ? Theme.P.Accent : Theme.P.Key,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 5, 12, 6),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = Theme.T(label, 12.5, main ? Theme.P.AccentInk : Theme.P.Text, Theme.Semi, false),
        };
        if (!main)
        {
            b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
            b.MouseLeave += (_, _) => b.Background = Theme.P.Key;
        }
        b.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    /// 복사 under a finished answer: the whole answer as written (Markdown source); "복사됨" for a moment.
    private static FrameworkElement CopyButton(string text)
    {
        var glyph = Theme.Glyph(G.Copy, 11, Theme.P.TextFaint);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.Margin = new Thickness(0, 1, 5, 0);
        var label = Theme.T(Tr("복사", "Copy"), 11.5, Theme.P.TextFaint, Theme.Medium, false);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(glyph);
        sp.Children.Add(label);
        var b = new SmoothBorder
        {
            Child = sp,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3, 9, 4),
            Margin = new Thickness(-7, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = Tr("답변 전체 복사 · Ctrl+Shift+C는 마지막 답변", "Copy full reply · Ctrl+Shift+C copies the last reply"),
        };
        b.MouseEnter += (_, _) => b.Background = Theme.P.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) =>
        {
            Launch.Copy(text);
            label.Text = Tr("복사 완료", "Copied");
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                label.Text = Tr("복사", "Copy");
            };
            t.Start();
            e.Handled = true;
        };
        return b;
    }

    public List<ActionItem> Actions(ChatThread? t)
    {
        var list = new List<ActionItem>
        {
            new() { Title = Tr("보내기", "Send"), Keys = "Enter", Glyph = G.Send, StayOpen = true, Run = owner.SendChat },
            new() { Title = Tr("파일 · 사진 첨부", "Attach files or images"), Keys = "Ctrl+O", Glyph = G.Paste, StayOpen = true, Run = owner.PickFiles },
            new() { Title = Tr("새 대화", "New chat"), Keys = "Ctrl+N", Glyph = G.Add, StayOpen = true, Run = () => owner.ShowChat(host.Chats.New()) },
            new() { Title = Tr("대화 목록", "Chat history"), Keys = "Ctrl+L", Glyph = G.History, StayOpen = true, Run = owner.ShowThreads },
            new() { Title = Tr("모델과 생각 수준 선택", "Select model and thinking effort"), Keys = "Ctrl+M", Glyph = G.Model, StayOpen = true, Run = owner.PickModel },
            new() { Title = Tr($"{AiModels.NextSideName(t?.Model ?? AiModels.DefaultModel())}로 바꾸기", $"Switch to {AiModels.NextSideName(t?.Model ?? AiModels.DefaultModel())}"), Keys = "Ctrl+Shift+M", Glyph = G.Chat, StayOpen = true, Run = owner.SwitchProvider },
            new() { Title = Tr("생각 수준 바꾸기", "Cycle thinking effort"), Keys = "Ctrl+E", Glyph = G.Bulb, StayOpen = true, Run = owner.CycleEffort },
        };
        if (t?.Messages.LastOrDefault(x => x.Role == "assistant" && x.Text.Length > 0) is { } last)
            list.Add(new ActionItem { Title = Tr("마지막 답변 복사", "Copy last reply"), Keys = "Ctrl+Shift+C", Glyph = G.Copy, StayOpen = true, Run = () => { Launch.Copy(last.Text); owner.ShowToast(Tr("답변 복사 완료", "Reply copied")); } });
        if (t?.Busy == true) list.Insert(0, new ActionItem { Title = Tr("답변 멈추기", "Stop generating"), Keys = "Ctrl+.", Glyph = G.Stop, StayOpen = true, Run = () => host.Chats.Stop(t) });
        if (t?.Messages.LastOrDefault()?.Asks.FirstOrDefault() is { } ask)
        {
            list.Insert(0, new ActionItem { Title = Tr($"거절: {ask.Says}", $"Deny: {ask.Says}"), Glyph = G.Close, StayOpen = true, Run = () => host.Chats.Answer(t, ask, false) });
            list.Insert(0, new ActionItem { Title = Tr($"허용: {ask.Says}", $"Allow: {ask.Says}"), Glyph = G.Shield, StayOpen = true, Run = () => host.Chats.Answer(t, ask, true) });
        }
        if (t is not null && host.Chats.IsLive(t))
            list.Add(new ActionItem { Title = Tr("세션 종료 · 새 대화 열기", "End session · Start new chat"), Glyph = G.Stop, StayOpen = true, Run = () => { host.Chats.EndSession(t); owner.ShowChat(host.Chats.New()); } });
        if (t is { Messages.Count: > 0 })
            list.Add(new ActionItem { Title = Tr("이 대화 삭제", "Delete this chat"), Glyph = G.Delete, Danger = true, StayOpen = true, Run = () => { host.DeleteThread(t); owner.ShowChat(host.Chats.New()); } });
        return list;
    }
}
