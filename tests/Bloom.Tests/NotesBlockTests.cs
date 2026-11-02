using System;
using System.Linq;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

/// The home's 메모 block (App\Engine\Engine.NotesBlock.cs): off by default with 3 rows, pinned notes first then the
/// newest, as many as its rows, 새 메모 last (alone when there are no notes), gone with 메모 switched off.
internal static class NotesBlockTests
{
    /// The home with only the 메모 block on, its section (null: not on the home).
    private static Section? Block(Engine engine) => engine.Root("").Find(s => s.HomeKey == "notes");

    private static void OnlyNotes(Action check)
    {
        Settings.Current.Home = null;
        Notes.Current.Items.Clear();
        foreach (var k in Home.Kinds) Home.Set(k.key, k.key == "notes");
        try
        {
            check();
        }
        finally
        {
            Settings.Current.Home = null;
            Notes.Current.Items.Clear();
            Features.Set("notes", true);
        }
    }

    public static void DefaultsOffWithThreeRows()
    {
        Settings.Current.Home = null;
        try
        {
            var w = Home.Of("notes")!;
            Check.True(!w.On && w.Right is true, "off by default, in the right column");
            Check.Eq(Home.Count(w), 3, "three rows by default");
            Check.True(Home.Hidden().Exists(h => h.Key == "notes"), "+ 블록 추가 offers it");
        }
        finally
        {
            Settings.Current.Home = null;
        }
    }

    public static void PinnedFirstNewestThenNewNote()
    {
        OnlyNotes(() =>
        {
            var engine = new Engine(new Host(new Options()));
            var old = Notes.New("오래된 메모");
            var pinned = Notes.New("# 고정한 메모\n본문");
            var newest = Notes.New("새로 쓴 메모");
            var middle = Notes.New("중간 메모");
            old.Updated = 1000;
            pinned.Updated = 2000;
            middle.Updated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60_000;
            newest.Updated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Notes.SetPinned(pinned, true);
            var s = Check.NotNull(Block(engine), "the block is on the home");
            Check.Eq(string.Join(" | ", s.Items.Select(i => i.Title)), "고정한 메모 | 새로 쓴 메모 | 중간 메모 | 새 메모", "pinned first, then the newest, three rows, 새 메모 last");
            Check.Eq(s.Items[0].Id, "note:" + pinned.Id, "a row is its note");
            Check.True(s.Items[0].Subtitle!.StartsWith("고정 · ", StringComparison.Ordinal), "a pinned note says so");
            Check.Eq(s.Items[1].Subtitle, "방금", "the others say when only");
            Check.True(s.Items.All(i => i.StayOpen && i.Run is not null), "every row opens 메모 and keeps the launcher");
            Check.Eq(s.Items[^1].Id, "note:add", "the new note row");
            Check.Eq(s.Note, "4개", "the header counts every note when some are left out");
            Home.SetCount("notes", 5);
            s = Block(engine)!;
            Check.Eq(s.Items.Count, 5, "five rows: the four notes and 새 메모");
            Check.Eq(s.Note, null, "no count when all show");
            Home.SetCollapsed("notes", true);
            s = Block(engine)!;
            Check.True(s.Collapsed && s.Items.Count == 0, "folded: its header only");
        });
    }

    public static void BlankNotesLeftOut()
    {
        OnlyNotes(() =>
        {
            var engine = new Engine(new Host(new Options()));
            Notes.New("하나");
            Notes.New("둘");
            Notes.New("셋");
            Notes.New(""); // 새 메모's, still open in 메모
            Notes.New("  \n ");
            var s = Check.NotNull(Block(engine), "the block is on the home");
            Check.True(!s.Items.Exists(i => i.Title == "빈 메모"), "a blank note is no row");
            Check.Eq(s.Items.Count, 4, "the three notes and 새 메모");
            Check.Eq(s.Note, null, "nor is it counted");
            Notes.New("넷");
            Check.Eq(Block(engine)!.Note, "4개", "four with words");
        });
    }

    public static void LatestKeepsTheOrder()
    {
        static Note N(string text, long updated, bool pinned = false) => new() { Id = text, Text = text, Updated = updated, Pinned = pinned };
        var notes = new System.Collections.Generic.List<Note> { N("a", 5), N("b", 9), N("p", 1, true), N("c", 7), N("d", 9), N(" ", 99), N("q", 3, true) };
        var top = Engine.Latest(notes, 4, out int count);
        Check.Eq(count, 6, "every note with words");
        Check.Eq(string.Join(",", top.Select(n => n.Id)), "q,p,b,d", "pinned first, then the newest, a tie in the list's order");
        var ordered = notes.Where(n => n.Text.Trim().Length > 0).OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Updated).Take(4);
        Check.Eq(string.Join(",", top.Select(n => n.Id)), string.Join(",", ordered.Select(n => n.Id)), "Notes.Ordered's order");
        Check.Eq(Engine.Latest(notes, 10, out _).Count, 6, "fewer than the rows: all of them");
    }

    public static void NoNotesOnlyNewNote()
    {
        OnlyNotes(() =>
        {
            var s = Check.NotNull(Block(new Engine(new Host(new Options()))), "a block with no notes stays");
            Check.Eq(s.Items.Single().Id, "note:add", "only 새 메모");
            Check.Eq(s.Note, null, "nothing to count");
        });
    }

    public static void GoneWithNotesOff()
    {
        OnlyNotes(() =>
        {
            Notes.New("메모 하나");
            Features.Set("notes", false);
            Check.Eq(Block(new Engine(new Host(new Options()))), null, "메모 switched off in 설정 > 기능: no block");
            Check.True(!Home.Hidden().Exists(h => h.Key == "notes"), "+ 블록 추가 leaves it out too");
        });
    }
}
