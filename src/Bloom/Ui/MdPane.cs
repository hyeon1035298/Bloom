using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// A Markdown text to write in and to read (메모, 문서): the editor or the drawn view (MdView), a 보기 / 편집 button
/// for the host's head, checklist boxes that tick in the source, and Ctrl+wheel (Ctrl+0: back to 14) for the text size
/// every pane shares (Settings.MdTextSize), shown for a moment as "글자 크기 n". It keeps nothing itself: its owner
/// holds the text and saves it — Edited hands every change over (typing, a box ticked), Text brings the owner's
/// changes from elsewhere back in.
internal sealed class MdPane : Grid, IComposerHost
{
    /// Longer than this (characters) the text is not drawn: it stays in the editor.
    public const int MaxView = 2_000_000;

    /// The shared size moved: every pane on screen follows.
    private static event Action? Resized;

    private readonly TextBox editor;
    private readonly MdView view;
    private readonly SmoothBorder toggle, badge;
    private readonly TextBlock toggleGlyph, badgeText;
    private readonly DispatcherTimer redraw = new() { Interval = TimeSpan.FromMilliseconds(120) }, hide = new();
    private readonly string? baseDir;
    private readonly bool readOnly, document;
    private string? rendered; // the text the view shows, and at what size
    private double renderedSize;
    private bool inView, quiet;
    private int wheel; // a fine wheel's turns, until they make a step

    /// The text as it is now, typed or ticked; the owner keeps it (and saves it).
    public event Action<string>? Edited;

    /// 보기 ↔ 편집.
    public event Action? ModeChanged;

    /// Where a link clicked in the view goes (the host decides).
    public Action<string> OpenLink { get; set; } = _ => { };

    /// The 보기 / 편집 button, for the host to put in its head.
    public FrameworkElement ToggleButton => toggle;

    /// Where the keyboard goes: the editor, or the drawn text in 보기 (scrolling with the keys, selecting, Ctrl+C).
    public UIElement KeyTarget => inView ? view : editor;

    /// The text size every pane uses now.
    public static int TextSize => Math.Clamp(Settings.Current.MdTextSize, Settings.MdTextMin, Settings.MdTextMax);

    /// `startInView`: drawn first (a file opened to read), else the editor first with the caret at the end (a note).
    /// `baseDir`: where pictures' relative paths start. `readOnly`: nothing can be typed and the boxes take no clicks.
    public MdPane(string text, bool startInView, string? baseDir, bool readOnly, bool document)
    {
        this.baseDir = baseDir;
        this.readOnly = readOnly;
        this.document = document;
        editor = new TextBox
        {
            FontFamily = Theme.Sans,
            FontSize = TextSize,
            Foreground = Theme.P.Text,
            CaretBrush = Theme.P.Accent,
            SelectionBrush = Theme.P.Selection,
            SelectionOpacity = 1,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true,
            AcceptsTab = true,
            IsReadOnly = readOnly,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14, 12, 12, 8),
            Text = text,
        };
        editor.CaretIndex = text.Length;
        editor.TextChanged += (_, _) =>
        {
            if (!quiet) Edited?.Invoke(editor.Text);
        };
        Children.Add(editor);
        view = new MdView { Visibility = Visibility.Collapsed, OpenLink = url => OpenLink(url), Toggle = readOnly ? null : Toggle };
        Children.Add(view);

        // "글자 크기 16" for a moment, low in the middle
        badgeText = Theme.T("", 12, Theme.P.Text, Theme.Medium);
        badge = new SmoothBorder
        {
            Child = badgeText,
            Background = Theme.P.Popup,
            BorderBrush = Theme.P.PopupLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 5, 11, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 14),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(badge);
        hide.Tick += (_, _) =>
        {
            hide.Stop();
            badge.Visibility = Visibility.Collapsed;
        };
        redraw.Tick += (_, _) =>
        {
            redraw.Stop();
            if (inView) Render(null);
        };

        toggle = ToolUi.Button(G.View, "", () => InView = !InView);
        toggleGlyph = (TextBlock)toggle.Child;
        SyncToggle();

        PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            e.Handled = true; // not the editor's or the view's scrolling
            wheel += e.Delta;
            int steps = wheel / Mouse.MouseWheelDeltaForOneLine;
            if (steps == 0) return;
            wheel -= steps * Mouse.MouseWheelDeltaForOneLine;
            SetTextSize(TextSize + steps);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || e.Key is not (Key.D0 or Key.NumPad0)) return;
            e.Handled = true;
            SetTextSize(Settings.MdTextDefault);
        };
        Loaded += (_, _) =>
        {
            Resized -= Follow;
            Resized += Follow;
            Follow();
        };
        Unloaded += (_, _) => Resized -= Follow;

        if (startInView) InView = true;
    }

    /// The text now. Set: the owner's text changed elsewhere (another program, the AI): shown in its place, the scroll
    /// and the caret kept; Edited is not raised for it.
    public string Text
    {
        get => editor.Text;
        set
        {
            if (value == editor.Text) return;
            double at = Fraction();
            Put(value);
            if (!inView) Restore(at);
            else if (value.Length > MaxView) Switch(false, at);
            else Render(null);
        }
    }

    /// 보기 (the Markdown drawn) or 편집. A text over MaxView stays in 편집, with a word why.
    public bool InView
    {
        get => inView;
        set
        {
            if (value == inView) return;
            if (value && editor.Text.Length > MaxView)
            {
                Flash(Tr("너무 긴 글 · 편집 화면으로만 표시", "Text too long · Shown in editor view only"), 2500);
                return;
            }
            Switch(value, Fraction());
        }
    }

    /// The size for every pane (clamped to Settings' range), saved; shown here for a moment either way.
    public void SetTextSize(int size)
    {
        int n = Math.Clamp(size, Settings.MdTextMin, Settings.MdTextMax);
        if (n != Settings.Current.MdTextSize)
        {
            Settings.Current.MdTextSize = n;
            Settings.Save();
            Resized?.Invoke();
        }
        Flash(Tr($"글자 크기 {n}", $"Font size {n}"), 1200);
    }

    // ReadText in the view: there is no composer to give the keyboard back to, it stays in the view (selecting, Ctrl+C)
    void IComposerHost.FocusComposer()
    {
    }

    void IComposerHost.ShowToast(string text) => Flash(text, 1300);

    private void Switch(bool toView, double at)
    {
        inView = toView;
        editor.Visibility = toView ? Visibility.Collapsed : Visibility.Visible;
        view.Visibility = toView ? Visibility.Visible : Visibility.Collapsed;
        if (toView && (rendered is null || rendered != editor.Text || renderedSize != TextSize)) Render(at);
        else Restore(at);
        SyncToggle();
        if (Window.GetWindow(this) is { IsActive: true }) Dispatcher.BeginInvoke(() => KeyTarget.Focus(), DispatcherPriority.Loaded);
        ModeChanged?.Invoke();
    }

    private void SyncToggle()
    {
        toggleGlyph.Text = (inView ? G.Pen : G.View).ToString();
        toggle.ToolTip = inView ? Tr("편집", "Edit") : Tr("보기 (마크다운)", "View (Markdown)");
    }

    /// The view drawn from the text now: coming from the editor, scrolled to the same share of its height (`at`);
    /// drawn again in place (null: another size, a change from outside, a tick), at the same block and pixels into it.
    private void Render(double? at)
    {
        var place = view.Place();
        rendered = editor.Text;
        renderedSize = TextSize;
        view.Show(rendered, renderedSize, document, baseDir);
        if (at is { } share) Restore(share);
        else Dispatcher.BeginInvoke(() => view.GoTo(place), DispatcherPriority.Loaded);
    }

    /// A checklist box clicked in the view: the owner's text with that line's box flipped, if the line is still the
    /// one drawn; otherwise the click is dropped and the view drawn again from the text now.
    private bool Toggle(int line)
    {
        string now = editor.Text;
        string? next = rendered is not null && MarkdownText.Line(rendered, line) is { } shown ? MarkdownText.ToggleTask(now, line, shown) : null;
        if (next is null)
        {
            Redraw();
            return false;
        }
        bool current = now == rendered;
        Put(next);
        Edited?.Invoke(next);
        if (current) rendered = next; // only the box changed: the view already shows it
        else Redraw();
        return true;
    }

    /// Drawn again after the click that asked for it is done with.
    private void Redraw() => Dispatcher.BeginInvoke(() =>
    {
        if (inView) Render(null);
    });

    /// The editor's text replaced without an Edited, the caret where it was.
    private void Put(string text)
    {
        int caret = editor.CaretIndex;
        quiet = true;
        try
        {
            editor.Text = text;
        }
        finally
        {
            quiet = false;
        }
        editor.CaretIndex = Math.Min(caret, text.Length);
    }

    /// How far down the shown side is scrolled, as a share of how far it can go.
    private double Fraction()
    {
        double max = inView ? view.ScrollableHeight : editor.ExtentHeight - editor.ViewportHeight;
        return max > 0 ? (inView ? view.VerticalOffset : editor.VerticalOffset) / max : 0;
    }

    /// The shown side scrolled to `at` once it is laid out.
    private void Restore(double at)
    {
        bool toView = inView;
        Dispatcher.BeginInvoke(() =>
        {
            if (toView) view.ScrollTo(at * view.ScrollableHeight);
            else editor.ScrollToVerticalOffset(at * Math.Max(0, editor.ExtentHeight - editor.ViewportHeight));
        }, DispatcherPriority.Loaded);
    }

    /// The shared size moved (here or in another pane): the editor at once, the view a moment after the last step.
    private void Follow()
    {
        editor.FontSize = TextSize;
        if (!inView || renderedSize == TextSize) return;
        redraw.Stop();
        redraw.Start();
    }

    /// QA: the view scrolled to its end.
    internal void ScrollToEndForQa() => view.ScrollToEnd();

    /// QA (MdBench): the first checklist box clicked; false: none took it.
    internal bool TickForQa() => view.TickForQa();

    private void Flash(string text, int ms)
    {
        badgeText.Text = text;
        badge.Visibility = Visibility.Visible;
        hide.Stop();
        hide.Interval = TimeSpan.FromMilliseconds(ms);
        hide.Start();
    }
}
