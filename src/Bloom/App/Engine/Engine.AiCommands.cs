using System;
using System.Collections.Generic;
using Bloom.Search;

namespace Bloom.App;

/// 내 AI 명령 in the 내 명령 view and its form: what it works on, how often it runs by itself, its actions.
internal sealed partial class Engine
{
    private static string NewTitle(string kind) => kind switch
    {
        "snippet" => Tr("새 상용구", "New snippet"),
        "chain" => Tr("새 묶음", "New chain"),
        "ai" => Tr("새 AI 명령", "New AI command"),
        _ => Tr("새 스크립트", "New script"),
    };

    /// The AI command's last two questions (after prompt, name and keyword): its input, then its schedule.
    private static void AddAiFields(FormSpec f, UserCommand? edit)
    {
        f.Fields.Add(new FormField
        {
            Label = Tr("입력", "Input"),
            Hint = Tr("선택 · 클립보드 · 없음 — Enter만 누르면 선택한 글", "selection · clipboard · none — Enter alone takes the selection"),
            Value = edit is null ? "" : AiInput.Name(edit.Input),
            Check = v => AiInput.Parse(v) is { } input ? (input, null) : (null, Tr("선택, 클립보드, 없음 중에서 입력하세요", "Enter selection, clipboard or none")),
        });
        f.Fields.Add(ScheduleField(edit?.Every, () => f[3]));
    }

    /// 주기 (optional): only for a command that needs no text, so nothing selected or copied is ever sent unwatched.
    private static FormField ScheduleField(string? every, Func<string> input) => new()
    {
        Label = Tr("주기", "Schedule"),
        Hint = Tr("주기 (선택) — 예) 30분, 2시간, 3일, 매일 09:00 · 입력이 ‘없음’일 때만", "Schedule (optional) — e.g. 30m, 2h, 3d, daily 09:00 · only with no input"),
        Value = every is null ? "" : AiSchedule.Name(every),
        Optional = true,
        Check = v =>
        {
            var (parsed, error) = AiSchedule.Parse(v);
            if (error is not null) return (null, error);
            if (parsed is not null && input() != AiInput.None) return (null, Tr("주기는 입력이 ‘없음’인 명령에만 정할 수 있습니다", "Only a command with no input can run on a schedule"));
            return (parsed ?? "", null);
        },
    };

    /// The form's answers into the command; a schedule set or changed counts from now (its first run is one interval on).
    private static void SetAiFields(UserCommand c, UserCommand? edit, string input, string every)
    {
        c.Input = input;
        c.Every = every.Length > 0 ? every : null;
        c.LastRun = edit is not null && edit.Every == c.Every ? edit.LastRun : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// "다음 실행 14:30" / "다음 실행 11월 8일 09:00".
    private static string NextRunText(UserCommand c)
    {
        if (c.Every is null || AiSchedule.Next(c.Every, DateTimeOffset.FromUnixTimeMilliseconds(c.LastRun).LocalDateTime) is not { } next) return "";
        if (next < DateTime.Now) next = DateTime.Now;
        string when = When(next);
        return Tr($"다음 실행 {when}", $"Next run {when}");
    }

    private List<ActionItem> AiCommandActions(UserCommand c)
    {
        var list = new List<ActionItem>();
        if (c.Thread is { } thread) list.Add(new ActionItem { Title = Tr("결과 채팅 열기", "Open the results chat"), Glyph = G.Chat, Run = () => host.OpenThread(thread) });
        if (c.Input == AiInput.None)
            list.Add(new ActionItem
            {
                Title = c.Every is null ? Tr("주기 정하기", "Set a schedule") : Tr("주기 바꾸기", "Change the schedule"),
                Glyph = G.Clock,
                StayOpen = true,
                Run = () => host.Window.StartForm(ScheduleForm(c)),
            });
        if (c.Every is not null)
            list.Add(new ActionItem
            {
                Title = Tr("주기 실행 끄기", "Stop the schedule"),
                Glyph = G.Stop,
                StayOpen = true,
                Run = () =>
                {
                    c.Every = null;
                    UserCommands.Upsert(c);
                    host.Window.ShowToast(Tr("주기 실행 끔", "Schedule off"));
                },
            });
        return list;
    }

    private FormSpec ScheduleForm(UserCommand c)
    {
        var f = new FormSpec { Title = Tr($"‘{c.Name}’ 주기", $"‘{c.Name}’ schedule"), Back = Mode.Commands };
        f.Fields.Add(ScheduleField(c.Every, () => c.Input));
        f.Done = form =>
        {
            string every = form[0];
            c.Every = every.Length > 0 ? every : null;
            c.LastRun = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            UserCommands.Upsert(c);
            host.Window.Go(Mode.Commands);
            host.Window.ShowToast(c.Every is null ? Tr("주기 실행 끔", "Schedule off") : AiSchedule.Name(c.Every) + " · " + NextRunText(c));
        };
        return f;
    }
}
