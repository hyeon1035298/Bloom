using System.Linq;
using Bloom.Core;

namespace Bloom.Tests;

/// 메모장's store: titles, order (pinned first, then the newest), search, and an empty note going without a trace.
internal static class NotesTests
{
    public static void Notebook()
    {
        Notes.Current.Items.Clear();
        var a = Notes.New("장보기\n- 우유\n- 계란");
        var b = Notes.New("");
        var c = Notes.New("회의\n견적서 다시 보내기");
        Check.Eq(a.Title, "장보기", "the first line is the title");
        Check.True(b.Title.Length > 0, "an empty note still has a title to show");
        Notes.SetPinned(a, true);
        Check.Eq(Notes.Ordered.First().Id, a.Id, "a pinned note comes first");
        a.Updated = 1;
        b.Updated = 2; // older than the change below even within one millisecond (the clock's step)
        Notes.SetText(c, "회의\n견적서 다시 보내기 · 일정");
        Check.Eq(Notes.Ordered.ElementAt(1).Id, c.Id, "then the most recently changed");
        Check.True(Matcher.Score(new Query("견적서"), c.Match) > 0 && Matcher.Score(new Query("견적서"), a.Match) == 0, "the search finds words past the title");
        Notes.Delete(b);
        Check.True(Notes.Find(b.Id) is null && Notes.Current.Items.Count == 2, "an empty note just goes (nothing to the Recycle Bin)");
        Notes.Current.Items.Clear();
    }

    public static void TitleWithoutHeadingMarks()
    {
        Notes.Current.Items.Clear();
        var shop = Notes.New("# 장보기\n- [ ] 우유");
        Check.Eq(shop.Title, "장보기", "a Markdown heading's # goes");
        Check.Eq(Notes.New("###   회의 메모  \r\n본문").Title, "회의 메모", "any number of them, and the spaces after");
        Check.Eq(Notes.New("#해시태그\n").Title, "#해시태그", "a # with no space after is kept (not a heading)");
        Check.Eq(Notes.New("#\n").Title, "빈 메모", "a heading with nothing in it: as empty");
        Check.True(Matcher.Score(new Query("장보기"), shop.Match) > 0, "the search finds it by its title");
        Notes.Current.Items.Clear();
    }
}
