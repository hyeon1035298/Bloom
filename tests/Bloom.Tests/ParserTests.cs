using System;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Tests;

/// The calculator: arithmetic, Korean words and amounts, units, bases.
internal static class CalculatorTests
{
    private static string Value(string input) => Check.NotNull(Calculator.Evaluate(input), input).Value;

    public static void Arithmetic()
    {
        Check.Eq(Value("12*3"), "36", "12*3");
        Check.Eq(Value("3 x 4"), "12", "3 x 4");
        Check.Eq(Value("2x5"), "10", "2x5");
        Check.Eq(Value("(1+2)*3"), "9", "precedence");
        Check.Eq(Value("5!"), "120", "factorial");
        Check.Eq(Value("1,234+1"), "1,235", "thousands separators");
    }

    public static void KoreanWords()
    {
        Check.Eq(Value("10 더하기 5 곱하기 2"), "20", "더하기/곱하기");
        Check.Eq(Value("100 나누기 4"), "25", "나누기");
        Check.Eq(Value("3억 5천만 × 2"), "700,000,000", "억/만 amounts");
    }

    public static void Units()
    {
        string km = Value("10 km to mi");
        Check.True(km.StartsWith("6.21", StringComparison.Ordinal), "10 km to mi → 6.21…, got " + km);
        string pyeong = Value("30평 to m2");
        Check.True(pyeong.StartsWith("99.17", StringComparison.Ordinal), "30평 to m2 → 99.17…, got " + pyeong);
    }

    public static void NotACalculation()
    {
        Check.True(Calculator.Evaluate("chrome") is null, "a word is not maths");
        Check.True(Calculator.Evaluate("10분 타이머") is null, "a timer line is not maths");
    }

    public static void KoreanReadingOnlyInKorean()
    {
        Check.Eq(Calculator.KoreanReading(350_000_000), "3억 5,000만", "Korean reading");
        Lang.Force(true);
        Check.Eq(Calculator.KoreanReading(350_000_000), null, "no Korean reading in English");
    }
}

/// Claude Code's `/usage` text (Claude Code 2.1.292) → the home's two limits.
internal static class ClaudeUsageTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 9, 0, 0);

    public static void UsageText()
    {
        var limits = ClaudeUsage.Parse("""
            Current session: 3% used · resets Oct 8, 12:29pm (Asia/Seoul)
            Current week (all models): 61% used · resets Oct 13, 5:59am (Asia/Seoul)
            Current week (Fable): 0% used · resets Oct 13, 6am (Asia/Seoul)
              92% of your usage was at >150k context
            """, Now);
        Check.Eq(limits.Count, 3, "session + all-models week + Fable's week");
        Check.Eq((limits[0].Title, limits[0].Percent, limits[0].Resets), ("5시간", 3.0, (DateTime?)new DateTime(2026, 10, 8, 12, 29, 0)), "session");
        Check.Eq((limits[1].Title, limits[1].Percent, limits[1].Resets), ("주간", 61.0, (DateTime?)new DateTime(2026, 10, 13, 5, 59, 0)), "week");
        Check.Eq((limits[2].Title, limits[2].Model, limits[2].WindowMins, limits[2].Percent), ("Fable 주간", (string?)"Fable", (int?)UsageLimit.WeekMins, 0.0), "a model's week");
        Check.True(limits.Take(2).All(l => l.Model is null), "the two the home shows have no model");
        var card = new UsageAccount("Claude", null, limits, Now);
        Check.Eq(card.Shown.Count(), 2, "the home's limits are the same two as before");
        Check.Eq(ClaudeUsage.Parse("Not logged in · Please run /login", Now).Count, 0, "signed out");
    }

    public static void ResetTimes()
    {
        Check.Eq(ClaudeUsage.ParseReset("3pm", Now), new DateTime(2026, 10, 8, 15, 0, 0), "a bare time later today");
        Check.Eq(ClaudeUsage.ParseReset("8:30am", Now), new DateTime(2026, 10, 9, 8, 30, 0), "a bare time already past → tomorrow");
        Check.Eq(ClaudeUsage.ParseReset("Jan 2, 6am", new DateTime(2026, 12, 30, 9, 0, 0)), new DateTime(2027, 1, 2, 6, 0, 0), "into next year");
        Check.Eq(ClaudeUsage.ParseReset("Sept 3, 12am", Now), new DateTime(2027, 9, 3, 0, 0, 0), "a long month name, 12am");
        Check.Eq(ClaudeUsage.ParseReset("soon", Now), null, "not a time");
    }
}

