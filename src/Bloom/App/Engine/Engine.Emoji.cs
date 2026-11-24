using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 이모지 · 기호: search in Korean or English, Enter pastes into the window that was in front.
internal sealed partial class Engine
{
    public List<Section> EmojiView(string text)
    {
        var sections = new List<Section>();
        if (text.Trim().Length > 0)
        {
            var found = Symbols.Find(text, 80);
            var s = new Section(Tr("결과", "Results"), Tr($"{found.Count}개", $"{found.Count}"));
            foreach (var e in found) s.Items.Add(FromSymbol(e));
            if (s.Items.Count == 0)
                s.Items.Add(new ResultItem { Id = "sym:none", Kind = ItemKind.Info, Title = Tr("검색된 이모지·기호 없음", "No matching emoji or symbols"), Subtitle = Tr("다른 말로, 또는 영어로 찾아보세요 (heart, arrow)", "Try another word or search in English (heart, arrow)"), Icon = IconSpec.Of(G.Info) });
            sections.Add(s);
            return sections;
        }
        var recent = new Section(Tr("최근 사용", "Recent Items"));
        foreach (var (id, _) in Usage.Current.Items.Where(kv => kv.Key.StartsWith("sym:", StringComparison.Ordinal)).OrderByDescending(kv => kv.Value.Last).Take(12))
            if (Symbols.Of(id[4..]) is { } e) recent.Items.Add(FromSymbol(e));
        if (recent.Items.Count > 0) sections.Add(recent);
        var emoji = new Section(Tr("자주 쓰는 이모지", "Frequent emoji"));
        foreach (var e in Symbols.All.Where(e => e.Emoji && e.Rank > 0).Take(48)) emoji.Items.Add(FromSymbol(e));
        sections.Add(emoji);
        var symbols = new Section(Tr("기호", "Symbol"));
        foreach (var e in Symbols.All.Where(e => !e.Emoji && e.Rank > 0).Take(40)) symbols.Items.Add(FromSymbol(e));
        sections.Add(symbols);
        return sections;
    }

    /// The root search: a line starting with ":" (":하트", ":fire") lists emoji right there.
    internal Section? RootSymbols(string text)
    {
        string t = text.TrimStart();
        if (t.Length < 2 || t[0] != ':') return null;
        var found = Symbols.Find(t[1..], 8);
        if (found.Count == 0) return null;
        var s = new Section(Tr("이모지 · 기호", "Emoji · Symbols"), Tr("Enter 붙여넣기", "Enter to paste"));
        foreach (var e in found) s.Items.Add(FromSymbol(e));
        return s;
    }

    public ResultItem FromSymbol(SymbolEntry e)
    {
        string ch = e.Char;
        string id = "sym:" + ch;
        return new ResultItem
        {
            Id = id,
            Kind = ItemKind.Symbol,
            Title = e.Name,
            Subtitle = e.Emoji ? null : ch,
            Accessory = e.Emoji ? Tr("이모지", "Emoji") : Tr("기호", "Symbol"),
            Icon = IconSpec.Symbol(ch, e.Emoji),
            PrimaryLabel = Tr("붙여넣기", "Paste"),
            Run = () =>
            {
                Usage.Touch(id);
                _ = host.Clipboard.PasteText(ch);
            },
            MoreActions = () => new List<ActionItem>
            {
                new() { Title = Tr("복사만 하기", "Copy only"), Keys = "Ctrl+Enter", Glyph = G.Copy, Run = () => { Usage.Touch(id); Launch.Copy(ch); } },
                new() { Title = Tr($"코드 복사 ({e.Code})", $"Copy code ({e.Code})"), Glyph = G.Copy, Run = () => Launch.Copy(e.Code) },
            },
            Detail = () =>
            {
                var d = new Detail { Symbol = ch, SymbolIsEmoji = e.Emoji };
                d.Add(Tr("이름", "Name"), e.English);
                if (e.Korean.Length > 0) d.Add(Tr("한국어", "Korean"), e.Korean);
                d.Add(Tr("코드", "Code"), e.Code);
                return d;
            },
        };
    }
}
