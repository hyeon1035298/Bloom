using System;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// Favorites as results, and their pictured or AI-drawn icons.
internal sealed partial class Engine
{
    private static readonly MatchTarget NewGroupMatch = new(Tr("즐겨찾기 폴더 만들기", "Create favorites folder"), new[] { "new folder group favorites 폴더 만들기 새 폴더 앱 묶기 작업 폴더" });
    private static string Clip(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    /// A favorite as a result; its name icon (when one was made) replaces the app's own.
    public ResultItem? FromFavorite(Favorites.Entry e)
    {
        if (!Features.Shows(e.Id)) return null; // a command of a feature switched off in 설정 > 기능
        var item = FavoriteItem(e);
        if (item is not null && e.Mono is { Length: > 0 } mono && !(e.Kind == Favorites.GroupKind && GroupIcon(e.Icon) is not null))
        {
            item.Icon = IconSpec.MonoOf(mono);
            if (e.Kind == Favorites.GroupKind) item.Data = null; // the letters, not the small icons
        }
        return item;
    }

    /// 설정 > 즐겨찾기 아이콘: a symbol per favorite from its shown name and program (a command keeps its own glyph,
    /// a folder the folder symbol).
    public int MakePictureIcons() => Favorites.MakeMono(e =>
    {
        if (e.Kind == Favorites.GroupKind) return "pic:E8B7";
        var item = FavoriteItem(e);
        char own = item?.Icon is { ShellPath: null } spec ? spec.Glyph : '\0';
        string? target = item?.Data is AppEntry a ? a.Target ?? a.Path : e.Target;
        return Ui.Icons.PictureFor(item?.Title ?? e.Title, target, own);
    });

    /// "AI로 아이콘 그리기": what to draw, in a few words (empty = Claude decides from the name).
    public FormSpec AiIconForm(string id, string name)
    {
        var f = new FormSpec { Title = Tr("AI로 아이콘 그리기", "Generate Icons with AI") };
        f.Fields.Add(new FormField
        {
            Label = Tr("그림", "Image prompt"),
            Hint = Tr($"예: 고양이 발바닥, 번개 · 비우면 ‘{(name.Length > 14 ? name[..14] + "…" : name)}’ 이름 사용 (사용량 1회)", $"e.g. cat paw, lightning · Leave blank to use '{(name.Length > 14 ? name[..14] + "…" : name)}' (uses 1 credit)"),
            Optional = true,
            Check = v => (v.Trim(), null),
        });
        f.Done = form =>
        {
            host.Window.Go(Mode.Root);
            host.Window.DrawIcon(id, name, form[0].Length > 0 ? form[0] : null);
        };
        return f;
    }

    /// 설정 "AI로 전부 그리기": every favorite one by one (each a Claude call); progress(done, total, failed).
    public async System.Threading.Tasks.Task DrawAllIcons(Action<int, int, int> progress)
    {
        var all = Favorites.All().ToList();
        int done = 0, failed = 0;
        foreach (var e in all)
        {
            string name = FavoriteItem(e)?.Title ?? e.Title;
            var (icon, _) = await Ai.IconMaker.Draw(e.Kind == Favorites.GroupKind ? name + " (a folder of apps)" : name, null, System.Threading.CancellationToken.None);
            if (icon is not null) Favorites.SetMono(e.Id, icon);
            else failed++;
            progress(++done, all.Count, failed);
        }
    }

    private ResultItem? FavoriteItem(Favorites.Entry e)
    {
        switch (e.Kind)
        {
            case "app":
            case "uwp":
                var a = Apps.Apps.FirstOrDefault(x => x.Id == e.Id) ?? Apps.Apps.FirstOrDefault(x => x.Path.Equals(e.Target, StringComparison.OrdinalIgnoreCase));
                if (a is not null) return FromApp(a);
                return FromApp(new AppEntry { Id = e.Id, Name = e.Title, Path = e.Target, Source = e.Kind == "uwp" ? "uwp" : "lnk", Match = new MatchTarget(e.Title) });
            case "file":
                return FromPath(e.Target, false);
            case "folder" when e.Id.StartsWith("folder:", StringComparison.Ordinal) && SystemItems.Find(e.Id) is null:
                return FromPath(e.Target, true);
            case "url":
                return FromUrl(e.Target, e.Title, e.Subtitle, ItemKind.Url, Tr("링크", "Link"));
            case "quicklink":
                var ql = Quicklinks.Current.Items.FirstOrDefault(x => x.Id == e.Target);
                return ql is null ? null : FromQuicklink(ql, null);
            case Favorites.GroupKind:
                return FromGroup(e);
            case "sys":
                return SysItem(e.Id);
            case "layout":
                return LayoutItem(e.Id);
            default:
                var s = SystemItems.Find(e.Id);
                return s is null ? null : FromSys(s);
        }
    }
}