/// The notices' readers: Codex log lines into a turn's state, Claude Code transcript tails into the last prompt.
internal static class AgentWatchTests
{
    public static void CodexTurn()
    {
        var t = new CodexWatch.Thread();
        static string Ev(string type, string extra = "") => "{\"timestamp\":\"2026-10-08T01:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"" + type + "\"" + extra + "}}";
        Check.Eq(CodexWatch.Step(t, Ev("task_started", ",\"started_at\":1791421200")).ev, "start", "task_started");
        Check.True(t.TurnOpen && !t.Waiting, "working");
        CodexWatch.Step(t, Ev("user_message", ",\"message\":\"테스트를  추가해줘\\n자세히\""));
        Check.Eq(t.Prompt, "테스트를 추가해줘", "prompt: first line, spaces folded");
        CodexWatch.Step(t, Ev("exec_approval_request"));
        Check.True(t.Waiting, "an approval request waits");
        CodexWatch.Step(t, """{"timestamp":"2026-10-08T01:01:00Z","type":"response_item","payload":{"type":"function_call"}}""");
        Check.True(!t.Waiting, "a call after it goes on");
        var (ev, took) = CodexWatch.Step(t, Ev("task_complete", ",\"duration_ms\":95000"));
        Check.Eq((ev, took), ("done", TimeSpan.FromSeconds(95)), "task_complete with its duration");
        Check.True(!t.TurnOpen, "turn closed");
        CodexWatch.Step(t, Ev("task_started"));
        Check.Eq(CodexWatch.Step(t, Ev("turn_aborted")).ev, "aborted", "a stopped turn is not done");
        Check.Eq(CodexWatch.Step(t, "{\"type\":\"event_msg\",\"payl").ev, null, "a cut line");
    }

    public static void ClaudePrompt()
    {
        string tail = string.Join('\n',
            """{"type":"user","message":{"role":"user","content":"로그인 화면 고쳐줘"}}""",
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":"ok"}]}}""",
            """{"type":"user","isMeta":true,"message":{"role":"user","content":"meta"}}""",
            """{"type":"user","message":{"role":"user","content":"<command-name>/clear</command-name>"}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"네"}]}}""",
            "{\"type\":\"user\",\"message\":{\"con");
        Check.Eq(ClaudeWatch.LastPrompt(tail), "로그인 화면 고쳐줘", "the typed prompt, not tool results, meta or commands");
        Check.Eq(ClaudeWatch.LastPrompt(tail + "\n" + """{"type":"last-prompt","lastPrompt":"버튼 색도 바꿔줘","sessionId":"x"}"""), "버튼 색도 바꿔줘", "Claude Code's own note first");
        Check.Eq(AgentText.Title("[◆ Opus • Web ] 로그인 수정"), "로그인 수정", "a [tag] before the name goes");
        Check.Eq(AgentText.Line(new string('가', 100), 10), "가가가가가가가가가…", "long prompts are cut");
    }
}

/// Timers and reminders from plain text, Korean and English.
internal static class TimeParseTests
{
    private static TimerAsk Read(string text) => Check.NotNull(TimeParse.Read(text), text);

    public static void Timers()
    {
        var a = Read("10분 타이머");
        Check.Eq(a.Kind, "timer", "kind");
        Check.Eq(a.Length, TimeSpan.FromMinutes(10), "10분");
        Check.Eq(Read("타이머 1시간 30분").Length, TimeSpan.FromMinutes(90), "1시간 30분");
        Check.Eq(Read("timer 5m").Length, TimeSpan.FromMinutes(5), "timer 5m");
    }

