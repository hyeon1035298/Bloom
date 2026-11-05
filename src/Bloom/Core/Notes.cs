using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Bloom.Core;

/// One note of 메모장: its text (the first line is its title), pinned or not.
internal sealed class Note
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Pinned { get; set; }
    public long Created { get; set; } // unix ms
    public long Updated { get; set; } // unix ms

    /// The first line, without a Markdown heading's leading #s ("# 장보기" → "장보기").
    [JsonIgnore] public string Title => Heading(Text.Split('\n', 2)[0].Trim()) is { Length: > 0 } t ? t : Tr("빈 메모", "Empty note");

    private static string Heading(string line)
    {
        int marks = line.Length - line.TrimStart('#').Length;
        return marks > 0 && (marks == line.Length || char.IsWhiteSpace(line[marks])) ? line[marks..].Trim() : line;
    }
    [JsonIgnore] public DateTime UpdatedAt => DateTimeOffset.FromUnixTimeMilliseconds(Updated).LocalDateTime;

    /// What the search line matches: the title, and the rest of the text as words.
    [JsonIgnore] public MatchTarget Match => match ??= new MatchTarget(Title, new[] { Text });
    [JsonIgnore] private MatchTarget? match;

    public void Touched() => match = null;
}

/// 메모장's notes (notes.json): every keystroke is kept (saved a moment later), pinned ones first, then the newest.
/// A deleted note goes to the Windows Recycle Bin as a .txt file, so it can still be found there.
internal sealed class Notes
{
    public List<Note> Items { get; set; } = new();

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static Notes Current { get; private set; } = new();

    /// A note was made, changed, pinned or deleted (the note, or null for the list).
    public static event Action<Note?>? Changed;

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static void Load()
    {
        saver?.Dispose();
        Current = Json.Load<Notes>(Paths.In("notes.json"));
        saver = Saver.ForJson(Paths.In("notes.json"), () => Current);
    }

    public static void Flush() => saver?.Flush();

    /// Pinned first, then the most recently changed.
    public static IEnumerable<Note> Ordered => Current.Items.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Updated);

    public static Note? Find(string id) => Current.Items.Find(n => n.Id == id);

    public static Note New(string text = "")
    {
        var n = new Note { Id = Guid.NewGuid().ToString("N")[..10], Text = text, Created = Now, Updated = Now };
        Current.Items.Add(n);
        Touch(n);
        return n;
    }

    public static void SetText(Note n, string text)
    {
        if (n.Text == text) return;
        n.Text = text;
        n.Updated = Now;
        n.Touched();
        Touch(n);
    }

    public static void SetPinned(Note n, bool pinned)
    {
        n.Pinned = pinned;
        Touch(n);
    }

    /// The note leaves the list; its text goes to the Recycle Bin as "<title>.txt" (an empty note just goes).
    public static void Delete(Note n)
    {
        if (n.Text.Trim().Length > 0)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "Bloom notes");
                Directory.CreateDirectory(dir);
                string name = string.Concat(n.Title.Take(40).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
                string file = Path.Combine(dir, (name.Length > 0 ? name : "note") + " " + n.Id[..4] + ".txt");
                File.WriteAllText(file, n.Text);
                if (!Native.ShellFiles.Recycle(file, IntPtr.Zero)) File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("note to the recycle bin", e);
            }
        }
        Current.Items.Remove(n);
        Touch(null);
    }

    /// 설정 > 데이터 > 메모: every note goes as Delete sends one (its text to the Recycle Bin) → how many went.
    public static int DeleteAll()
    {
        var all = Current.Items.ToList();
        foreach (var n in all) Delete(n);
        return all.Count;
    }

    private static void Touch(Note? n)
    {
        saver?.Touch();
        Changed?.Invoke(n);
    }
}
