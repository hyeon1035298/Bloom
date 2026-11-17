using System;
using System.Collections.Generic;
using Bloom.Browser;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// Quicklinks ("yt 고양이") and the small form in the search line that makes and edits them.
internal sealed partial class Engine
{
    public ResultItem FromQuicklink(Quicklink l, string? query)
    {
        string? h = Quicklinks.HostOf(l.Plain);
        string id = "ql:" + l.Id;
        bool needsWords = l.TakesQuery && string.IsNullOrWhiteSpace(query);
        var item = new ResultItem
        {
            Id = id,
            Kind = ItemKind.Quicklink,
            Title = query is { Length: > 0 } && !needsWords ? $"{l.Name} — “{query.Trim()}”" : l.Name,
            Subtitle = needsWords ? (l.Keyword is { Length: > 0 } k ? Tr($"{k} + 검색어 · {h}", $"{k} + search words · {h}") : h) : h,
            Accessory = Tr("브라우저", "Browser"),
            Icon = IconSpec.Of(G.Link),
            Data = l,
            PrimaryLabel = needsWords ? Tr("검색어 넣기", "Type search words") : Tr("브라우저에서 열기", "Open in the browser"),
            StayOpen = needsWords,
            AsFavorite = l.TakesQuery ? null : new Favorites.Entry { Id = id, Kind = "quicklink", Title = l.Name, Subtitle = h, Target = l.Id },
        };
        string Url() => l.TakesQuery ? l.For(query ?? "") : l.Url;
        item.Run = needsWords
            ? () => host.OpenView(Mode.Root, (l.Keyword is { Length: > 0 } kw ? kw : l.Name) + " ")
            : () =>
            {
                Usage.Touch(id);
                Web.Open(Url());
            };
        item.MoreActions = () =>
        {
            var list = new List<ActionItem>();
            if (!needsWords)
            {
                list.Add(new ActionItem { Title = Tr("기본 브라우저로 열기", "Open in the default browser"), Keys = "Ctrl+Enter", Glyph = G.NewWindow, Run = () => { Usage.Touch(id); Web.Open(Url(), defaultBrowser: true); } });
                list.Add(new ActionItem { Title = Tr("링크 복사", "Copy link"), Keys = "Ctrl+Shift+C", Glyph = G.Link, Run = () => Launch.Copy(Url()) });
            }
            list.Add(new ActionItem { Title = Tr("바로 가기 고치기", "Edit shortcut"), Glyph = G.Settings, StayOpen = true, Run = () => host.Window.StartForm(QuicklinkForm(l, null, null)) });
            list.Add(new ActionItem
            {
                Title = Tr("바로 가기 지우기", "Delete shortcut"),
                Glyph = G.Delete,
                Danger = true,
                StayOpen = true,
                Run = () => host.Window.Confirm(Tr($"‘{l.Name}’ 바로 가기를 삭제합니다.", $"Deletes the site shortcut ‘{l.Name}’."), new ActionItem
                {
                    Title = Tr("지우기", "Delete"),
                    Keys = "Enter",
                    Glyph = G.Delete,
                    Danger = true,
                    StayOpen = true,
                    Run = () =>
                    {
                        Quicklinks.Remove(l);
                        Favorites.Remove(id);
                        host.Window.Refresh(false);
                    },
                }),
            });
            return list;
        };
        return item;
    }

    /// "yt 고양이" in the root list: the quicklink with the words, above everything else.
    internal Section? TypedQuicklink(string text)
    {
        if (Quicklinks.Typed(text) is not { } t) return null;
        var s = new Section(Tr("브라우저", "Browser"));
        s.Items.Add(FromQuicklink(t.link, t.query));
        return s;
    }

    public List<Section> QuicklinksView(string text)
    {
        var sections = new List<Section>();
        var q = new Query(text);
        var top = new Section(null);
        top.Items.Add(new ResultItem
        {
            Id = "ql:new",
            Kind = ItemKind.Command,
            Title = Tr("새 바로 가기", "New shortcut"),
            Subtitle = Quicklinks.Normalize(text) is { } typed ? typed : Tr("주소, 이름, 단축어 입력", "Enter an address, name and keyword"),
            Icon = IconSpec.Of(G.Add, true),
            PrimaryLabel = Tr("만들기", "Create"),
            StayOpen = true,
            Run = () => host.Window.StartForm(QuicklinkForm(null, Quicklinks.Normalize(text), null)),
        });
        sections.Add(top);
        var mine = new Section(Tr("브라우저", "Browser"), Tr($"{Quicklinks.Current.Items.Count}개 · 단축어 + 스페이스 + 검색어", $"{Quicklinks.Current.Items.Count} · keyword + space + search words"));
        foreach (var l in Quicklinks.Current.Items)
            if (q.IsEmpty || Matcher.Score(q, l.Match) > 0)
                mine.Items.Add(FromQuicklink(l, null));
        if (mine.Items.Count > 0) sections.Add(mine);
        return sections;
    }

