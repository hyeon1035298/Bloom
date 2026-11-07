using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Clip;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// Clipboard history: grouped by day, pinned first; Enter pastes into the window that was in front.
internal sealed partial class Engine
{
    private const char PinGlyph = '', UnpinGlyph = '';

    public List<Section> ClipboardView(string text)
    {
        var sections = new List<Section>();
        var clip = host.Clipboard;
        if (!Settings.Current.ClipHistory)
        {
            var off = new Section(null);
            off.Items.Add(new ResultItem
            {
                Id = "clip:off",
                Kind = ItemKind.Info,
                Title = Tr("클립보드 기록 꺼짐", "Clipboard history is off"),
                Subtitle = Tr("Enter로 켜기 · 켠 뒤에 복사한 것부터 저장됩니다", "Enter turns it on · it keeps what you copy from then on"),
                Icon = IconSpec.Of(G.Paste),
                PrimaryLabel = Tr("켜기", "Turn on"),
                StayOpen = true,
                Run = () =>
                {
                    Settings.Current.ClipHistory = true;
                    Settings.Save();
                    host.ApplyHotkeys();
                    host.Window.Refresh(false);
                },
            });
            sections.Add(off);
        }
        var list = clip.Find(text.Trim());
        var today = DateTime.Today;
        var groups = new (string title, Func<ClipEntry, bool> has)[]
        {
            (Tr("고정됨", "Pinned"), e => e.Pinned),
            (Tr("오늘", "Today"), e => !e.Pinned && e.CopiedAt >= today),
            (Tr("어제", "Yesterday"), e => !e.Pinned && e.CopiedAt >= today.AddDays(-1) && e.CopiedAt < today),
            (Tr("이번 주", "This week"), e => !e.Pinned && e.CopiedAt >= today.AddDays(-7) && e.CopiedAt < today.AddDays(-1)),
            (Tr("이전", "Earlier"), e => !e.Pinned && e.CopiedAt < today.AddDays(-7)),
        };
        bool first = true;
        foreach (var (title, has) in groups)
        {
            var s = new Section(title, first && list.Count > 0 ? Tr($"{list.Count}개", $"{list.Count}") : null);
            foreach (var e in list.Where(has)) s.Items.Add(FromClip(e));
            if (s.Items.Count == 0) continue;
            sections.Add(s);
            first = false;
        }
        if (list.Count == 0 && Settings.Current.ClipHistory)
        {
            var s = new Section(null);
            s.Items.Add(new ResultItem
            {
                Id = "clip:none",
                Kind = ItemKind.Info,
                Title = text.Trim().Length > 0 ? Tr("검색 결과 없음", "Nothing found") : Tr("클립보드 기록 없음", "Nothing copied yet"),
                Subtitle = Tr("복사한 텍스트·이미지·파일 기록 · 비밀번호 관리자가 숨긴 내용 제외", "Text, images and files you copy gather here (not what password managers hide)"),
                Icon = IconSpec.Of(G.Info),
            });
            sections.Add(s);
        }
        return sections;
    }

