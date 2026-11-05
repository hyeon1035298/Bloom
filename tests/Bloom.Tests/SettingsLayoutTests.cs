using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.App;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.Tests;

/// The settings window's pages after the twelve-page layout: every line the fourteen pages had is still somewhere (by its
/// title, or the title it became), except the ones that went on purpose. Collected the way the launcher's search does
/// (SettingsView in collect mode), with the settings that bring conditional lines out.
internal static class SettingsLayoutTests
{
    /// The lines of the fourteen-page layout, as collected with the default settings.
    private static readonly string[] Before =
    {
        "언어 · Language", "Win 키로 열기", "Alt + Space로 열기", "윈도우 시작 시 실행", "테마", "색", "직접 고른 색", "크기", "런처 창 크기",
        "아이콘 스타일", "그림 아이콘 만들기", "AI로 전부 그리기",
        "AI 채팅", "Bloom Lens", "AI 빠른 동작", "계산기 · 단위 · 환율", "날짜 · 세계 시간", "타이머 · 알림", "캘린더", "컬러 피커", "이모지 · 기호",
        "볼륨 · 시스템 조작", "브라우저 바로 가기", "사용자 명령", "메모장 · 도구 창", "미니 플레이어", "마크다운 문서 (.md)", "기본 앱으로 정하기",
        "창 전환", "창 배치", "앱 설치 (winget)", "프로세스 종료", "Bloom Space Cleaner",
        "애니메이션", "초당 애니메이션 횟수", "유리 효과", "유리 투명도", "GPU로 그리기 (실험적)", "지금 Bloom이 쓰는 메모리",
        "작업표시줄",
        "AI 사용량", "토큰 사용량", "토큰 그래프", "이번 주", "모델별 사용량", "세션별 사용량", "즐겨찾기", "최근 사용", "최근 파일", "최근 복사",
        "최근 AI 채팅", "메모", "플레이어", "명령",
        "새로 받는 간격", "보여 줄 값", "아이콘 색", "사용량 파일 내보내기", "가격표 고치기", "토큰 집계 지우기",
        "파일도 함께 찾기", "Everything", "열린 창도 함께 찾기", "선택한 결과 우선 표시", "검색 학습 기록 지우기", "검색 · 링크를 열 브라우저", "검색 엔진",
        "사이트 바로 가기", "Bloom에서 바꾼 앱 이름",
        "새 대화는", "AI 채팅 창 단축키", "Tab으로 지난 대화 이어가기", "대화 메모리 해제 시간", "AI로 대화 제목 생성",
        "Claude 모델", "Claude 생각 수준", "Claude Code", "ChatGPT 모델", "ChatGPT 생각 수준", "Codex",
        "omp", "omp 경로", "목록이 비어 있습니다", "모델 추가", "직접 추가", "omp 모델 목록 새로고침",
        "채팅 모델", "채팅 모델 직접 입력", "채팅 생각 수준", "채팅에서 웹 검색", "빠른 작업 모델", "빠른 작업 모델 직접 입력", "빠른 작업 생각 수준", "API 모델",
        "Claude 계정 추가…", "ChatGPT 계정 추가…", "OpenAI", "Anthropic", "Gemini", "OpenRouter", "DeepSeek", "xAI", "OpenCode Go",
        "ChatGPT 조회 방식", "조회 간격",
        "Claude 대화에서", "ChatGPT 대화에서", "omp 대화에서", "API 대화에서",
        "파일 읽기 (대화 폴더 밖)", "파일 쓰기", "파일 고치기", "명령 실행", "다른 MCP 도구", "PC 조작 (Bloom 기능)",
        "찾아보기", "열기 · 실행", "메모 · 타이머 · 일정", "파일 정리", "클립보드", "MCP 설정 파일 가져오기…", "켜기 전에", "연결 방식", "쓰는 양",
        "작업 알림", "위치", "직접 옮기기", "보이는 시간", "효과음", "미리 보기",
        "진행 중", "작업 완료", "승인 대기", "알림 카드에서 승인", "한도 임박", "한도 복구",
        "바로 AI에게 묻기", "바로 글자 복사", "바로 번역", "바로 실시간 번역", "바로 캡처", "읽은 글자 바로 복사", "인식 언어",
        "번역 엔진", "번역용 Claude 모델", "번역할 언어", "실시간 번역 간격", "캡처 저장본 폴더 열기", "캡처 저장본 모두 삭제",
        "클립보드 기록", "보관 기간", "단축키", "기록 모두 지우기",
        "창 전환 단축키", "이모지 · 기호 단축키", "AI 빠른 동작 단축키", "계산기", "소리로도 알리기", "타이머 · 알림 열기",
        "컬러 피커 단축키", "찍은 색 바로 복사", "최근 색 · 팔레트 지우기",
        "재생 방식", "JRiver 주소", "JRiver 사용자", "JRiver 비밀번호", "foobar2000 프로그램", "MusicBee 프로그램",
        "데이터 폴더 열기", "설정 내보내기", "Bloom 초기화", "설정 불러오기", "Bloom 삭제",
        "라이선스", "이번 버전에서 바뀐 점", "업데이트 확인", "켤 때 업데이트 확인", "Bloom 둘러보기", "문제 보고",
    };

