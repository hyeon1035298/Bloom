using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bloom.Music;

namespace Bloom.Ui;

/// The home's 플레이어 block (App\HomePlayer): the song (its cover, or a music note on a tile until one comes), its title,
/// where it plays and the artist, and previous · play/pause · next. It lays out the song it is given and reads nothing
/// else, so the kept home's key changes only with what shows (no position: it would draw the block anew every time).
internal static class PlayerViews
{
    /// `open`: the mini player (a click on the song, or on the line when nothing plays); `press`: a button (null: the
    /// buttons steer nothing, a made-up song).
    public static FrameworkElement Block(NowPlaying.Song s, Action open, Action<PlayerOp>? press)
    {
        var body = new Grid { Margin = new Thickness(20, 0, 16, 8) };
        if (!s.Has)
        {
            var line = Theme.T(Tr("재생 중인 곡 없음 · 누르면 미니 플레이어", "Nothing playing · click for the mini player"), 12, Theme.P.TextFaint);
            line.Margin = new Thickness(0, 2, 0, 2);
            body.Children.Add(Clickable(line, open));
            return body;
        }
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var song = new Grid();
        song.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        song.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        song.Children.Add(Cover(s.Cover));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Theme.T(s.Title, 12.5, Theme.P.Text, Theme.Medium));
        string under = s.App.Length > 0 && s.Artist.Length > 0 ? s.App + " · " + s.Artist : s.App + s.Artist;
        if (under.Length > 0)
        {
            var second = Theme.T(under, 11, Theme.P.TextFaint);
            second.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(second);
        }
        Grid.SetColumn(words, 1);
        song.Children.Add(words);
        song.ToolTip = Tr("미니 플레이어 열기", "Open the mini player");
        body.Children.Add(Clickable(song, open));

        var buttons = new (char glyph, string tip, PlayerOp op, bool on)[]
        {
            (PlayerGlyph.Previous, Tr("이전 곡", "Previous song"), PlayerOp.Previous, s.CanPrevious),
            (s.Playing ? PlayerGlyph.Pause : PlayerGlyph.Play, Tr("재생 · 일시정지", "Play · pause"), PlayerOp.Toggle, s.CanToggle),
            (PlayerGlyph.Next, Tr("다음 곡", "Next song"), PlayerOp.Next, s.CanNext),
        };
        for (int i = 0; i < buttons.Length; i++)
        {
            var (glyph, tip, op, on) = buttons[i];
            // one the app does not take faded, its glyph's colour too (the kept home's key reads colours, not opacity)
            var b = ToolUi.Button(glyph, tip, () => press?.Invoke(op), on ? null : Theme.P.TextFaint);
            b.Width = b.Height = 24;
            b.Margin = new Thickness(i == 0 ? 8 : 2, 0, 0, 0);
            b.MouseLeftButtonDown += (_, e) => e.Handled = true; // the search box keeps the focus, the block is not dragged
            b.IsEnabled = on;
            b.Opacity = on ? 1 : 0.5;
            Grid.SetColumn(b, 1 + i);
            body.Children.Add(b);
        }
        return body;
    }

    /// The cover's square: the picture, or a music note on a tile (the note a child of its own, so the kept home's key
    /// changes when the picture comes or goes).
    private static SmoothBorder Cover(ImageSource? cover)
    {
        var tile = new SmoothBorder
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = cover is null ? Theme.P.Tile : new ImageBrush(cover) { Stretch = Stretch.UniformToFill },
        };
        if (cover is null) tile.Child = Theme.Glyph(PlayerGlyph.Music, 16, Theme.P.TextFaint);
        return tile;
    }

    /// A part that opens something: pressed it keeps the search box's focus, let go it acts.
    private static FrameworkElement Clickable(FrameworkElement part, Action act)
    {
        var holder = new Border { Child = part, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        holder.MouseLeftButtonDown += (_, e) => e.Handled = true;
        holder.MouseLeftButtonUp += (_, e) =>
        {
            act();
            e.Handled = true;
        };
        return holder;
    }
}
