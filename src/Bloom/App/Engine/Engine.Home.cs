using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The home: the blocks the user turned on, in their order.
internal sealed partial class Engine
{
    /// 홈 편집 ✎ on a usage card: the account's own words in place of its e-mail (empty: the e-mail again).
    private void RenameAccount(string id)
    {
        if (AiAccounts.Find(id) is not { } a) return; // a made-up QA card
        var f = new FormSpec { Title = Tr("AI 계정 표시 이름", "AI account name") };
        f.Fields.Add(new FormField
        {
            Label = Tr("표시 이름", "Name"),
            Hint = a.Email is { Length: > 0 } mail ? Tr($"카드에 이메일 대신 보일 문구 — 비우면 이메일 ({mail})", $"Shown on the card instead of the e-mail — empty: the e-mail ({mail})") : Tr("카드에 보일 문구", "Shown on the card"),
            Value = a.Alias ?? "",
            Optional = true,
            Check = v => (v.Trim(), null),
        });
        f.Done = form =>
        {
            a.Alias = form[0].Length == 0 ? null : form[0];
            Settings.Save();
            host.Window.Go(Mode.Root);
        };
        host.Window.StartForm(f);
    }

    /// The home: the blocks the user turned on, in their order (설정 > 홈 화면), the left column's first; a list block
    /// as many rows as set (Home.Count), a folded one its header only.
    private List<Section> RootEmpty(List<Section> sections)
    {
        var shown = new HashSet<string>();
        foreach (var e in Favorites.All()) shown.Add(e.Id); // favorites (and what sits in folders) are not "recent"
        foreach (var w in Home.Order())
        {
            if (!w.On || !Features.Shows(w.Key)) continue; // a block of a feature switched off in 설정 > 기능
            int before = sections.Count;
            switch (w.Key)
            {
                case "usage" when AiUsage.Available:
                    AiUsage.FirstLook();
                    var accounts = AiUsage.Accounts;
                    string? asked = AiUsage.Busy ? Tr("불러오는 중…", "Loading…") : AiUsage.Fetched is { } fetched ? Ago(fetched) : null;
                    (Action Moved, Action<string> Rename)? edit = host.Window.HomeEditing ? (() => host.Window.Refresh(true), RenameAccount) : null;
                    sections.Add(new Section(Tr("AI 사용량", "AI usage"), asked) { Widget = () => Ui.HomeViews.Usage(accounts, edit), Reload = () => _ = AiUsage.Refresh() });
                    break;
                case "tokens" or "tokenchart" or "tokenmodels":
                    sections.Add(TokenBlock(w));
                    break;
                case "timers":
                    var timers = new Section(Tr("타이머 · 알림", "Timers · reminders"), Tr("Ctrl+K 5분 더 · 취소", "Ctrl+K 5 more min · cancel"));
                    foreach (var e in Timers.Waiting.Take(Home.Count(w))) timers.Items.Add(FromTimer(e));
                    if (timers.Items.Count > 0) sections.Add(timers);
                    break;
                case "favorites":
                    // a row of icons (Ctrl+1…9, ←→ inside it)
                    var favs = new Section(Tr("즐겨찾기", "Favorites")) { Strip = true };
                    foreach (var e in Favorites.Current.Items)
                        if (FromFavorite(e) is { } item) favs.Items.Add(item);
                    if (favs.Items.Count > 0) sections.Add(favs);
                    break;
                case "week":
                    var marks = CalendarMarks("");
                    int count = Enumerable.Range(0, 7).Sum(i => marks.TryGetValue(DateTime.Today.AddDays(i), out var l) ? l.Count : 0);
                    sections.Add(new Section(Tr("이번 주", "This week"), count > 0 ? count == 1 ? Tr("알림 1개", "1 reminder") : Tr($"알림 {count}개", $"{count} reminders") : Tr("알림 없음", "No reminders")) { Widget = () => Ui.HomeViews.Week(marks, d => host.Window.OpenCalendar(d)) });
                    break;
                case "recent":
                    var recent = new Section(Tr("최근 사용", "Recent"));
                    foreach (var (id, _) in Usage.Current.Items.OrderByDescending(kv => kv.Value.Last))
                    {
                        if (shown.Contains(id)) continue;
                        if (FromId(id) is not { } item) continue;
                        recent.Items.Add(item);
                        shown.Add(id);
                        if (recent.Items.Count >= Home.Count(w)) break;
                    }
                    if (recent.Items.Count > 0) sections.Add(recent);
                    break;
                case "recentfiles":
                    var files = new Section(Tr("최근 파일", "Recent files"));
                    foreach (var f in RecentFiles.List().Where(f => !shown.Contains((f.Folder ? "folder:" : "file:") + f.Path.ToLowerInvariant())).Take(Home.Count(w))) files.Items.Add(FromRecent(f));
                    if (files.Items.Count > 0) sections.Add(files);
                    break;
                case "clipboard":
                    var clips = new Section(Tr("최근 복사", "Recently copied"), Tr("Ctrl+Alt+V 전체", "Ctrl+Alt+V all"));
                    foreach (var c in host.Clipboard.Items.OrderByDescending(c => c.Copied).Take(Home.Count(w))) clips.Items.Add(FromClip(c));
                    if (clips.Items.Count > 0) sections.Add(clips);
                    break;
                case "threads":
                    var threads = new Section(Tr("최근 AI 채팅", "Recent AI chats"));
                    foreach (var t in Threads("").SelectMany(x => x.Items).Where(i => i.Id != "thread:new").Take(Home.Count(w))) threads.Items.Add(t);
                    if (threads.Items.Count > 0) sections.Add(threads);
                    break;
                case "commands":
                    var cmds = new Section(Tr("명령", "Command"));
                    foreach (var e in SystemItems.Commands)
                    {
                        if (shown.Contains(e.Id) || !Features.Shows(e.Id) || e.Id is "cmd:reindex" or "cmd:lock" or "cmd:restart" or "cmd:shutdown" or "cmd:quit") continue;
                        cmds.Items.Add(FromSys(e));
                    }
                    if (cmds.Items.Count > 0) sections.Add(cmds);
                    break;
            }
            if (sections.Count == before) continue;
            var block = sections[^1];
            block.HomeKey = w.Key;
            block.HomeRight = w.Right is true;
            block.HomeCount = Home.Count(w);
            if (!w.Collapsed) continue;
            block.Collapsed = true;
            block.Items.Clear();
            block.Widget = null;
            block.Strip = false;
        }
        return sections;
    }
}