    /// Lines that took a new title (or went into one line with another).
    private static readonly Dictionary<string, string> Renamed = new()
    {
        ["윈도우 시작 시 실행"] = "Windows 시작 시 실행",
        ["AI로 전부 그리기"] = "AI로 아이콘 그리기",
        ["브라우저 바로 가기"] = "사이트 바로 가기",
        ["검색 · 링크를 열 브라우저"] = "링크를 열 브라우저",
        ["새 대화는"] = "새 대화의 AI",
        ["AI 채팅 창 단축키"] = "AI 채팅 창",
        ["Claude Code"] = "Claude Code 설치 안내",
        ["Codex"] = "Codex 설치 안내",
        ["omp"] = "omp 설치 안내",
        ["채팅 모델"] = "omp 채팅 모델",
        ["채팅 모델 직접 입력"] = "모델 직접 입력",
        ["채팅 생각 수준"] = "omp 채팅 생각 수준",
        ["채팅에서 웹 검색"] = "omp 채팅에서 웹 검색",
        ["빠른 작업 모델"] = "모델",
        ["빠른 작업 모델 직접 입력"] = "모델 직접 입력",
        ["빠른 작업 생각 수준"] = "생각 수준",
        ["API 모델"] = "API 키 추가",
        ["번역용 Claude 모델"] = "모델",
        ["OpenAI"] = "API 키 추가…",
        ["Anthropic"] = "API 키 추가…",
        ["Gemini"] = "API 키 추가…",
        ["OpenRouter"] = "API 키 추가…",
        ["DeepSeek"] = "API 키 추가…",
        ["xAI"] = "API 키 추가…",
        ["OpenCode Go"] = "API 키 추가…",
        ["Claude 대화에서"] = "사용할 대화",
        ["ChatGPT 대화에서"] = "사용할 대화",
        ["omp 대화에서"] = "사용할 대화",
        ["API 대화에서"] = "사용할 대화",
        ["창 전환 단축키"] = "창 전환",
        ["이모지 · 기호 단축키"] = "이모지 · 기호",
        ["AI 빠른 동작 단축키"] = "AI 빠른 동작",
        ["컬러 피커 단축키"] = "컬러 피커",
        ["단축키"] = "클립보드 기록",
        ["소리로도 알리기"] = "끝날 때 소리",
    };

    /// Lines that went: the ones the 데이터 page takes, the three dropped (도구 창, 타이머 · 알림 열기, 연결 방식), and the
    /// ones that became a line under another or a group's note or button.
    private static readonly HashSet<string> Gone = new()
    {
        "토큰 집계 지우기", "검색 학습 기록 지우기", "Bloom에서 바꾼 앱 이름", "기록 모두 지우기", "캡처 저장본 폴더 열기", "캡처 저장본 모두 삭제", "최근 색 · 팔레트 지우기",
        "데이터 폴더 열기", "설정 내보내기", "Bloom 초기화", "설정 불러오기", "Bloom 삭제",
        "계산기", "타이머 · 알림 열기", "연결 방식",
        "Everything", "조회 간격", "켜기 전에", "쓰는 양", "미리 보기", "업데이트 확인",
    };

