using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.Music;
using Bloom.Search;

namespace Bloom.Ui;

/// 미니 플레이어: the song that is on (its cover, title, artist and album), 무작위 · 이전 · 재생/일시정지 · 다음 · 반복, the
/// progress bar (a drag moves it when Bloom plays the file itself), the volume, and below the queue, Bloom's 재생목록 or
/// 다른 앱 (direct; another player keeps its own). When Bloom itself plays nothing, the top shows what another app plays
/// (Spotify, a browser: OtherApps) and its buttons steer that app. A view of MusicPlayer: closing it leaves the music on.
internal sealed class PlayerTool : ITool
{
    private readonly Grid view = new() { Focusable = true };
    private readonly TextBlock source, title, artist, note, clock, queueTitle, count;
    private readonly SeekBar progress, volume;
    private readonly SmoothBorder playButton, previousButton, nextButton, clearButton, shuffleButton, repeatButton, art;
    private readonly TextBlock playGlyph, shuffleGlyph, repeatGlyph, artGlyph;
    private readonly Grid volumeBox = new();
    private readonly TextBlock volumeGlyph;
    private readonly Segmented tabs;
    private readonly ScrollViewer scroll;
    private readonly StackPanel list = new() { Margin = new Thickness(6, 2, 6, 8) };
    private string listKey = "";

    // ── 재생목록 ──
    private readonly StackPanel shelf = new() { Margin = new Thickness(6, 2, 6, 8) };
    private readonly StackPanel shelfRows = new();
    private readonly FrameworkElement saveField;
    private readonly TextBox saveBox;
    private readonly TextBlock shelfNote;
    private int shelfCount;

    // ── 다른 앱 ──
    private readonly StackPanel others = new() { Margin = new Thickness(6, 2, 6, 8) };
    private string othersKey = "";

    /// Another app's song on top (Bloom plays nothing: NowPlaying.Shown), or null: Bloom's own.
    private MediaNow? shown;

    /// What the cover square shows now (set once per change).
    private ImageSource? cover;
    private bool coverSet;

    public string Key => "player";
    public string Name => Tr("미니 플레이어", "Mini Player");
    public char Glyph => PlayerGlyph.Music;
    public FrameworkElement View => view;
    public FrameworkElement? Head => clearButton;
    public string? State => null;

    private bool OnShelf => tabs.Value == "lists";

    private bool OnApps => tabs.Value == "apps";