    /// New (or changed) quicklink: address → name → keyword, asked in the search line.
    public FormSpec QuicklinkForm(Quicklink? edit, string? url, string? name)
    {
        var f = new FormSpec { Title = edit is null ? Tr("새 바로 가기", "New shortcut") : Tr("바로 가기 고치기", "Edit shortcut"), Back = Mode.Quicklinks };
        f.Fields.Add(new FormField
        {
            Label = Tr("주소", "Address"),
            Hint = Tr("주소 — 검색어 자리는 {query}  예) youtube.com/results?search_query={query}", "Address — {query} marks the search words  e.g. youtube.com/results?search_query={query}"),
            Value = edit?.Url ?? url ?? "",
            Check = v => Quicklinks.Normalize(v) is { } u ? (u, null) : (null, Tr("주소를 확인하세요 · 예: naver.com 또는 https://…", "Check the address · e.g. example.com or https://…")),
        });
        f.Fields.Add(new FormField
        {
            Label = Tr("이름", "Name"),
            Hint = Tr("검색에 사용할 이름", "Name used in search"),
            Value = edit?.Name ?? name ?? "",
            Check = v => v.Trim().Length > 0 ? (v.Trim(), null) : (null, Tr("이름을 입력하세요", "Enter a name")),
        });
        f.Fields.Add(new FormField
        {
            Label = Tr("단축어", "Keyword"),
            Hint = Tr("단축어 (선택) — 예) yt 를 정하면 “yt 고양이”로 바로 검색", "Keyword (optional) — e.g. with yt, “yt cats” searches right away"),
            Value = edit?.Keyword ?? "",
            Optional = true,
            Check = v =>
            {
                string k = v.Trim().ToLowerInvariant();
                bool query = f[0].Contains("{query}", StringComparison.OrdinalIgnoreCase);
                if (k.Length == 0) return query ? (null, Tr("검색어가 포함된 주소에는 단축어를 입력하세요", "Enter a keyword for an address with search words")) : ("", null);
                if (k.Contains(' ')) return (null, Tr("공백 없는 단축어를 입력하세요", "Enter a keyword without spaces"));
                if (!UserCommands.KeywordFree(k, edit?.Id))
                    return (null, Tr($"‘{k}’ 단축어 사용 중 · 다른 단축어를 입력하세요", $"‘{k}’ is in use. Enter another keyword."));
                return (k, null);
            },
        });
        f.Prepare = (form, step) =>
        {
            var field = form.Fields[step];
            if (field.Value.Length > 0) return;
            if (step == 1) field.Value = Quicklinks.SuggestName(form[0]);
            else if (step == 2) field.Value = Quicklinks.SuggestKeyword(form[1], edit?.Id);
        };
        f.Done = form =>
        {
            var l = edit is null ? Quicklinks.New(form[1], form[0], form[2].Length > 0 ? form[2] : null) : new Quicklink { Id = edit.Id, Name = form[1], Url = form[0], Keyword = form[2].Length > 0 ? form[2] : null };
            Quicklinks.Upsert(l);
            host.Window.Go(Mode.Quicklinks);
            host.Window.ShowToast(edit is null ? Tr("바로 가기 추가 완료", "Shortcut added") : Tr("바로 가기 저장 완료", "Shortcut saved"));
        };
        return f;
    }

    /// The form's answers so far, the one being typed marked.
    public static List<Section> FormView(FormSpec f, string typing)
    {
        var s = new Section(f.Title, Tr($"{Math.Min(f.Step + 1, f.Fields.Count)} / {f.Fields.Count} · Enter 다음 · Esc 취소", $"{Math.Min(f.Step + 1, f.Fields.Count)} / {f.Fields.Count} · Enter next · Esc cancel"));
        for (int i = 0; i < f.Fields.Count; i++)
        {
            var field = f.Fields[i];
            bool now = i == f.Step;
            string value = now ? typing : field.Value;
            s.Items.Add(new ResultItem
            {
                Id = "form:" + i,
                Kind = ItemKind.Info,
                Title = field.Label,
                Subtitle = value.Length > 0 ? value : field.Optional ? Tr("(선택)", "(optional)") : "",
                Accessory = now ? (field.Keys ? Tr("키 누르기", "Press keys") : Tr("입력 중", "Typing")) : i < f.Step ? Tr("완료", "Done") : null,
                Icon = IconSpec.Of(now ? G.Keyboard : i < f.Step ? G.Check : G.Info, now),
                PrimaryLabel = i == f.Fields.Count - 1 ? Tr("저장", "Save") : Tr("다음", "Next"),
                Run = null,
            });
        }
        var list = new List<Section> { s };
        if (f.Error is { } err)
        {
            var e = new Section(null);
            e.Items.Add(new ResultItem { Id = "form:error", Kind = ItemKind.Info, Title = err, Icon = IconSpec.Of(G.Info, true) });
            list.Insert(0, e);
        }
        return list;
    }
}
