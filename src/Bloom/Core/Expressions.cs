using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Search;

namespace Bloom.Core;

/// 쓸 수 있는 표현: what the search line understands, as examples by kind. The list "?" shows (Engine.Guide) and the
/// one example the root's placeholder shows at each opening come from here, and tests\Bloom.Tests feeds every example to
/// the real parsers, so none can stay in the list after the parser stops reading it.
internal static class Expressions
{
    /// Typed alone in the root search, it lists the examples.
    public const string Key = "?";

    /// One thing to type and what it does. The input is Korean in the Korean UI and English in every other language: the
    /// parsers only read those two, so it never goes through Tr (the explanation does). `Hint`: short enough for the
    /// placeholder of the search line.
    internal sealed record Example(string Ko, string En, string Does, bool Hint = false)
    {
        public string Input => Lang.En ? En : Ko;
    }

    /// Examples of one kind. `Feature` is the switch in 설정 > 기능 that turns them off (null: always there); `Id` names
    /// the kind of answer a test expects.
    internal sealed record Kind(string Id, string? Feature, string Title, char Glyph, Example[] Examples);

    /// The emoji tile of 설정 > 도구 (G has no glyph for it).
    private const char EmojiGlyph = '\uE76E';

    /// Every kind in the order of the list, in the language of the run, whether its feature is on or not.
    public static List<Kind> All() => new()
    {
        new("calc", "calc", Tr("계산 · 단위 · 환율", "Math · Units · Currency"), G.Calculator, new Example[]
        {
            new("45000*1.1", "45000*1.1", Tr("식을 적으면 바로 계산합니다", "Type an expression to calculate immediately")),
            new("5km to mi", "5km to mi", Tr("단위를 변환합니다", "Convert units"), true),
            new("100 usd", "100 usd", Tr("최신 환율로 변환합니다", "Convert using live exchange rates")),
        }),
        new("timer", "timers", Tr("타이머 · 알림", "Timers · Reminders"), G.Stopwatch, new Example[]
        {
            new("10분 타이머", "timer 10m", Tr("타이머를 시작합니다", "Start a timer")),
            new("30분 뒤 빨래", "laundry in 30 min", Tr("정한 시간이 지나면 알림을 표시합니다", "Remind you after a set duration"), true),
            new("오후 3시 반에 회의 알림", "remind me at 3pm to call", Tr("정한 시각에 알림을 표시합니다", "Remind you at a specific time")),
        }),
        new("date", "dates", Tr("날짜 · 세계 시간", "Dates · World Clock"), G.Calendar, new Example[]
        {
            new("100일 뒤", "in 100 days", Tr("며칠 뒤의 날짜를 표시합니다", "Find the date N days from now"), true),
            new("크리스마스까지", "days until christmas", Tr("남은 날수를 셉니다", "Count days remaining until a date")),
            new("뉴욕 시간", "new york time", Tr("다른 도시의 시각을 표시합니다", "Show current time in another city")),
            new("도쿄 오후 3시", "tokyo 3pm", Tr("그 도시의 시각을 이 PC의 시각으로 변환합니다", "Convert a city’s time to your local time")),
        }),
        new("layout", "layout", Tr("창 배치", "Window Layout"), G.Screen, new Example[]
        {
            new("왼쪽 절반", "left half", Tr("앞의 창을 화면 왼쪽 절반에 맞춥니다", "Snap the active window to the left half"), true),
            new("최대화", "maximize", Tr("앞의 창을 최대화합니다", "Maximize the active window")),
            new("다음 화면으로", "next screen", Tr("앞의 창을 다음 화면으로 옮깁니다", "Move the active window to the next display")),
        }),
        new("path", null, Tr("경로", "Path"), G.Folder, new Example[]
        {
            new(@"~\Downloads", @"~\Downloads", Tr("폴더와 안의 항목을 표시합니다", "Browse folders and their contents")),
            new("%APPDATA%", "%APPDATA%", Tr("환경 변수가 든 경로도 읽습니다", "Supports paths with environment variables")),
        }),
        new("quicklink", "quicklinks", Tr("사이트 바로 가기", "Site Shortcuts"), G.Link, new Example[]
        {
            new("yt 고양이", "yt cats", Tr("YouTube에서 검색합니다", "Search on YouTube"), true),
            new("map 성수역", "map central park", Tr("지도에서 검색합니다", "Search on Maps")),
        }),
        new("emoji", "emoji", Tr("이모지 · 기호", "Emoji · Symbols"), EmojiGlyph, new Example[]
        {
            new(":하트", ":heart", Tr("이모지를 찾아 붙여넣습니다", "Find and paste emojis"), true),
            new(":화살표", ":arrow", Tr("기호도 찾아 붙여넣습니다", "Find and paste symbols")),
        }),
        new("install", "winget", Tr("앱 설치", "Install Apps"), G.Download, new Example[]
        {
            new("설치 vlc", "install vlc", Tr("winget에서 찾아 설치합니다", "Search and install via winget"), true),
        }),
        new("system", "system", Tr("시스템", "System"), G.Volume, new Example[]
        {
            new("볼륨 30", "volume 30", Tr("볼륨을 맞춥니다", "Set the system volume"), true),
            new("다크 모드", "dark mode", Tr("다크 모드를 켜고 끕니다", "Toggle Dark Mode")),
        }),
        new("note", "notes", Tr("메모", "Notes"), G.Document, new Example[]
        {
            new("메모 우유 사기", "note buy milk", Tr("바로 메모로 저장합니다", "Quickly save as a note"), true),
        }),
        new("ai", "chat", "AI (Tab)", G.Sparkle, new Example[]
        {
            new("파이썬 리스트 정렬하는 법", "how to sort a list in python", Tr("Tab을 누르면 입력한 내용을 AI에게 묻습니다", "Press Tab to ask AI what you typed")),
        }),
    };

    /// The kinds whose feature is on.
    public static List<Kind> On() => All().Where(k => k.Feature is null || Features.On(k.Feature)).ToList();

    private static readonly int FirstHint = Random.Shared.Next(64);
    private static (int opening, string code, string? text) hinted = (-1, "", null);

    /// The example after the placeholder at the `opening`-th opening of the launcher: one of the short ones whose
    /// feature is on, the next at each opening (from a different start each run); null when none is on. The placeholder
    /// is asked for at every selection, so the answer is kept for the opening.
    public static string? Hint(int opening)
    {
        if (hinted.opening == opening && hinted.code == Lang.Code) return hinted.text;
        var hints = On().SelectMany(k => k.Examples).Where(e => e.Hint).ToList();
        string? text = hints.Count == 0 ? null : hints[(FirstHint + opening) % hints.Count].Input;
        hinted = (opening, Lang.Code, text);
        return text;
    }
}