    public PlayerTool()
    {
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // the song
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // progress
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons + volume
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // queue | playlists
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var song = new Grid { Margin = new Thickness(16, 12, 16, 6) };
        song.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        song.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        artGlyph = Theme.Glyph(PlayerGlyph.Music, 20, Theme.P.TextFaint);
        artGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        artGlyph.VerticalAlignment = VerticalAlignment.Center;
        art = new SmoothBorder { Width = 56, Height = 56, CornerRadius = new CornerRadius(8), Background = Theme.P.Tile, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top, Child = artGlyph };
        song.Children.Add(art);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        source = Theme.T("", 11, Theme.P.TextFaint, Theme.Medium);
        title = Theme.T("", 15, Theme.P.Text, Theme.Semi);
        artist = Theme.T("", 12.5, Theme.P.TextDim);
        artist.TextWrapping = TextWrapping.Wrap;
        artist.TextTrimming = TextTrimming.None;
        note = Theme.T("", 11.5, Theme.P.Danger);
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.None;
        words.Children.Add(source);
        words.Children.Add(title);
        words.Children.Add(artist);
        words.Children.Add(note);
        Grid.SetColumn(words, 1);
        song.Children.Add(words);
        view.Children.Add(song);

        var bar = new Grid { Margin = new Thickness(16, 2, 16, 0) };
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        progress = new SeekBar();
        progress.Chosen += SeekTo;
        clock = Theme.T("", 11, Theme.P.TextFaint);
        Grid.SetRow(clock, 1);
        bar.Children.Add(progress);
        bar.Children.Add(clock);
        Grid.SetRow(bar, 1);
        view.Children.Add(bar);

        var controls = new Grid { Margin = new Thickness(10, 4, 14, 6) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        shuffleButton = ToolUi.Button(PlayerGlyph.Shuffle, "", ToggleShuffle);
        shuffleGlyph = (TextBlock)shuffleButton.Child;
        buttons.Children.Add(shuffleButton);
        previousButton = ToolUi.Button(PlayerGlyph.Previous, Tr("이전 곡 (3초 넘게 들었으면 처음부터)", "Previous Track (restarts track if past 3s)"), Previous);
        buttons.Children.Add(previousButton);
        playButton = ToolUi.Button(PlayerGlyph.Play, Tr("재생 · 일시정지 (Space)", "Play · Pause (Space)"), TogglePlay);
        playButton.Width = playButton.Height = 34;
        playButton.CornerRadius = new CornerRadius(17);
        playButton.Margin = new Thickness(4, 0, 4, 0);
        playButton.Background = Theme.P.AccentSoft;
        playGlyph = (TextBlock)playButton.Child;
        playGlyph.FontSize = 15;
        playGlyph.Foreground = Theme.P.AccentText;
        playButton.MouseEnter += (_, _) => playButton.Background = Theme.P.Accent;
        playButton.MouseLeave += (_, _) => playButton.Background = Theme.P.AccentSoft;
        buttons.Children.Add(playButton);
        nextButton = ToolUi.Button(PlayerGlyph.Next, Tr("다음 곡", "Next Track"), Next);
        buttons.Children.Add(nextButton);
        repeatButton = ToolUi.Button(PlayerGlyph.RepeatAll, "", CycleRepeat);
        repeatGlyph = (TextBlock)repeatButton.Child;
        buttons.Children.Add(repeatButton);
        controls.Children.Add(buttons);

        volumeBox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volumeBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 110 });
        volumeGlyph = Theme.Glyph(PlayerGlyph.Volume, 12, Theme.P.TextDim);
        volumeGlyph.Margin = new Thickness(0, 0, 8, 0);
        volume = new SeekBar { VerticalAlignment = VerticalAlignment.Center };
        volume.Chosen += v =>
        {
            MusicPlayer.Volume = (int)Math.Round(v * 100);
            ShowVolume();
        };
        Grid.SetColumn(volume, 1);
        volumeBox.Children.Add(volumeGlyph);
        volumeBox.Children.Add(volume);
        volumeBox.HorizontalAlignment = HorizontalAlignment.Right;
        volumeBox.MinWidth = 96;
        volumeBox.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(volumeBox, 1);
        controls.Children.Add(volumeBox);
        Grid.SetRow(controls, 2);
        view.Children.Add(controls);

        var heading = new Grid { Margin = new Thickness(16, 6, 16, 2) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        queueTitle = Theme.T("", 11.5, Theme.P.TextFaint, Theme.Medium);
        tabs = new Segmented(new[] { ("queue", Tr("대기열", "Queue")), ("lists", Tr("재생목록", "Playlists")), ("apps", Tr("다른 앱", "Other Apps")) }, "queue") { HorizontalAlignment = HorizontalAlignment.Left };
        tabs.Picked += _ => ShowTab();
        count = Theme.T("", 11.5, Theme.P.TextFaint, null, false);
        count.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(count, 1);
        heading.Children.Add(queueTitle);
        heading.Children.Add(tabs);
        heading.Children.Add(count);
        Grid.SetRow(heading, 3);
        view.Children.Add(heading);
        scroll = ToolUi.Scroll(list);
        Grid.SetRow(scroll, 4);
        view.Children.Add(scroll);

        (saveField, saveBox) = ToolUi.Field(Tr("대기열을 저장할 이름", "Name to save queue as playlist"), 13, SaveList);
        saveField.Margin = new Thickness(10, 4, 10, 4);
        saveBox.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            SaveList();
        };
        shelfNote = Theme.T("", 11.5, Theme.P.TextDim);
        shelfNote.TextWrapping = TextWrapping.Wrap;
        shelfNote.TextTrimming = TextTrimming.None;
        shelfNote.Margin = new Thickness(16, 0, 16, 4);
        shelfNote.Visibility = Visibility.Collapsed;
        shelf.Children.Add(saveField);
        shelf.Children.Add(shelfNote);
        shelf.Children.Add(shelfRows);

        clearButton = ToolUi.Button(G.Delete, Tr("대기열 비우기 (지금 곡은 남겨요)", "Clear Queue (keeps current track)"), MusicPlayer.ClearRest);

