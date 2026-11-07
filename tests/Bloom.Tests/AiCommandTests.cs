using System;
using System.Linq;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

/// 내 AI 명령: the schedule's words and times, a macro's steps, what each step sends, and the inbox preview.
internal static class AiCommandTests
{
    public static void ScheduleWords()
    {
        Check.Eq(AiSchedule.Parse("2시간"), ("2h", (string?)null), "N시간");
        Check.Eq(AiSchedule.Parse("2시간마다"), ("2h", (string?)null), "N시간마다 (how the form shows it back)");
        Check.Eq(AiSchedule.Parse("3일"), ("3d", (string?)null), "N일");
        Check.Eq(AiSchedule.Parse("30분"), ("30m", (string?)null), "30 minutes is the shortest");
        Check.Eq(AiSchedule.Parse("1.5시간"), ("90m", (string?)null), "a fraction of an hour in minutes");
        Check.Eq(AiSchedule.Parse("Every 2 h"), ("2h", (string?)null), "English, as the form shows it back");
        Check.Eq(AiSchedule.Parse("매일 9:05"), ("daily 09:05", (string?)null), "매일 HH:MM");
        Check.Eq(AiSchedule.Parse("매일 9시"), ("daily 09:00", (string?)null), "매일 N시");
        Check.Eq(AiSchedule.Parse("Daily at 09:00"), ("daily 09:00", (string?)null), "English daily, as shown back");
        Check.Eq(AiSchedule.Parse(""), ((string?)null, (string?)null), "empty: no schedule");
        Check.Eq(AiSchedule.Parse("끄기"), ((string?)null, (string?)null), "off: no schedule");
        Check.True(AiSchedule.Parse("10분").error is not null, "under 30 minutes is refused");
        Check.True(AiSchedule.Parse("매일 25:00").error is not null, "no 25 o'clock");
        Check.True(AiSchedule.Parse("자주").error is not null, "words it can't read");
        Check.Eq(AiSchedule.Name("2h"), "2시간마다", "the name of an interval");
        Check.Eq(AiSchedule.Name("90m"), "90분마다", "minutes");
        Check.Eq(AiSchedule.Name("daily 09:00"), "매일 09:00", "a daily time");
    }

    public static void ScheduleTimes()
    {
        var last = new DateTime(2026, 11, 7, 10, 0, 0);
        Check.Eq(AiSchedule.Next("2h", last), new DateTime(2026, 11, 7, 12, 0, 0), "an interval counts from the last run");
        Check.Eq(AiSchedule.Next("3d", last), new DateTime(2026, 11, 10, 10, 0, 0), "days");
        Check.Eq(AiSchedule.Next("daily 09:00", last), new DateTime(2026, 11, 8, 9, 0, 0), "a daily time already passed today: tomorrow");
        Check.Eq(AiSchedule.Next("daily 18:30", last), new DateTime(2026, 11, 7, 18, 30, 0), "a daily time later today");
        Check.Eq(AiSchedule.Next("5m", last), new DateTime(2026, 11, 7, 10, 30, 0), "a hand-edited 5m still waits the shortest interval");
        Check.Eq(AiSchedule.Next("x", last), null, "unreadable: never");
        // off for three days: due once; after that run the next one counts from it
        var back = new DateTime(2026, 11, 10, 8, 0, 0);
        Check.True(AiSchedule.Due("daily 09:00", last, back), "a missed daily run is due");
        Check.True(!AiSchedule.Due("daily 09:00", back, back.AddMinutes(30)), "after catching up once, it waits for the next 09:00");
        Check.True(!AiSchedule.Due("2h", last, last.AddMinutes(119)), "not before its time");
    }

    public static void MacroSteps()
    {
        var summary = UserCommands.New("ai", "요약", "핵심 3줄로 요약", "sum");
        var macro = UserCommands.New("ai", "요약 후 번역", " sum >>영어로 번역 >> ", null);
        var other = UserCommands.New("snippet", "번역", "not an ai command", null);
        var all = new[] { summary, macro, other };
        Check.Eq(string.Join("|", AiCommandRun.Steps(macro, all)), "핵심 3줄로 요약|영어로 번역", "a step named by another AI command's keyword runs its prompt; empty steps go");
        var self = UserCommands.New("ai", "자기", "자기 >> 다듬기", null);
        Check.Eq(string.Join("|", AiCommandRun.Steps(self, new[] { self })), "자기|다듬기", "a command never runs itself as a step");
        var nested = UserCommands.New("ai", "겹", "요약 후 번역", null);
        Check.Eq(string.Join("|", AiCommandRun.Steps(nested, all.Append(nested))), "sum|영어로 번역", "one level: the inner command's steps are prompts as written");
    }

    public static void StepMessage()
    {
        var now = new DateTime(2026, 11, 7, 9, 5, 0);
        Check.Eq(AiCommandRun.Message("영어로 번역", "안녕하세요", now), "영어로 번역\n\n<text>\n안녕하세요\n</text>", "the text after the prompt");
        Check.Eq(AiCommandRun.Message("‘{input}’을 제목으로", " 회의록 ", now), "‘회의록’을 제목으로", "{input} puts the text in place");
        Check.Eq(AiCommandRun.Message("{date} 할 일 정리", "", now), "2026-11-07 할 일 정리", "no input: the prompt alone, {date} filled");
        Check.Eq(AiCommandRun.Message("한 줄\\n두 줄", "", now), "한 줄\n두 줄", "\\n in the one-line form is a new line");
    }

    public static void InputWords()
    {
        Check.Eq(AiInput.Parse(""), AiInput.Selection, "Enter alone: the selection");
        Check.Eq(AiInput.Parse("클립보드"), AiInput.Clipboard, "클립보드");
        Check.Eq(AiInput.Parse(" 없음 "), AiInput.None, "없음");
        Check.Eq(AiInput.Parse(AiInput.Name(AiInput.None)), AiInput.None, "the shown name reads back");
        Check.Eq(AiInput.Parse("뭐"), null, "anything else");
        Check.True(!UserCommands.New("ai", "x", "{input} 요약", "k").TakesInput, "an AI command never waits for keyword + input");
    }

    public static void InboxPreview()
    {
        Check.Eq(NoticeLog.Preview("첫 줄\n\n  둘째   줄 "), "첫 줄 둘째 줄", "one line, spaces folded");
        Check.Eq(NoticeLog.Preview(new string('가', 400)).Length, 301, "cut at 300 with an ellipsis");
        Check.Eq(NoticeLog.Preview("## 오늘\n**놓치다(miss)** — 버스를 *놓쳤어요*.\n1. 기차를 ~~놓칠~~ 뻔했어요.\n- [x] `miss` 복습\n> [사전](https://example.com) 참고"),
            "오늘 놓치다(miss) — 버스를 놓쳤어요. 기차를 놓칠 뻔했어요. miss 복습 사전 참고", "Markdown marks off: heading, bold, italic, list, box, code, quote, link");
        Check.Eq(NoticeLog.Preview("```\nvar a = 2*3*4;\n```"), "var a = 2*3*4;", "fences dropped, a lone * kept");
    }
}
