using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.Ui;

/// Shell icons (IShellItemImageFactory) extracted on one STA worker and cached in memory, and colour emoji drawn there too
/// (ColorEmoji, ~3 ms each). Rows ask for an icon and get it immediately when cached, or a moment later.
internal static class Icons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();
    private static readonly ConcurrentDictionary<string, int> Tones = new(); // +1 white glyph, -1 black glyph
    private static readonly BlockingQueue Work = new();
    private static Dispatcher? ui;
    public static int Pixels = 48;

    private sealed class Job
    {
        public string Key = "", Path = "";
        public List<Action<ImageSource?>> Done = new();
    }

    private sealed class BlockingQueue
    {
        private readonly LinkedList<Job> jobs = new();
        private readonly Dictionary<string, Job> byKey = new();
        private readonly object gate = new();

        public void Add(string key, string path, Action<ImageSource?> done)
        {
            lock (gate)
            {
                if (byKey.TryGetValue(key, out var j))
                {
                    j.Done.Add(done);
                    // most recent requests first: what is on screen now
                    jobs.Remove(j);
                    jobs.AddFirst(j);
                    return;
                }
                j = new Job { Key = key, Path = path };
                j.Done.Add(done);
                byKey[key] = j;
                jobs.AddFirst(j);
                Monitor.Pulse(gate);
            }
        }

        public Job Take()
        {
            lock (gate)
            {
                while (jobs.Count == 0) Monitor.Wait(gate);
                var j = jobs.First!.Value;
                jobs.RemoveFirst();
                byKey.Remove(j.Key);
                return j;
            }
        }
    }

    public static void Start(Dispatcher dispatcher)
    {
        ui = dispatcher;
        // Normal, not below: a GC started here (Workstation, not concurrent) runs at this thread's priority and stops the
        // UI thread and the Win key's hook until it ends, which on a busy PC left them waiting behind everything else
        var t = new Thread(Worker) { IsBackground = true, Name = "icons" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    private static void Worker()
    {
        while (true)
        {
            var job = Work.Take();
            ImageSource? img = null;
            try
            {
                if (job.Key.StartsWith(EmojiPrefix, StringComparison.Ordinal))
                {
                    // emoji:<px>:<1 dark | 0 light>:<text> — ColorEmoji keeps its own cache
                    var parts = job.Key.Split(':', 4);
                    img = ColorEmoji.Render(job.Path, int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[2] == "1");
                    var drawn = job.Done;
                    ui?.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        foreach (var cb in drawn) cb(img);
                    });
                    continue;
                }
                bool thumb = job.Key.StartsWith(ThumbPrefix, StringComparison.Ordinal);
                bool art = job.Key.StartsWith(ArtPrefix, StringComparison.Ordinal);
                bool large = art || job.Key.StartsWith(LargePrefix, StringComparison.Ordinal);
                int tone;
                if (art)
                {
                    int px = int.Parse(job.Key.AsSpan(ArtPrefix.Length, 3));
                    img = Extract(job.Path, px, true, out tone) ?? (Music.AlbumArt.FolderImage(job.Path, File.Exists) is { } cover ? Extract(cover, px, true, out tone) : null);
                }
                else img = large ? Extract(job.Path, int.Parse(job.Key.AsSpan(LargePrefix.Length, 3)), job.Key[LargePrefix.Length + 4] == 'i' ? false : null, out tone) : Extract(job.Path, Pixels, thumb, out tone);
                if (!large && tone != 0 && !thumb) Tones[job.Key] = tone;
            }
            catch (Exception e)
            {
                Log.Error("icon " + job.Path, e);
            }
            // previews and covers are big and seen once: only the small row icons stay in memory
            if (!job.Key.StartsWith(LargePrefix, StringComparison.Ordinal) && !job.Key.StartsWith(ArtPrefix, StringComparison.Ordinal)) Cache[job.Key] = img;
            var callbacks = job.Done;
            ui?.BeginInvoke(DispatcherPriority.Background, () =>
            {
                foreach (var cb in callbacks) cb(img);
            });
        }
    }

    public static bool TryCached(string key, out ImageSource? img) => Cache.TryGetValue(key, out img);

    public static void Request(string key, string path, Action<ImageSource?> done)
    {
        if (Cache.TryGetValue(key, out var img))
        {
            done(img);
            return;
        }
        Work.Add(key, path, done);
    }

    public static void Prefetch(string key, string path)
    {
        if (!Cache.ContainsKey(key)) Work.Add(key, path, _ => { });
    }

    /// Cache keys starting with this ask for the picture's thumbnail instead of its icon.
    public const string ThumbPrefix = "thumb:";

    private const string LargePrefix = "large:", EmojiPrefix = "emoji:", ArtPrefix = "art:";

    /// A colour emoji drawn on the worker (the lists build at once and the emoji come in as they are drawn).
    public static void RequestEmoji(string text, int px, bool dark, Action<ImageSource?> done) =>
        Work.Add($"{EmojiPrefix}{px}:{(dark ? 1 : 0)}:{text}", text, done);

    /// The preview pane's picture: the shell's thumbnail when the file has one, else its big icon (not cached).
    /// `iconOnly`: folders (their thumbnail is the icon on a white square).
    public static void Large(string path, int px, bool iconOnly, Action<ImageSource?> done) =>
        Work.Add($"{LargePrefix}{Math.Clamp(px, 100, 999)}:{(iconOnly ? 'i' : 't')}:{path.ToLowerInvariant()}", path, done);

    /// 미니 플레이어's cover: the picture in the song's file (the shell's thumbnail, cropped square), else the folder's
    /// cover image (Music\AlbumArt), else null. Not cached: the player holds the one of the song on.
    public static void Art(string path, int px, Action<ImageSource?> done) =>
        Work.Add($"{ArtPrefix}{Math.Clamp(px, 100, 999)}:{path.ToLowerInvariant()}", path, done);

    /// thumb: true = thumbnail only (cropped square), false = icon only, null = thumbnail if any, else the icon.
    private static ImageSource? Extract(string path, int px, bool? thumb, out int tone)
    {
        tone = 0;
        if (ShellCom.SHCreateItemFromParsingName(path, IntPtr.Zero, ShellCom.IID_IShellItemImageFactory, out IntPtr pf) != 0 || pf == IntPtr.Zero) return null;
        try
        {
            var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(pf);
            try
            {
                int flags = thumb switch
                {
                    true => ShellCom.SIIGBF_THUMBNAILONLY | ShellCom.SIIGBF_CROPTOSQUARE | ShellCom.SIIGBF_BIGGERSIZEOK,
                    false => ShellCom.SIIGBF_ICONONLY | ShellCom.SIIGBF_BIGGERSIZEOK,
                    null => ShellCom.SIIGBF_BIGGERSIZEOK,
                };
                if (factory.GetImage(new SIZE { cx = px, cy = px }, flags, out IntPtr hbm) != 0 || hbm == IntPtr.Zero) return null;
                try
                {
                    return FromHBitmap(hbm, out tone);
                }
                finally
                {
                    Win32.DeleteObject(hbm);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        finally
        {
            Marshal.Release(pf);
        }
    }

    /// A 32-bit DIB from the shell (premultiplied alpha) → frozen BitmapSource. Icons without any
    /// alpha (old 24-bit ones) are made opaque.
    private static BitmapSource? FromHBitmap(IntPtr hbm, out int tone)
    {
        tone = 0;
        var bmp = new Win32.BITMAP();
        if (Win32.GetObject(hbm, Marshal.SizeOf<Win32.BITMAP>(), ref bmp) == 0) return null;
        int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight);
        if (w <= 0 || h <= 0) return null;
        var bi = new Win32.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var px = new byte[w * h * 4];
        IntPtr dc = Win32.GetDC(IntPtr.Zero);
        try
        {
            if (Win32.GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0) == 0) return null;
        }
        finally
        {
            _ = Win32.ReleaseDC(IntPtr.Zero, dc);
        }
        bool anyAlpha = false;
        for (int i = 3; i < px.Length; i += 4)
            if (px[i] != 0)
            {
                anyAlpha = true;
                break;
            }
        if (!anyAlpha)
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        else tone = ToneOf(px);
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        src.Freeze();
        return src;
    }

    /// Store-app glyphs are often pure white (or black) on transparency, made for a coloured tile;
    /// such an icon gets a plate when it would vanish on the current theme.
    private static int ToneOf(byte[] px)
    {
        int opaque = 0, light = 0, dark = 0, total = px.Length / 4;
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a < 128) continue;
            opaque++;
            // premultiplied → straight
            double b = px[i] * 255.0 / a, g = px[i + 1] * 255.0 / a, r = px[i + 2] * 255.0 / a;
            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
            if (lum > 0.86) light++;
            else if (lum < 0.14) dark++;
        }
        if (opaque == 0 || opaque > total * 0.8) return 0; // a full square icon brings its own background
        if (light > opaque * 0.9) return 1;
        if (dark > opaque * 0.9) return -1;
        return 0;
    }

    private static void Plate(Border holder, Image image, string key, double size)
    {
        if (!Tones.TryGetValue(key, out int tone)) return;
        bool need = (tone > 0 && !Theme.P.Dark) || (tone < 0 && Theme.P.Dark);
        if (!need) return;
        holder.Background = tone > 0 ? new SolidColorBrush(Color.FromRgb(0x2B, 0x24, 0x27)) : new SolidColorBrush(Color.FromRgb(0xEB, 0xE6, 0xE8));
        image.Width = image.Height = size * 0.66;
    }

    // ── row icons ───────────────────────────────────────────────────────────────────────────────
    /// The 24-DIP icon box for a row: a shell image or a glyph tile.
    public static FrameworkElement Build(IconSpec spec, double size = 24)
    {
        if (spec.Mono is { } mono) return MonoTile(mono, size);
        if (spec.Text is { } text) return SymbolTile(text, spec.Emoji, size);
        if (spec.Area is { } area) return AreaTile(area, size);
        if (spec.Fill is { } fill)
            return new SmoothBorder { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.27), Background = fill, BorderBrush = Theme.P.CardLine, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        if (spec.ShellPath is not null)
        {
            var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var holder = new SmoothBorder { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.27), Child = image, VerticalAlignment = VerticalAlignment.Center };
            string key = spec.CacheKey ?? spec.ShellPath.ToLowerInvariant();
            if (TryCached(key, out var img))
            {
                image.Source = img;
                Plate(holder, image, key, size);
            }
            else
                Request(key, spec.ShellPath, i =>
                {
                    image.Source = i;
                    Plate(holder, image, key, size);
                });
            return holder;
        }
        return Tile(spec.Glyph, spec.Accent, size);
    }

    private static Image Img(ImageSource? src, double size)
    {
        var image = new Image { Source = src, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static readonly SolidColorBrush MonoDark = Frozen(0xFF050404), MonoLight = Frozen(0xFF161214);

    private static SolidColorBrush Frozen(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }

    /// A favorite's picture icon: a symbol ("pic:E774", "pic:sparkle") in the accent colour on a black tile
    /// (a hairline in the dark theme). Older letter icons still draw as letters.
    public static FrameworkElement MonoTile(string value, double size)
    {
        FrameworkElement inner;
        if (value == "pic:sparkle") inner = Theme.Sparkle(size * 0.56, Theme.P.Accent);
        else if (value.StartsWith("svg:", StringComparison.Ordinal) && DrawnGeometry(value) is { } g)
            inner = new System.Windows.Shapes.Path { Data = g, Fill = Theme.P.Accent, Stretch = Stretch.Uniform, Width = size * 0.56, Height = size * 0.56 };
        else if (value.StartsWith("pic:", StringComparison.Ordinal) && int.TryParse(value.AsSpan(4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int code))
            inner = Theme.Glyph((char)code, size * 0.5, Theme.P.Accent);
        else
        {
            var t = new TextBlock
            {
                Text = value,
                FontFamily = Theme.Sans,
                FontSize = size * (value.Length > 1 ? 0.4 : 0.5),
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.P.Accent,
                Margin = new Thickness(0, 0, 0, size * 0.03),
            };
            TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
            inner = t;
        }
        inner.HorizontalAlignment = HorizontalAlignment.Center;
        inner.VerticalAlignment = VerticalAlignment.Center;
        // the tile follows the UI (설정 > 즐겨찾기 아이콘 > 스타일): the same surfaces Bloom's own rows and cards use
        string style = Core.Settings.Current.IconStyle;
        Brush ink = style switch { "tile" => Theme.P.TileFg, "card" => Theme.P.Text, _ => Theme.P.Accent };
        Recolor(inner, ink);
        var tile = new SmoothBorder
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.27),
            VerticalAlignment = VerticalAlignment.Center,
            Child = inner,
        };
        switch (style)
        {
            case "black":
                tile.Background = Theme.P.Dark ? MonoDark : MonoLight;
                tile.BorderBrush = Theme.P.Dark ? Theme.P.PopupLine : Brushes.Transparent;
                tile.BorderThickness = new Thickness(Theme.P.Dark ? 1 : 0);
                break;
            case "tile":
                tile.Background = Theme.P.Tile;
                break;
            case "card":
                tile.Background = Theme.P.Raised;
                tile.BorderBrush = Theme.P.CardLine;
                tile.BorderThickness = new Thickness(1);
                tile.Effect = CardLift;
                break;
            default: // soft
                tile.Background = Theme.P.AccentSoft;
                break;
        }
        return tile;
    }

    private static readonly System.Windows.Media.Effects.DropShadowEffect CardLift = MakeLift();

    private static System.Windows.Media.Effects.DropShadowEffect MakeLift()
    {
        var e = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Direction = 270, Opacity = 0.22, Color = Colors.Black };
        e.Freeze();
        return e;
    }

    /// The symbol in the style's ink (glyph text, drawn path or the sparkle's fill).
    private static void Recolor(FrameworkElement inner, Brush ink)
    {
        switch (inner)
        {
            case TextBlock t:
                t.Foreground = ink;
                break;
            case System.Windows.Shapes.Shape sh:
                sh.Fill = ink;
                break;
            default:
                if (inner is Viewbox { Child: System.Windows.Shapes.Shape vs }) vs.Fill = ink;
                break;
        }
    }

    private static readonly System.Collections.Generic.Dictionary<string, Geometry?> drawn = new();

    /// An AI-drawn icon's path data as a frozen geometry (parsed once; null = it doesn't parse).
    private static Geometry? DrawnGeometry(string value)
    {
        if (drawn.TryGetValue(value, out var g)) return g;
        try
        {
            g = Geometry.Parse(value[4..]);
            g.Freeze();
        }
        catch (FormatException)
        {
            g = null;
        }
        return drawn[value] = g;
    }

    /// The symbols a favorite can wear (Segoe Fluent Icons; "sparkle" = the AI mark), for the picker.
    public static readonly (string value, string name)[] Pictures =
    {
        ("pic:sparkle", Tr("AI 반짝임", "AI sparkle")), ("pic:E8BD", Tr("대화", "Chat")), ("pic:E774", Tr("웹", "Web")), ("pic:E721", Tr("검색", "Search")), ("pic:E8B7", Tr("폴더", "Folder")),
        ("pic:E8A5", Tr("문서", "Documents")), ("pic:E70F", Tr("메모 · 편집", "Notes · editing")), ("pic:E943", Tr("코드", "Code")), ("pic:E756", Tr("터미널", "Terminal")), ("pic:E7FC", Tr("게임", "Games")),
        ("pic:E81E", Tr("3D · 레이어", "3D · layers")), ("pic:E790", Tr("색 · 디자인", "Colour · design")), ("pic:E714", Tr("영상", "Video")), ("pic:EB9F", Tr("사진", "Photos")), ("pic:E722", Tr("카메라", "Camera")),
        ("pic:E8D6", Tr("음악", "Music")), ("pic:E720", Tr("마이크", "Microphone")), ("pic:E896", Tr("다운로드", "Downloads")), ("pic:E716", Tr("사람 · 메신저", "People · messenger")), ("pic:E715", Tr("메일", "Mail")),
        ("pic:E787", Tr("달력", "Calendar")), ("pic:EC4A", Tr("사용량 · 속도", "Usage · speed")), ("pic:E8EF", Tr("계산", "Calculator")), ("pic:E713", Tr("설정", "Settings")), ("pic:EB51", Tr("하트", "Heart")),
        ("pic:E734", Tr("별", "Star")), ("pic:E753", Tr("클라우드", "Cloud")), ("pic:E719", Tr("쇼핑", "Shopping")), ("pic:E72E", Tr("잠금", "Lock")), ("pic:E71D", Tr("앱", "App")),
    };

    private static readonly (string words, string pic)[] PictureRules =
    {
        ("claude|gemini|antigravity|copilot", "pic:sparkle"),
        ("chatgpt|openai|codex|gpt", "pic:E8BD"),
        ("usage|사용량|monitor|모니터", "pic:EC4A"),
        ("unity|유니티|vrchat|steam|game|게임|epic", "pic:E7FC"),
        ("blender|블렌더|maya|3ds|substance|painter|zbrush|marvelous", "pic:E81E"),
        ("photoshop|illustrator|figma|krita|clip studio|paint|그림판|canva|indesign|xd", "pic:E790"),
        ("premiere|after effects|davinci|vegas|capcut|영상|video|obs|vlc|player|플레이어", "pic:E714"),
        ("aside|chrome|edge|firefox|browser|브라우저|whale|웨일|opera|brave", "pic:E774"),
        ("파일 검색|everything|search|검색", "pic:E721"),
        ("explorer|탐색기|folder|폴더|files", "pic:E8B7"),
        ("terminal|powershell|cmd|명령 프롬프트|터미널|wsl", "pic:E756"),
        ("visual studio|vs code|code|rider|cursor|notepad++|sublime", "pic:E943"),
        ("discord|slack|kakao|카카오|telegram|line|teams|zoom", "pic:E716"),
        ("spotify|music|음악|멜론|melon|audio|오디오", "pic:E8D6"),
        ("youtube|download|다운|4k", "pic:E896"),
        ("메모|notepad|note|obsidian|notion|word|hwp|한글|onenote", "pic:E70F"),
        ("calc|계산기", "pic:E8EF"),
        ("설정|settings|control|제어판", "pic:E713"),
        ("mail|outlook|메일|thunderbird", "pic:E715"),
        ("calendar|캘린더|일정", "pic:E787"),
        ("camera|카메라", "pic:E722"),
        ("photo|사진|image|viewer|이미지", "pic:EB9F"),
        ("recorder|녹음|mic|마이크", "pic:E720"),
    };

    /// A symbol that fits a name (and the program behind it); `own` = a command's own glyph; else the apps symbol.
    public static string PictureFor(string name, string? target, char own)
    {
        string text = (name + " " + (target is null ? "" : System.IO.Path.GetFileNameWithoutExtension(target))).ToLowerInvariant();
        foreach (var (words, pic) in PictureRules)
            foreach (var w in words.Split('|'))
                if (text.Contains(w, StringComparison.Ordinal)) return pic;
        if (own != '\0') return own == G.Sparkle ? "pic:sparkle" : $"pic:{(int)own:X4}";
        return "pic:E71D";
    }

    /// An emoji (in colour) or a symbol (in the UI font) on a plain tile; also the preview pane's big one.
    public static FrameworkElement SymbolTile(string text, bool emoji, double size, bool plain = false)
    {
        FrameworkElement inner;
        if (emoji && !ColorEmoji.Broken)
        {
            // drawn at once when cached, else on the icons worker and set when it is ready (no wait while a list builds)
            int px = (int)Math.Ceiling(size * (plain ? 1.5 : 2));
            bool dark = Theme.P.Dark;
            var image = Img(null, plain ? size : size * 0.8);
            image.HorizontalAlignment = HorizontalAlignment.Center;
            if (ColorEmoji.TryCached(text, px, dark, out var bmp)) image.Source = bmp;
            else RequestEmoji(text, px, dark, i => image.Source = i);
            inner = image;
        }
        else
            inner = new TextBlock
            {
                Text = text,
                FontFamily = emoji ? EmojiFont : Theme.Sans,
                FontSize = size * (plain ? 0.7 : 0.6),
                Foreground = Theme.P.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
        if (plain) return inner;
        return new SmoothBorder { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.27), Background = Theme.P.Tile, Child = inner, VerticalAlignment = VerticalAlignment.Center };
    }

    private static readonly FontFamily EmojiFont = new("Segoe UI Emoji");

    public static FrameworkElement Tile(char glyph, bool accent, double size = 24)
    {
        var b = new SmoothBorder
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.27),
            Background = accent ? Theme.P.AccentSoft : Theme.P.Tile,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.Child = glyph == G.Sparkle ? Theme.Sparkle(size * 0.56, Theme.P.Accent) : Theme.Glyph(glyph, size * 0.52, accent ? Theme.P.Accent : Theme.P.TileFg);
        return b;
    }

    /// 창 배치's icon: a screen outline on the glyph tile with the part the window takes filled in.
    private static FrameworkElement AreaTile(Rect area, double size)
    {
        double w = Math.Round(size * 0.62), h = Math.Round(size * 0.46), inset = 1.5;
        var screen = new Canvas { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        screen.Children.Add(new System.Windows.Shapes.Rectangle { Width = w, Height = h, RadiusX = 2, RadiusY = 2, Stroke = Theme.P.TileFg, StrokeThickness = 1, Opacity = 0.55 });
        var part = new System.Windows.Shapes.Rectangle
        {
            Width = Math.Max(1, area.Width * (w - inset * 2)),
            Height = Math.Max(1, area.Height * (h - inset * 2)),
            RadiusX = 1,
            RadiusY = 1,
            Fill = Theme.P.TileFg,
        };
        Canvas.SetLeft(part, inset + area.X * (w - inset * 2));
        Canvas.SetTop(part, inset + area.Y * (h - inset * 2));
        screen.Children.Add(part);
        return new SmoothBorder
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.27),
            Background = Theme.P.Tile,
            VerticalAlignment = VerticalAlignment.Center,
            Child = screen,
        };
    }
}