    public static void Reminders()
    {
        var later = Read("30분 뒤 빨래 확인");
        Check.Eq(later.Kind, "reminder", "kind");
        Check.Eq(later.Length, TimeSpan.FromMinutes(30), "30분 뒤");
        Check.True(later.Label.Contains("빨래", StringComparison.Ordinal), "label keeps the words: " + later.Label);

        var at = Read("오후 3시 반에 전화 알려줘");
        Check.Eq((at.Due.Hour, at.Due.Minute), (15, 30), "오후 3시 반");

        var tomorrow = Read("내일 9시 업로드 리마인더");
        Check.Eq(tomorrow.Due.Date, DateTime.Today.AddDays(1), "내일");
        Check.Eq(tomorrow.Due.Hour, 9, "9시");

        var en = Read("remind me at 3pm to call");
        Check.Eq(en.Kind, "reminder", "English reminder");
        Check.Eq(en.Due.Hour, 15, "3pm");
    }

    public static void BareTimeIsASearch()
    {
        Check.True(TimeParse.Read("10분") is null, "a bare length is not a timer");
        Check.True(TimeParse.Read("엑셀 10분 튜토리얼") is null, "a search with a length in it");
    }

    public static void TimerFieldNeedsNoWord()
    {
        // the 타이머 · 알림 tool only ever starts timers and reminders: a time alone is enough there
        var bare = Check.NotNull(TimeParse.Read("10분", timerField: true), "10분");
        Check.Eq(bare.Kind, "timer", "10분: a timer");
        var named = Check.NotNull(TimeParse.Read("10분 회의", timerField: true), "10분 회의");
        Check.Eq(named.Kind, "timer", "10분 회의: a timer");
        Check.Eq(named.Label, "회의", "its label");
        Check.Eq(named.Length, TimeSpan.FromMinutes(10), "its length");
        var at = Check.NotNull(TimeParse.Read("오후 3시 회의", timerField: true), "오후 3시 회의");
        Check.Eq(at.Kind, "reminder", "a clock time: a reminder");
        Check.Eq(at.Due.Hour, 15, "at 15:00");
        Check.True(TimeParse.Read("회의", timerField: true) is null, "no time: nothing");
    }

    public static void TextInBothLanguages()
    {
        Check.Eq(Core.Timers.DurationText(TimeSpan.FromMinutes(90)), "1시간 30분", "Korean duration");
        Lang.Force(true);
        Check.Eq(Core.Timers.DurationText(TimeSpan.FromMinutes(90)), "1 h 30 min", "English duration");
        Check.True(Core.Timers.WhenText(DateTime.Today.AddHours(15.5)).StartsWith("today", StringComparison.Ordinal), "English 'today'");
    }
}

/// Colours typed as text.
internal static class ColorTests
{
    public static void Parse()
    {
        var color = ColorBook.Parse("#ff5a8a") ?? throw new InvalidOperationException("#ff5a8a: got null");
        Check.Eq(ColorBook.Hex(color), "#FF5A8A", "hex");
        Check.True(ColorBook.Parse("rgb(255, 90, 138)") is { } r && ColorBook.Hex(r) == "#FF5A8A", "rgb(…)");
        Check.True(ColorBook.Parse("not a colour") is null, "words are not a colour");
    }

    public static void Contrast()
    {
        var white = System.Windows.Media.Colors.White;
        var black = System.Windows.Media.Colors.Black;
        Check.Near(ColorBook.Contrast(white, black), 21, 0.01, "white on black");
        Check.Near(ColorBook.Contrast(white, white), 1, 0.01, "white on white");
    }
}

/// Global shortcut strings.
internal static class HotkeyTests
{
    public static void Parse()
    {
        Check.Eq(App.Hotkeys.Parse("ctrl+alt+a"), (Native.Win32.MOD_CONTROL | Native.Win32.MOD_ALT, (uint)'A'), "ctrl+alt+a");
        Check.Eq(App.Hotkeys.Parse("alt+space"), (Native.Win32.MOD_ALT, 0x20u), "alt+space");
        Check.True(App.Hotkeys.Parse("off") is null, "off");
        Check.True(App.Hotkeys.Parse("a") is null, "a key needs a modifier");
        Check.Eq(App.Hotkeys.Label("ctrl+alt+w"), "Ctrl+Alt+W", "label");
    }
}
