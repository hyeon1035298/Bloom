using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The right half of the launcher in file search and clipboard history.
internal sealed class PreviewPane : Grid
{
    private readonly SmoothBorder media = new() { CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(16, 14, 16, 0) };
    private readonly StackPanel meta = new() { Margin = new Thickness(16, 6, 16, 10) };
    private readonly ScrollViewer metaScroll;
    private MediaElement? player;
    private int version;
    private readonly DispatcherTimer start = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private Action? pending;

    public PreviewPane()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(media);
        metaScroll = new ScrollViewer { Content = meta, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, MaxHeight = 190, Template = Launcher.ThinScroll() };
        SetRow(metaScroll, 1);
        Children.Add(metaScroll);
        // the heavy part (decode, player) waits until the selection rests a moment
        start.Tick += (_, _) =>
        {
            start.Stop();
            var p = pending;
            pending = null;
            p?.Invoke();
        };
    }

    /// null clears the pane and stops any video.
    public void Show(Detail? d)
    {
        int v = ++version;
        start.Stop();
        pending = null;
        StopPlayer();
        media.Child = null;
        meta.Children.Clear();
        media.Background = d is null ? Brushes.Transparent : Theme.P.Card;
        media.BorderBrush = Theme.P.CardLine;
        media.BorderThickness = new Thickness(d is null ? 0 : 1);
        if (d is null) return;

        // facts
        if (d.Meta.Count > 0) meta.Children.Add(Pad(Section2.Header(Tr("정보", "Info"), null)));
        foreach (var (label, value) in d.Meta) meta.Children.Add(MetaLine(label, value));

        // the big one
        if (d.Text is { } text && d.Image is null && d.Video is null)
        {
            media.Background = Theme.P.Code;
            var tb = new TextBlock
            {
                Text = text.Length > 8000 ? text[..8000] + "…" : text,
                FontFamily = LooksLikeCode(text) ? Theme.Mono : Theme.Sans,
                FontSize = 12.5,
                Foreground = Theme.P.Text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, 12, 14, 12),
            };
            media.Child = new ScrollViewer { Content = tb, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Template = Launcher.ThinScroll() };
            return;
        }
        if (d.Fill is { } fill)
        {
            media.Background = fill;
            return;
        }
        if (d.Symbol is { } symbol)
        {
            var big = Icons.SymbolTile(symbol, d.SymbolIsEmoji, 112, plain: true);
            big.HorizontalAlignment = HorizontalAlignment.Center;
            big.VerticalAlignment = VerticalAlignment.Center;
            media.Child = big;
            return;
        }
        var stage = new Grid();
        media.Child = stage;
        // first the shell's thumbnail (instant when cached), then the real picture or the player over it
        if (d.Shell is { } shell)
        {
            var img = Fit(null);
            stage.Children.Add(img);
            Icons.Large(shell, 256, d.ShellIcon, src =>
            {
                if (v != version || src is null) return;
                img.Source = src;
                // an icon (not a thumbnail) stays small in the middle
                if (src.Width <= 64 || (d.Image is null && d.Video is null && !IsThumbnailShaped(src))) img.MaxWidth = img.MaxHeight = 96;
            });
        }
        else if (d.Icon is { } icon)
        {
            var tile = Icons.Build(icon, 64);
            tile.HorizontalAlignment = HorizontalAlignment.Center;
            tile.VerticalAlignment = VerticalAlignment.Center;
            stage.Children.Add(tile);
        }
        if (d.Image is { } pic)
        {
            pending = () => LoadPicture(pic, stage, v);
            start.Start();
        }
        else if (d.Video is { } video)
        {
            pending = () => Play(video, stage, v);
            start.Start();
        }
    }

    private static bool IsThumbnailShaped(ImageSource src) => src.Width >= 128 && Math.Abs(src.Width - src.Height) > 2;

    private static FrameworkElement Pad(FrameworkElement h)
    {
        h.Margin = new Thickness(0, 6, 0, 4);
        return h;
    }

    private static Image Fit(ImageSource? src)
    {
        var img = new Image { Source = src, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    private static FrameworkElement MetaLine(string label, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 0), MinHeight = 28 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = Theme.T(label, 12, Theme.P.TextFaint);
        g.Children.Add(l);
        var t = Theme.T(Hangul.KeepAll(value), 12, Theme.P.TextDim, Theme.Medium);
        t.HorizontalAlignment = HorizontalAlignment.Right;
        t.ToolTip = value;
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var line = new SmoothBorder { BorderBrush = Theme.P.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        return line;
    }

    private static bool LooksLikeCode(string s)
    {
        int braces = 0;
        foreach (char c in s.Length > 2000 ? s[..2000] : s)
            if (c is '{' or '}' or ';' or '<' or '>' or '=') braces++;
        return braces > 12;
    }

    private async void LoadPicture(string path, Grid stage, int v)
    {
        var (bmp, w, h) = await Task.Run(() =>
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                b.DecodePixelWidth = 900;
                b.UriSource = new Uri(path);
                b.EndInit();
                b.Freeze();
                var dec = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var f = dec.Frames[0];
                return ((BitmapSource?)b, f.PixelWidth, f.PixelHeight);
            }
            catch
            {
                return (null, 0, 0);
            }
        });
        if (v != version || bmp is null) return;
        // a small picture is not blown up: DecodePixelWidth made it 900 wide, so size it back
        var img = Fit(bmp);
        if (w > 0 && w < 900) img.MaxWidth = w;
        if (h > 0 && w < 900) img.MaxHeight = h;
        stage.Children.Clear();
        stage.Children.Add(img);
        if (w > 0) meta.Children.Insert(Math.Min(3, meta.Children.Count), MetaLine(Tr("해상도", "Dimensions"), $"{w} × {h}"));
    }

    private void Play(string path, Grid stage, int v)
    {
        if (v != version) return;
        var p = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            IsMuted = true,
            Stretch = Stretch.Uniform,
            ScrubbingEnabled = false,
            Cursor = Cursors.Hand,
            ToolTip = Tr("소리 없이 미리 보기 · 클릭하면 멈춤", "Muted preview · Click to pause"),
        };
        bool paused = false;
        p.MediaOpened += (_, _) =>
        {
            if (v != version) return;
            if (p.NaturalDuration.HasTimeSpan)
            {
                var t = p.NaturalDuration.TimeSpan;
                meta.Children.Insert(Math.Min(3, meta.Children.Count), MetaLine(Tr("길이", "Length"), t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss")));
            }
            if (p.NaturalVideoWidth > 0) meta.Children.Insert(Math.Min(3, meta.Children.Count), MetaLine(Tr("해상도", "Dimensions"), $"{p.NaturalVideoWidth} × {p.NaturalVideoHeight}"));
            // the thumbnail underneath goes once real frames come
            if (stage.Children.Count > 1) stage.Children.RemoveAt(0);
        };
        p.MediaEnded += (_, _) =>
        {
            p.Position = TimeSpan.Zero;
            p.Play();
        };
        p.MediaFailed += (_, e) => Log.Write("preview video: " + e.ErrorException?.Message);
        p.MouseLeftButtonUp += (_, e) =>
        {
            if (paused) p.Play();
            else p.Pause();
            paused = !paused;
            e.Handled = true;
        };
        player = p;
        stage.Children.Add(p);
        p.Source = new Uri(path);
        p.Play();
    }

    private void StopPlayer()
    {
        if (player is null) return;
        try
        {
            player.Stop();
            player.Close();
        }
        catch
        {
            /* already gone */
        }
        player = null;
    }
}
