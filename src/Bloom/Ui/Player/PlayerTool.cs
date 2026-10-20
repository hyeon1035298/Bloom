using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Music;
using Bloom.Search;

namespace Bloom.Ui;

/// 미니 플레이어: the song that is on (title, artist), 이전 · 재생/일시정지 · 다음, the progress bar (a drag moves it when
/// Bloom plays the file itself), the volume, and the queue below (Bloom's own; another player keeps its own). A view of
/// MusicPlayer: closing it leaves the music on.
internal sealed class PlayerTool : ITool
{
    private readonly Grid view = new() { Focusable = true };
    private readonly TextBlock title, artist, note, clock, queueTitle;
    private readonly SeekBar progress, volume;
    private readonly SmoothBorder playButton, clearButton;
    private readonly TextBlock playGlyph;
    private readonly Grid volumeBox = new();
    private readonly TextBlock volumeGlyph;
    private readonly StackPanel list = new() { Margin = new Thickness(6, 2, 6, 8) };
    private string listKey = "";

    public string Key => "player";
    public string Name => Tr("미니 플레이어", "Mini player");
    public char Glyph => PlayerGlyph.Music;
    public FrameworkElement View => view;
    public FrameworkElement? Head => clearButton;
    public string? State => null;

    public PlayerTool()
    {
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // the song
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // progress
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons + volume
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // queue heading
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var song = new StackPanel { Margin = new Thickness(16, 12, 16, 6) };
        title = Theme.T("", 15, Theme.P.Text, Theme.Semi);
        artist = Theme.T("", 12.5, Theme.P.TextDim);
        artist.TextWrapping = TextWrapping.Wrap;
        artist.TextTrimming = TextTrimming.None;
        note = Theme.T("", 11.5, Theme.P.Danger);
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.None;
        song.Children.Add(title);
        song.Children.Add(artist);
        song.Children.Add(note);
        view.Children.Add(song);

        var bar = new Grid { Margin = new Thickness(16, 2, 16, 0) };
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        progress = new SeekBar();
        progress.Chosen += MusicPlayer.Seek;
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
        buttons.Children.Add(ToolUi.Button(PlayerGlyph.Previous, Tr("이전 곡 (3초 넘게 들었으면 처음부터)", "Previous (from the start after 3 s)"), MusicPlayer.Previous));
        playButton = ToolUi.Button(PlayerGlyph.Play, Tr("재생 · 일시정지 (Space)", "Play · pause (Space)"), MusicPlayer.Toggle);
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
        buttons.Children.Add(ToolUi.Button(PlayerGlyph.Next, Tr("다음 곡", "Next song"), MusicPlayer.Next));
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
        volumeBox.MinWidth = 130;
        Grid.SetColumn(volumeBox, 1);
        controls.Children.Add(volumeBox);
        Grid.SetRow(controls, 2);
        view.Children.Add(controls);

        queueTitle = Theme.T("", 11.5, Theme.P.TextFaint, Theme.Medium);
        queueTitle.Margin = new Thickness(16, 6, 16, 2);
        Grid.SetRow(queueTitle, 3);
        view.Children.Add(queueTitle);
        var scroll = ToolUi.Scroll(list);
        Grid.SetRow(scroll, 4);
        view.Children.Add(scroll);

        clearButton = ToolUi.Button(G.Delete, Tr("대기열 비우기 (지금 곡은 남겨요)", "Clear the queue (the song on stays)"), MusicPlayer.ClearRest);

        view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Space) return;
            e.Handled = true;
            MusicPlayer.Toggle();
        };
        view.MouseDown += (_, _) => view.Focus();
        MusicPlayer.Changed += Refresh;
        MusicPlayer.Watch(true);
        Refresh();
    }

    private void ShowVolume()
    {
        volume.Value = MusicPlayer.Volume / 100.0;
        volumeGlyph.Text = (MusicPlayer.Volume == 0 ? PlayerGlyph.Mute : PlayerGlyph.Volume).ToString();
        volume.ToolTip = Tr($"볼륨 {MusicPlayer.Volume}", $"Volume {MusicPlayer.Volume}");
    }

    /// Everything the view shows, from the player.
    private void Refresh()
    {
        bool has = MusicPlayer.HasSong;
        title.Text = has ? MusicPlayer.Title : Tr("재생 중인 곡이 없어요", "Nothing is playing");
        title.Foreground = has ? Theme.P.Text : Theme.P.TextDim;
        artist.Text = has ? MusicPlayer.Artist : MusicPlayer.External ? Tr($"{PlayerWays.NameOf(MusicPlayer.Way)}에서 곡을 틀거나, 파일 검색 결과에서 Ctrl+K", $"Play a song in {PlayerWays.NameOf(MusicPlayer.Way)}, or Ctrl+K on a file you searched") : Tr("파일 검색 결과의 오디오 파일에서 Ctrl+K › 미니 플레이어로 재생", "Ctrl+K on an audio file in the search results › Play in mini player");
        artist.Visibility = artist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        note.Text = MusicPlayer.Note ?? "";
        note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        playGlyph.Text = (MusicPlayer.IsPlaying ? PlayerGlyph.Pause : PlayerGlyph.Play).ToString();

        var len = MusicPlayer.Length;
        var pos = MusicPlayer.Position;
        progress.IsEnabled = MusicPlayer.CanSeek;
        progress.Value = len > TimeSpan.Zero ? pos / len : 0;
        clock.Text = has ? MusicPlayer.Clock(pos) + (len > TimeSpan.Zero ? " / " + MusicPlayer.Clock(len) : "") : "";

        bool direct = !MusicPlayer.External;
        volume.IsEnabled = direct;
        volumeBox.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
        clearButton.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
        ShowVolume();
        FillQueue();
    }

    /// The queue (direct): the songs, the one on marked; a click plays it, ✕ takes it out. Only rebuilt when it changed.
    private void FillQueue()
    {
        var q = MusicPlayer.Queue;
        bool direct = !MusicPlayer.External;
        string key = direct ? q.Index + "|" + string.Join("\n", q.Items.ConvertAll(t => t.Path + "|" + t.Title)) : MusicPlayer.Way;
        if (key == listKey) return;
        listKey = key;
        list.Children.Clear();
        if (!direct)
        {
            queueTitle.Text = Tr("대기열", "Queue");
            string name = PlayerWays.NameOf(MusicPlayer.Way);
            list.Children.Add(ToolUi.Empty(Tr($"{name}에서 재생해요 — 대기열은 {name}에서 보세요", $"Playing in {name} — see the queue there")));
            return;
        }
        queueTitle.Text = Tr($"대기열 · {q.Items.Count}곡", $"Queue · {q.Items.Count}");
        if (q.Items.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(Tr("비어 있어요 — 오디오 파일에서 Ctrl+K › 대기열에 추가", "Empty — Ctrl+K on an audio file › Add to the queue")));
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
                var remove = ToolUi.Button(G.Close, Tr("대기열에서 빼기", "Take out of the queue"), () => MusicPlayer.Remove(at));
                remove.Width = remove.Height = 22;
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
            }
            list.Children.Add(ToolUi.Row(row, () => MusicPlayer.PlayAt(at)));
        }
    }

    public void FocusFirst() => view.Dispatcher.BeginInvoke(() => view.Focus(), System.Windows.Threading.DispatcherPriority.Input);

    public void Closed()
    {
        MusicPlayer.Changed -= Refresh;
        MusicPlayer.Watch(false);
    }
}