    private ResultItem FromClip(ClipEntry e)
    {
        var clip = host.Clipboard;
        string? url = ClipboardHistory.Url(e);
        var snapshot = new ClipPreview(e.Kind, e.Text, e.Files?.ToArray() ?? Array.Empty<string>(),
            e.Kind == "image" ? ClipboardHistory.ImagePath(e) : "", e.Width, e.Height,
            e.Source, e.CopiedAt, e.Pinned, url is not null);
        bool readsFile = snapshot.Kind == "image" || (snapshot.Kind == "files" && snapshot.Files.Length == 1);
        var item = new ResultItem
        {
            Id = e.Id,
            Kind = ItemKind.Clip,
            Title = ClipboardHistory.Title(e),
            Subtitle = ClipboardHistory.Detail(e),
            Accessory = Ago(e.CopiedAt),
            Icon = e.Kind switch
            {
                "image" => IconSpec.Thumb(ClipboardHistory.ImagePath(e)),
                "files" when e.Files is { Count: > 0 } f => IconSpec.Shell(f[0]),
                _ when url is not null => IconSpec.Of(G.Link),
                _ => IconSpec.Of(G.Document),
            },
            Data = e,
            PrimaryLabel = Tr("붙여넣기", "Paste"),
            Run = () => _ = clip.Paste(e),
            Detail = readsFile ? null : () => ClipDetail(snapshot),
            ReadDetail = readsFile ? () => ClipDetail(snapshot) : null,
        };
        item.MoreActions = () =>
        {
            var list = new List<ActionItem>
            {
                new() { Title = Tr("복사만 하기", "Copy only"), Keys = "Ctrl+Enter", Glyph = G.Copy, Run = () => clip.Put(e) },
            };
            if (url is not null) list.Add(new ActionItem { Title = Tr("브라우저에서 열기", "Open in the browser"), Glyph = G.Globe, Run = () => Browser.Web.Open(url) });
            if (e.Kind == "image") list.Add(new ActionItem { Title = Tr("이미지 열기", "Open the image"), Glyph = G.NewWindow, Run = () => Launch.Open(ClipboardHistory.ImagePath(e)) });
            if (e.Kind == "files" && e.Files is { Count: > 0 } files) list.Add(new ActionItem { Title = Tr("파일 위치 열기", "Show in folder"), Glyph = G.Folder, Run = () => Launch.Reveal(files[0]) });
            if (e.Kind == "text")
                list.Add(new ActionItem
                {
                    Title = Tr("AI에게 이 텍스트로 묻기", "Ask AI about this text"),
                    Glyph = G.Sparkle,
                    StayOpen = true,
                    Run = () => host.Window.ShowChat(host.Chats.New(), null, (Tr("복사한 텍스트", "Copied text"), "", e.Text ?? "")),
                });
            if (e.Kind == "text" && !string.IsNullOrWhiteSpace(e.Text))
            {
                string text = e.Text;
                list.Add(new ActionItem { Title = Tr("상용구로 저장", "Save as a snippet"), Glyph = KindGlyph("snippet"), StayOpen = true, Run = () => SaveSnippet(text) });
            }
            list.Add(new ActionItem
            {
                Title = e.Pinned ? Tr("고정 해제", "Unpin") : Tr("위에 고정", "Pin to the top"),
                Keys = "Ctrl+D",
                Glyph = e.Pinned ? UnpinGlyph : PinGlyph,
                StayOpen = true,
                Run = () =>
                {
                    clip.SetPinned(e, !e.Pinned);
                    host.Window.Refresh(true);
                },
            });
            list.Add(new ActionItem
            {
                Title = Tr("이 항목 지우기", "Delete this entry"),
                Keys = "Ctrl+Delete",
                Glyph = G.Delete,
                Danger = true,
                StayOpen = true,
                Run = () =>
                {
                    clip.Delete(e);
                    host.Window.Refresh(false);
                },
            });
            list.Add(new ActionItem
            {
                Title = Tr("기록 모두 지우기 (고정 빼고)", "Clear the history (not the pinned)"),
                Glyph = G.Delete,
                Danger = true,
                StayOpen = true,
                Run = () => host.Window.Confirm(Tr("고정되지 않은 클립보드 기록을 모두 삭제합니다.", "Deletes all unpinned clipboard entries."), new ActionItem
                {
                    Title = Tr("모두 지우기", "Delete all"),
                    Keys = "Enter",
                    Glyph = G.Delete,
                    Danger = true,
                    StayOpen = true,
                    Run = () =>
                    {
                        int n = clip.Clear();
                        host.Window.Refresh(false);
                        host.Window.ShowToast(Tr($"{n}개 삭제 완료", $"Deleted {n}"));
                    },
                }),
            });
            return list;
        };
        return item;
    }

    private sealed record ClipPreview(string Kind, string? Text, string[] Files, string ImagePath,
        int Width, int Height, string? Source, DateTime CopiedAt, bool Pinned, bool IsLink);

    private static Search.Detail ClipDetail(ClipPreview e)
    {
        Search.Detail d;
        switch (e.Kind)
        {
            case "image":
                string path = e.ImagePath;
                d = new Search.Detail { Image = path, Shell = path };
                d.Add(Tr("종류", "Kind"), Tr("이미지", "Image")).Add(Tr("해상도", "Resolution"), $"{e.Width} × {e.Height}");
                try
                {
                    d.Add(Tr("크기", "Size"), Size(new FileInfo(path).Length));
                }
                catch
                {
                    /* the PNG went away */
                }
                break;
            case "files" when e.Files is { Length: 1 } one:
                d = Search.Detail.ForPath(one[0], Directory.Exists(one[0]));
                break;
            case "files":
                d = new Search.Detail { Text = string.Join("\n", e.Files), Icon = IconSpec.Of(G.Folder) };
                d.Add(Tr("종류", "Kind"), Tr($"파일 {e.Files.Length}개", $"{e.Files.Length} files"));
                break;
            default:
                d = TextDetail(e.Text ?? "", e.IsLink ? Tr("링크", "Link") : Tr("텍스트", "Text"));
                break;
        }
        d.Add(Tr("가져온 곳", "From"), e.Source).Add(Tr("복사한 시각", "Copied at"), e.CopiedAt.ToString("yyyy. M. d. HH:mm:ss"));
        if (e.Pinned) d.Add(Tr("고정", "Pinned"), Tr("예", "Yes"));
        return d;
    }

    /// A text's preview: the text, its kind, how long it is (copied text, a snippet).
    private static Search.Detail TextDetail(string text, string kind)
    {
        var d = new Search.Detail { Text = text };
        int lines = text.TrimEnd('\r', '\n').Split('\n').Length;
        return d.Add(Tr("종류", "Kind"), kind).Add(Tr("글자 수", "Characters"), Tr($"{text.Length:#,0}자", $"{text.Length:#,0}") + (lines > 1 ? Tr($" · {lines}줄", $" · {lines} lines") : ""));
    }
}