    private static readonly string[] Pages = { "general", "launcher", "features", "shortcuts", "ai", "accounts", "mcp", "notices", "tools", "addons", "about" };

    public static void EveryLineHasAPlace()
    {
        using (new SettingsTransferTests.DataFolder())
        {
            var s = Settings.Current;
            var first = new SettingsView(new Host(new Options())).Collect();
            var lines = new List<SettingsCatalog.Line>(first);
            // the conditional lines: the AI new chats use (its model, its install guide, omp's web search) and the
            // mini player's way (JRiver's lines, a program's path)
            foreach (string ai in new[] { "chatgpt", "omp", "api" })
            {
                s.AiProvider = ai;
                lines.AddRange(new SettingsView(new Host(new Options())).Collect());
            }
            s.AiProvider = "claude";
            foreach (string way in new[] { "jriver", "foobar", "musicbee" })
            {
                s.PlayerMode = way;
                lines.AddRange(new SettingsView(new Host(new Options())).Collect());
            }
            s.PlayerMode = "direct";
            var titles = lines.Select(l => l.Title).ToHashSet();
            foreach (string old in Before)
            {
                if (Gone.Contains(old)) continue;
                string now = Renamed.GetValueOrDefault(old, old);
                Check.True(titles.Contains(now), $"‘{old}’ is on a page (as ‘{now}’)");
            }
            Check.True(lines.Any(l => l.Title.StartsWith("omp 모델 목록 · ", StringComparison.Ordinal)), "omp 모델 목록 · N개");
            Check.True(lines.Any(l => l.PageKey == "about" && l.Title.StartsWith("Bloom ", StringComparison.Ordinal) && l.Group == "버전"), "Bloom {버전} under 버전");
            foreach (string gone in new[] { "타이머 · 알림 열기", "연결 방식", "토큰 집계 지우기", "기록 모두 지우기", "Bloom 삭제" })
                Check.True(!titles.Contains(gone), $"‘{gone}’ is gone from these pages");

            // the pages in the sidebar's order (데이터 is a page of its own, built elsewhere)
            var order = first.Select(l => l.PageKey).Distinct().ToList();
            Check.Eq(string.Join(",", order), string.Join(",", Pages), "the pages in order");

            // where a few lines went
            void At(string title, string page, string group) =>
                Check.True(lines.Any(l => l.Title == title && l.PageKey == page && l.Group == group), $"‘{title}’ in {page} › {group}");
            At("애니메이션", "general", "모양");
            At("GPU로 그리기 (실험적)", "general", "모양");
            At("검색 엔진", "launcher", "웹 검색");
            At("AI 사용량", "launcher", "홈 블록");
            At("바로 캡처", "shortcuts", "Bloom Lens");
            At("AI 채팅 창", "shortcuts", "창");
            At("번역 엔진", "ai", "번역 · AI 빠른 동작");
            At("사용량 파일 내보내기", "accounts", "사용량");
            At("인식 언어", "tools", "Bloom Lens");
            At("재생 방식", "tools", "미니 플레이어");
            At("알림 카드에서 승인", "notices", "알릴 일");
        }
    }

    /// The page keys of the fourteen-page layout (a search line, a notice, a QA list) open their new pages.
    public static void OldPageKeys()
    {
        foreach (var (old, now) in new[] { ("performance", "general"), ("home", "launcher"), ("search", "launcher"), ("clipboard", "tools"), ("screen", "tools"), ("lens", "tools"), ("player", "tools"), ("taskbar", "addons"), ("ai", "ai") })
            Check.Eq(SettingsView.PageOf(old), now, $"‘{old}’ opens");
    }
}
