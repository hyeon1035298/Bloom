using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The clipboard view's 상용구 tab: the snippets of 사용자 명령, one list in both places rather than a second store,
/// so their keywords and {date} {time} {clipboard} {input} work from either. Enter pastes into the window that was in
/// front, as the history does; text copied before goes in from the history's Ctrl+K.
internal sealed partial class Engine
{
    private const string SnippetKind = "snippet";
    private const int SnippetNameRoom = 40; // the typed text in the "‘…’ 상용구로 저장" row

    public static int SnippetCount => UserCommands.Current.Items.Count(c => c.Kind == SnippetKind);

    public List<Section> SnippetsView(string text)
    {
        var sections = new List<Section>();
        string typed = text.Trim();
        var q = new Query(typed);
        var mine = new Section(Tr("상용구", "Snippets"), Tr($"{SnippetCount}개", $"{SnippetCount}"));
        foreach (var c in UserCommands.Current.Items)
        {
            if (c.Kind != SnippetKind) continue;
            // by its name and keyword, and by what it says
            if (!q.IsEmpty && Matcher.Score(q, c.Match) <= 0 && !UserCommands.SnippetText(c.Body).Contains(typed, StringComparison.OrdinalIgnoreCase)) continue;
            mine.Items.Add(FromSnippet(c));
        }
        if (mine.Items.Count > 0) sections.Add(mine);
        // last, so Enter on a fresh opening pastes the first snippet; a text that finds none leaves this row alone
        var add = new Section(mine.Items.Count > 0 ? null : Tr("상용구", "Snippets"));
        add.Items.Add(new ResultItem
        {
            Id = "snip:new",
            Kind = ItemKind.Command,
            Title = typed.Length > 0 ? Tr($"‘{Clip(typed, SnippetNameRoom)}’ 상용구로 저장", $"Save ‘{Clip(typed, SnippetNameRoom)}’ as a snippet") : NewTitle(SnippetKind),
            Subtitle = Tr("자주 쓰는 글 · Enter로 앞의 창에 붙여넣기 · {date} {time} {clipboard} {input}", "A text you use often · Enter pastes it into the window in front · {date} {time} {clipboard} {input}"),
            Icon = IconSpec.Of(G.Add, true),
            PrimaryLabel = Tr("만들기", "Create"),
            StayOpen = true,
            Run = () => host.Window.StartForm(CommandForm(SnippetKind, null, typed.Length > 0 ? typed : null)),
            Detail = typed.Length > 0 ? () => TextDetail(typed, NewTitle(SnippetKind)) : null,
        });
        sections.Add(add);
        return sections;
    }

    /// The 사용자 명령 row with what this tab needs: its keyword at the right, its whole text in the preview.
    private ResultItem FromSnippet(UserCommand c)
    {
        var item = FromCommand(c, null);
        item.Accessory = c.Keyword;
        string text = UserCommands.SnippetText(c.Body);
        item.Detail = () => TextDetail(text, Tr("상용구", "Snippet")).Add(Tr("단축어", "Keyword"), c.Keyword);
        // the list keeps the file's order: a row dragged to another place stays there (row ids are "my:" + the command's)
        item.MoveTo = (target, after) => UserCommands.Move(c.Id, target[(target.IndexOf(':') + 1)..], after);
        return item;
    }

    /// The history's Ctrl+K › 상용구로 저장: saved at once under its first words; a text with {input} asks for the
    /// keyword it then needs (the form), and one already saved is not saved twice.
    private void SaveSnippet(string text)
    {
        string body = SnippetBody(text);
        if (UserCommands.Current.Items.Find(c => c.Kind == SnippetKind && c.Body == body) is { } same)
        {
            host.Window.ShowToast(Tr($"‘{same.Name}’ 상용구에 이미 있음", $"Already a snippet: ‘{same.Name}’"));
            return;
        }
        if (body.Contains("{input}", StringComparison.OrdinalIgnoreCase))
        {
            host.Window.StartForm(CommandForm(SnippetKind, null, body));
            return;
        }
        string name = SuggestCommandName(SnippetKind, SnippetBody(text.Trim()));
        var c = UserCommands.New(SnippetKind, name.Length > 0 ? name : Tr("상용구", "Snippet"), body, null);
        UserCommands.Upsert(c);
        host.Window.ShowToast(Tr($"‘{c.Name}’ 상용구로 저장 완료", $"Saved ‘{c.Name}’ as a snippet"));
    }

    /// A snippet keeps its lines in one as "\n" (UserCommands.SnippetText turns them back).
    internal static string SnippetBody(string text) => text.Replace("\r\n", "\n").Replace("\n", "\\n");
}
