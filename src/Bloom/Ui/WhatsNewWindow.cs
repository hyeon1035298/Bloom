using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 바뀐 점: the first start after an update (ReleaseNotes.JustUpdated) and 설정 › 정보 show a version's notes from
/// GitHub, selectable like a chat answer (Markdown → ReadText); the list at the top picks another version, whose notes
/// come when it is picked.
/// 확인 (Enter / Esc) closes it; GitHub에서 보기 opens the release page. It doesn't take the focus when it comes up on its
/// own. The notes' pictures (![alt](address)) come from their GitHub address while their alt text holds the place, and
/// stay that text when they can't be had. A picture's link to bloom://settings/<page> opens that settings page.
internal sealed class WhatsNewWindow : Window
{
    private const string SettingsLink = "bloom://settings/";

    private readonly string current;
    private readonly bool listed; // the version list came (else the window says the notes couldn't be had)
    private readonly Action<string>? openSettings;
    private readonly TextBlock heading, sub;
    private readonly ScrollViewer scroll;
    private CancellationTokenSource? shown; // the shown version's notes and pictures on their way: cut off by another version or closing

    /// `versions`: ReleaseNotes.Versions' list, null when it couldn't be had (the window says so).
    public WhatsNewWindow(string current, List<string>? versions, Action<string>? openSettings = null)
    {
        this.current = current;
        listed = versions is not null;
        versions ??= new();
        this.openSettings = openSettings;
        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Theme.P.Solid;
        Shots.TrackIcon(this);

        var stack = new StackPanel { Margin = new Thickness(32, 26, 32, 20) };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading = Theme.T("", 20, Theme.P.Text, Theme.Semi);
        top.Children.Add(heading);
        string first = versions.Contains(current) || versions.Count == 0 ? current : versions[0];
        Dropdown? pick = null;
        if (versions.Count > 1)
        {
            pick = new Dropdown(versions.Select(v => (v, v == current ? Tr($"{v} · 설치됨", $"{v} · installed") : v, (string?)null)).ToArray(), first) { Margin = new Thickness(16, 0, 0, 0) };
            pick.Picked += v => _ = Show(v);
            Grid.SetColumn(pick, 1);
            top.Children.Add(pick);
        }
        stack.Children.Add(top);
        sub = Theme.T(Tr("이번 버전의 변경 사항", "Changes in this version"), 13, Theme.P.TextFaint);
        sub.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(sub);
        scroll = new ScrollViewer
        {
            MaxHeight = 420,
            Margin = new Thickness(0, 16, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Template = Launcher.ThinScroll(),
        };
        stack.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(ActionButton.Make(Tr("GitHub에서 보기", "See it on GitHub"), false, () => Launch.Open(Updates.Page)));
        buttons.Children.Add(ActionButton.Make(Tr("확인", "OK"), true, Close));
        stack.Children.Add(buttons);
        Content = new Border { Background = Theme.P.Solid, Child = stack }; // one surface with the title bar (SolidCaption)
        _ = Show(first);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape) || pick is { IsOpen: true }) return; // an open list takes its keys
            Close();
            e.Handled = true;
        };
        SourceInitialized += (_, _) => Shots.SolidCaption(this);
        Closed += (_, _) => shown?.Cancel();
    }

    /// QA: another version's notes, as the list at the top picks them.
    public Task ShowForQa(string version) => Show(version);

    /// `version`'s notes in place of the shown ones, fetched now (or this run's copy).
    private async Task Show(string version)
    {
        shown?.Cancel();
        shown?.Dispose();
        shown = new CancellationTokenSource();
        var cut = shown.Token;
        bool mine = version == current;
        Title = Tr($"Bloom {version} · 바뀐 점", $"Bloom {version} · What's new");
        heading.Text = mine ? Tr($"Bloom {version} 업데이트 완료", $"Bloom {version} update complete") : "Bloom " + version;
        sub.Visibility = mine ? Visibility.Visible : Visibility.Collapsed;
        string? notes = null;
        if (listed)
        {
            scroll.Content = Theme.T(Tr("불러오는 중…", "Loading…"), 13, Theme.P.TextFaint);
            notes = await ReleaseNotes.For(version) ?? Tr("이번 버전의 변경 기록 없음", "No release notes for this version");
            if (cut.IsCancellationRequested) return; // another version was picked meanwhile, or the window closed
        }
        notes ??= Tr("변경 기록을 불러오지 못했습니다. 인터넷 연결을 확인하거나 GitHub에서 보세요.", "Couldn't load the release notes. Check the connection, or see them on GitHub.");
        scroll.Content = Markdown.Render(notes, Open, picture: span => Picture(span, cut));
        scroll.ScrollToTop();
    }

    private void Open(string link)
    {
        if (!link.StartsWith(SettingsLink, StringComparison.OrdinalIgnoreCase)) Launch.Open(link);
        else openSettings?.Invoke(link[SettingsLink.Length..]);
    }

    /// The picture `span` names: its alt text in its place until it has come from its https address (it stays that
    /// text when it can't be had or read). Null for a picture with no such address (the notes show its alt text).
    private FrameworkElement? Picture(MdSpan span, CancellationToken cut)
    {
        if (!Uri.TryCreate(span.Source, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps) return null;
        var alt = Theme.T(span.Text, 12.5, Theme.P.TextFaint, Theme.Regular, false);
        alt.TextWrapping = TextWrapping.Wrap;
        var place = new Border { Child = alt };
        _ = Load(place, url, span, cut);
        return place;
    }

    private async Task Load(Border place, Uri url, MdSpan span, CancellationToken cut)
    {
        byte[] bytes;
        try
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cut);
            wait.CancelAfter(ReleaseNotes.Wait);
            bytes = await Ai.ApiClient.Shared.GetByteArrayAsync(url, wait.Token).ConfigureAwait(true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            if (!cut.IsCancellationRequested) Log.Write("whatsnew picture: " + e.Message);
            return;
        }
        if (cut.IsCancellationRequested) return;
        AnimatedPicture picture;
        try
        {
            picture = new AnimatedPicture(bytes) { ToolTip = span.Text.Length > 0 ? span.Text : null };
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            Log.Error("whatsnew picture", e);
            return;
        }
        if (span.Link is { } link)
        {
            picture.Cursor = Cursors.Hand;
            picture.MouseLeftButtonUp += (_, e) =>
            {
                Open(link.Url);
                e.Handled = true;
            };
        }
        place.Child = picture;
    }
}