        view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Space || e.OriginalSource is TextBox) return; // the playlist's name takes its spaces
            e.Handled = true;
            TogglePlay();
        };
        view.MouseDown += (_, _) => view.Focus();
        MusicPlayer.Changed += Refresh;
        NowPlaying.ArtCame += OwnArtCame;
        MusicPlayer.Watch(true);
        Refresh();
    }

    private void ShowVolume()
    {
        volume.Value = MusicPlayer.Volume / 100.0;
        volumeGlyph.Text = (MusicPlayer.Volume == 0 ? PlayerGlyph.Mute : PlayerGlyph.Volume).ToString();
        volume.ToolTip = Tr($"볼륨 {MusicPlayer.Volume}", $"Volume {MusicPlayer.Volume}");
    }

    /// Everything the view shows, from the player (or from another app when Bloom plays nothing).
    private void Refresh()
    {
        bool direct = !MusicPlayer.External;
        shown = NowPlaying.Shown;
        if (shown is { } other) ShowOther(other);
        else ShowOwn();
        ShowArt();
        ShowVolume();
        volumeBox.Visibility = direct && shown is null ? Visibility.Visible : Visibility.Collapsed;
        tabs.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
        queueTitle.Visibility = direct ? Visibility.Collapsed : Visibility.Visible;
        if (!direct && tabs.Value != "queue") tabs.Set("queue");
        clearButton.Visibility = direct && tabs.Value == "queue" ? Visibility.Visible : Visibility.Collapsed;
        saveField.Visibility = MusicPlayer.Queue.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        scroll.Content = OnShelf ? shelf : OnApps ? others : list;
        count.Text = !direct || OnApps ? "" : OnShelf ? Tr($"{shelfCount}개", $"{shelfCount}") : Tr($"{MusicPlayer.Queue.Items.Count}곡", $"{MusicPlayer.Queue.Items.Count} tracks");
        FillQueue();
        FillOthers();
    }

    /// Bloom's own song (or the other player's the settings chose).
    private void ShowOwn()
    {
        bool has = MusicPlayer.HasSong;
        source.Visibility = Visibility.Collapsed;
        title.Text = has ? MusicPlayer.Title : Tr("재생 중인 곡이 없습니다", "Nothing is playing");
        title.Foreground = has ? Theme.P.Text : Theme.P.TextDim;
        artist.Text = has ? Join(MusicPlayer.Artist, MusicPlayer.Album) : MusicPlayer.External ? Tr($"{PlayerWays.NameOf(MusicPlayer.Way)}에서 곡을 틀거나, 파일 검색 결과에서 Ctrl+K", $"Play music in {PlayerWays.NameOf(MusicPlayer.Way)}, or press Ctrl+K on a file search result") : Tr("파일 검색 결과의 오디오 파일에서 Ctrl+K › 미니 플레이어로 재생", "On an audio file in search results, press Ctrl+K › Play in Mini Player");
        artist.Visibility = artist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        note.Text = MusicPlayer.Note ?? "";
        note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        playGlyph.Text = (MusicPlayer.IsPlaying ? PlayerGlyph.Pause : PlayerGlyph.Play).ToString();
        Usable(previousButton, true);
        Usable(playButton, true);
        Usable(nextButton, true);

        var len = MusicPlayer.Length;
        var pos = MusicPlayer.Position;
        progress.IsEnabled = MusicPlayer.CanSeek;
        progress.Value = len > TimeSpan.Zero ? pos / len : 0;
        clock.Text = has ? MusicPlayer.Clock(pos) + (len > TimeSpan.Zero ? " / " + MusicPlayer.Clock(len) : "") : "";
        bool direct = !MusicPlayer.External;
        volume.IsEnabled = direct;
        ShowModes(direct, MusicPlayer.Shuffle, MusicPlayer.Repeat);
    }

    /// Another app's song: where it plays, its title, artist and album, its buttons as far as it takes them.
    private void ShowOther(MediaNow other)
    {
        source.Text = Tr($"{OtherApps.NameOf(other.App)}에서 재생", $"Playing in {OtherApps.NameOf(other.App)}");
        source.Visibility = Visibility.Visible;
        title.Text = other.Title;
        title.Foreground = Theme.P.Text;
        artist.Text = Join(other.Artist, other.Album);
        artist.Visibility = artist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        note.Visibility = Visibility.Collapsed;
        playGlyph.Text = (other.Status == MediaStatus.Playing ? PlayerGlyph.Pause : PlayerGlyph.Play).ToString();
        Usable(previousButton, other.CanPrevious);
        Usable(playButton, other.CanToggle);
        Usable(nextButton, other.CanNext);

        var len = other.Length;
        var pos = OtherApps.PositionOf(other, DateTime.UtcNow);
        progress.IsEnabled = other.CanSeek && len > TimeSpan.Zero;
        progress.Value = len > TimeSpan.Zero ? pos / len : 0;
        clock.Text = len > TimeSpan.Zero ? MusicPlayer.Clock(pos) + " / " + MusicPlayer.Clock(len) : "";
        ShowModes(true, other.CanShuffle ? other.Shuffle : null, other.CanRepeat ? other.Repeat : null);
    }

    /// A button that works lit, one the app does not take faded.
    private static void Usable(UIElement button, bool on)
    {
        button.IsEnabled = on;
        button.Opacity = on ? 1 : 0.35;
    }

    private static string Join(string a, string b) => a.Length > 0 && b.Length > 0 ? a + " · " + b : a + b;

    /// 무작위 and 반복: lit (accent) when on, the tooltip says what a click does; hidden where they don't work (another
    /// player chosen, or an app that does not take them: null).
    private void ShowModes(bool show, bool? shuffle, Repeat? repeat)
    {
        shuffleButton.Visibility = show && shuffle is not null ? Visibility.Visible : Visibility.Collapsed;
        repeatButton.Visibility = show && repeat is not null ? Visibility.Visible : Visibility.Collapsed;
        bool on = shuffle == true;
        shuffleGlyph.Foreground = on ? Theme.P.Accent : Theme.P.TextDim;
        shuffleButton.ToolTip = on ? Tr("무작위 재생 켜짐 · 누르면 끔", "Shuffle On · Click to turn off") : Tr("무작위 재생 꺼짐 · 누르면 켬", "Shuffle Off · Click to turn on");
        var r = repeat ?? Repeat.Off;
        repeatGlyph.Text = (r == Repeat.One ? PlayerGlyph.RepeatOne : PlayerGlyph.RepeatAll).ToString();
        repeatGlyph.Foreground = r == Repeat.Off ? Theme.P.TextDim : Theme.P.Accent;
        repeatButton.ToolTip = r switch
        {
            Repeat.All => Tr("전체 반복 · 누르면 한 곡 반복", "Repeat All · Click to repeat one"),
            Repeat.One => Tr("한 곡 반복 · 누르면 반복 끔", "Repeat One · Click to turn off repeat"),
            _ => Tr("반복 꺼짐 · 누르면 전체 반복", "Repeat Off · Click to repeat all"),
        };
    }

    // ── the buttons: Bloom's own song, or the app's on top ──────────────────────────────────────
    private void TogglePlay() => NowPlaying.Press(shown, PlayerOp.Toggle);

    private void Previous() => NowPlaying.Press(shown, PlayerOp.Previous);

    private void Next() => NowPlaying.Press(shown, PlayerOp.Next);

    private void SeekTo(double fraction)
    {
        if (shown is { } other) OtherApps.Seek(other, fraction);
        else MusicPlayer.Seek(fraction);
    }

    private void ToggleShuffle()
    {
        if (shown is { } other) OtherApps.ToggleShuffle(other);
        else MusicPlayer.Shuffle = !MusicPlayer.Shuffle;
    }

    private void CycleRepeat()
    {
        if (shown is { } other) OtherApps.CycleRepeat(other);
        else MusicPlayer.CycleRepeat();
    }

    /// The cover: Bloom's song's (asked for when the song changes: NowPlaying), or the app's on top; a music note on a
    /// tile until it comes or without one.
    private void ShowArt() => Cover(NowPlaying.ArtOf(shown));

    private void OwnArtCame()
    {
        if (shown is null) Cover(NowPlaying.ArtOf(null));
    }

    private void Cover(ImageSource? img)
    {
        if (coverSet && ReferenceEquals(img, cover)) return;
        cover = img;
        coverSet = true;
        art.Background = img is null ? Theme.P.Tile : new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        artGlyph.Visibility = img is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// 다른 앱: every app that tells Windows a song, with its picture, its name and its buttons. Rebuilt when one changes.
    private void FillOthers()
    {
        var all = OtherApps.Sessions;
        string key = string.Join("\n", all.Select(s => $"{s.App}|{s.Title}|{s.Artist}|{s.Status}|{s.CanPrevious}{s.CanToggle}{s.CanNext}|{OtherApps.ArtOf(s) is not null}"));
        if (key == othersKey) return;
        othersKey = key;
        others.Children.Clear();
        if (all.Count == 0)
        {
            others.Children.Add(ToolUi.Empty(Tr("재생 중인 다른 앱 없음 · Spotify나 브라우저에서 재생하면 여기에 나옵니다", "No other apps playing · Play audio in Spotify or a browser to see it here")));
            return;
        }
        foreach (var s in all)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var img = OtherApps.ArtOf(s);
            var glyph = Theme.Glyph(PlayerGlyph.Music, 13, Theme.P.TextFaint);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(new SmoothBorder
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 10, 0),
                Background = img is null ? Theme.P.Tile : new ImageBrush(img) { Stretch = Stretch.UniformToFill },
                Child = img is null ? glyph : null,
            });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Theme.T(s.Title, 13, Theme.P.Text));
            text.Children.Add(Theme.T(Join(OtherApps.NameOf(s.App), s.Artist), 11, Theme.P.TextFaint));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var buttons = new (char glyph, string tip, PlayerOp op, bool on)[]
            {
                (PlayerGlyph.Previous, Tr("이전 곡", "Previous Track"), PlayerOp.Previous, s.CanPrevious),
                (s.Status == MediaStatus.Playing ? PlayerGlyph.Pause : PlayerGlyph.Play, Tr("재생 · 일시정지", "Play · Pause"), PlayerOp.Toggle, s.CanToggle),
                (PlayerGlyph.Next, Tr("다음 곡", "Next Track"), PlayerOp.Next, s.CanNext),
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                var (g, tip, op, on) = buttons[i];
                var b = ToolUi.Button(g, tip, () => OtherApps.Press(s, op));
                b.Width = b.Height = 24;
                Usable(b, on);
                Grid.SetColumn(b, 2 + i);
                row.Children.Add(b);
            }
            others.Children.Add(ToolUi.Row(row, null));
        }
    }

    private void ShowTab()
    {
        if (OnShelf) FillShelf();
        Refresh();
    }

    /// A tab ("queue", "lists", "apps"), as a click on it would (QA).
    public void ShowTab(string tab)
    {
        tabs.Set(tab);
        ShowTab();
    }

    /// The queue (direct): the songs, the one on marked; a click plays it, ✕ takes it out. Only rebuilt when it changed.
    private void FillQueue()
    {
        var q = MusicPlayer.Queue;
        bool direct = !MusicPlayer.External;
        string key = direct ? q.Index + "|" + string.Join("\n", q.Items.ConvertAll(t => t.Path + "|" + t.Title + "|" + t.Artist)) : MusicPlayer.Way;
        if (key == listKey) return;
        listKey = key;
        list.Children.Clear();
        if (!direct)
        {
            queueTitle.Text = Tr("대기열", "Queue");
            string name = PlayerWays.NameOf(MusicPlayer.Way);
            list.Children.Add(ToolUi.Empty(Tr($"{name}에서 재생해요 — 대기열은 {name}에서 보세요", $"Playing in {name} — View queue in {name}")));
            return;
        }
        if (q.Items.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(Tr("비어 있어요 — 오디오 파일에서 Ctrl+K › 대기열에 추가", "Queue is empty — On an audio file, press Ctrl+K › Add to Queue")));
            return;
        }
        for (int i = 0; i < q.Items.Count; i++)
        {
            int at = i;
            var t = q.Items[i];
            bool on = i == q.Index;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mark = on ? Theme.Glyph(PlayerGlyph.Play, 9, Theme.P.Accent) : (UIElement)Theme.T((i + 1).ToString(), 10.5, Theme.P.TextFaint, null, false);
            row.Children.Add(mark);
            var text = new StackPanel();
            text.Children.Add(Theme.T(t.Title, 13, on ? Theme.P.AccentText : Theme.P.Text, on ? Theme.Semi : Theme.Regular));
            if (t.Artist.Length > 0) text.Children.Add(Theme.T(t.Artist, 11, Theme.P.TextFaint));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (!on)
            {
                var remove = ToolUi.Button(G.Close, Tr("대기열에서 빼기", "Remove from Queue"), () => MusicPlayer.Remove(at));
                remove.Width = remove.Height = 22;
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
            }
            list.Children.Add(ToolUi.Row(row, () => MusicPlayer.PlayAt(at)));
        }
    }

    // ── 재생목록 ────────────────────────────────────────────────────────────────────────────────
    /// Bloom's playlists: a click plays one in place of the queue, ＋ adds it after the queue, the bin sends it to the
    /// Recycle Bin. Built when the tab opens and after a change (the name being typed stays).
    private void FillShelf()
    {
        shelfRows.Children.Clear();
        var all = Playlists.All();
        shelfCount = all.Count;
        if (all.Count == 0)
        {
            shelfRows.Children.Add(ToolUi.Empty(Tr("저장한 재생목록 없음 · 대기열에 곡을 넣고 이름을 붙여 저장", "No saved playlists · Add tracks to the queue and save with a name")));
            return;
        }
        foreach (var saved in all)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = Theme.Glyph(PlayerGlyph.List, 11, Theme.P.TextFaint);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(glyph);
            var text = new StackPanel();
            text.Children.Add(Theme.T(saved.Name, 13, Theme.P.Text));
            text.Children.Add(Theme.T(Tr($"{saved.Count}곡", $"{saved.Count} tracks"), 11, Theme.P.TextFaint));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var add = ToolUi.Button(G.Add, Tr("대기열에 추가", "Add to Queue"), () => AddList(saved));
            var delete = ToolUi.Button(G.Delete, Tr("삭제 (휴지통으로)", "Delete (Move to Recycle Bin)"), () => DeleteList(saved));
            add.Width = add.Height = delete.Width = delete.Height = 22;
            Grid.SetColumn(add, 2);
            Grid.SetColumn(delete, 3);
            row.Children.Add(add);
            row.Children.Add(delete);
            row.ToolTip = saved.Path;
            shelfRows.Children.Add(ToolUi.Row(row, () => PlayList(saved)));
        }
    }

    private void Tell(string text)
    {
        shelfNote.Text = text;
        shelfNote.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveList()
    {
        string name = saveBox.Text.Trim();
        if (Playlists.FileName(name) is not { } file)
        {
            Tell(Tr("이름 필요", "Name required"));
            return;
        }
        bool replaced = File.Exists(Path.Combine(Playlists.Folder, file + ".m3u8"));
        int songs = MusicPlayer.Queue.Items.Count;
        if (Playlists.Save(name, MusicPlayer.Queue.Items) is null)
        {
            Tell(Tr("저장 실패 · 재생목록 폴더에 쓸 수 없음", "Save failed · Cannot write to playlists folder"));
            return;
        }
        saveBox.Clear();
        Tell(replaced ? Tr($"‘{file}’ 바꿔 저장 · {songs}곡", $"Overwrote ‘{file}’ · {songs} tracks") : Tr($"‘{file}’ 저장 · {songs}곡", $"Saved ‘{file}’ · {songs} tracks"));
        FillShelf();
        Refresh();
    }

    private void PlayList(Playlists.Saved saved)
    {
        Tell("");
        if (!MusicPlayer.OpenPlaylist(saved.Path)) return;
        tabs.Set("queue");
        ShowTab();
    }

    private void AddList(Playlists.Saved saved)
    {
        int before = MusicPlayer.Queue.Items.Count;
        if (MusicPlayer.OpenPlaylist(saved.Path, true)) Tell(Tr($"‘{saved.Name}’ {MusicPlayer.Queue.Items.Count - before}곡 대기열에 추가", $"Added {MusicPlayer.Queue.Items.Count - before} tracks from ‘{saved.Name}’ to queue"));
    }

    private void DeleteList(Playlists.Saved saved)
    {
        Tell(Playlists.Delete(saved) ? Tr($"‘{saved.Name}’ 휴지통으로 이동", $"Moved ‘{saved.Name}’ to Recycle Bin") : Tr("삭제 실패", "Delete failed"));
        FillShelf();
        Refresh();
    }

    public void FocusFirst() => view.Dispatcher.BeginInvoke(() => view.Focus(), System.Windows.Threading.DispatcherPriority.Input);

    public void Closed()
    {
        MusicPlayer.Changed -= Refresh;
        NowPlaying.ArtCame -= OwnArtCame;
        MusicPlayer.Watch(false);
    }
}
