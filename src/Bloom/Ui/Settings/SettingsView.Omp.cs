using System.IO;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 › AI 채팅 › omp: where omp is, the chat's and the quick jobs' model and thinking level, web search; 내 모델 is
/// SettingsView.OmpModels.cs. Without omp on this PC the page says so and asks for its path, and shows nothing else.
internal sealed partial class SettingsView
{
    private const string OmpHome = "https://github.com/can1357/oh-my-pi";

    private void PageOmp(Settings s, string filter)
    {
        Header("omp");
        string? exe = collected is null ? OmpCli.FindCli(s.OmpPath) : null; // collecting looks nowhere on the PC
        if (exe is not null && Demo.On) exe = Tr("설치됨", "Installed"); // recording mode: no path with the account name
        Add("omp", exe is null ? AiErrors.OmpMissing + Tr(" 설치 후 omp login 으로 로그인하세요.", " Install it and sign in with omp login.") : exe + Tr(" · 로그인해 둔 모델로 답합니다", " · answers with the models you signed in to"),
            exe is null ? ToolUi.TextButton(Tr("설치 안내", "Installation guide"), () => Launch.Open(OmpHome)) : null, G.Info, null, null, filter);
        Field(Tr("omp 경로", "omp path"), Tr("비워 두면 PATH에서 찾습니다", "Empty = looked up on the PATH"), G.Folder, s.OmpPath, "C:\\...\\omp.exe", v => s.OmpPath = v.Length == 0 ? null : v, filter,
            v => File.Exists(v) ? null : Tr("그 파일이 없습니다", "No such file"));
        if (exe is null && collected is null) return; // nothing to choose from until omp is there
        if (collected is null) OmpCatalog.Ensure(); // the list from `omp models` arrives once (OnCatalog builds the page again)
        PageOmpModels(s, filter);
        string chat = s.OmpChatModel, fast = s.OmpFastModel;
        string chatId = AiModels.Omp(chat), fastId = AiModels.Omp(fast);
        Header(Tr("채팅", "Chat"));
        Menu(Tr("채팅 모델", "Chat model"), Tr("새 대화의 기본값 · 비우면 omp의 기본 모델 · 대화 중에는 Ctrl+M", "For new chats · empty = omp's own default · Ctrl+M during a chat"), G.Model,
            collected is null ? ListOptions(chat) : [], chat, v =>
            {
                s.OmpChatModel = v;
                Dispatcher.BeginInvoke(() => Build(lastFilter)); // the levels follow the model
            }, filter);
        Field(Tr("채팅 모델 직접 입력", "Type a chat model"), Tr("내 모델에 없는 모델: 제공자/모델 · 비우면 omp의 기본 모델", "A model not in My models: provider/model · empty = omp's own default"), G.Keyboard, chat.Length > 0 ? chat : null, "provider/model",
            v => s.OmpChatModel = v, filter, v => v.Contains('/') ? null : Tr("제공자/모델 모양이어야 합니다", "Use provider/model"));
        if (AiModels.HasEffort(chatId))
            Choice(Tr("채팅 생각 수준", "Chat thinking level"), Tr("모델마다 단계가 다릅니다 · 대화 중 Ctrl+E", "The levels depend on the model · Ctrl+E during a chat"), G.Bulb,
                AiModels.Efforts(chatId).Select(e => (e, AiModels.EffortName(e))).ToArray(), AiModels.Fit(chatId, s.OmpChatEffort), v => s.OmpChatEffort = v, filter);
        var searchSw = Toggle(s.OmpSearch, v =>
        {
            s.OmpSearch = v;
            host.Chats.EndAll(); // omp reads its tools when the session starts
        }, out var searchT);
        Add(Tr("채팅에서 웹 검색", "Web search in chats"), Tr("omp의 파일 읽기 · 쓰기와 명령 실행은 막아 둡니다 · Bloom의 PC 작업과 켜 둔 MCP 서버는 설정 › PC 작업 · MCP", "omp's own file access and commands are blocked · Bloom's PC actions and the MCP servers you switched on: Settings › PC actions · MCP"), searchSw, G.Globe, searchT, null, filter);
        Header(Tr("빠른 작업", "Quick jobs"));
        Menu(Tr("빠른 작업 모델", "Quick-job model"), Tr("번역 · AI 빠른 동작 · 가벼울수록 빠릅니다 · 비우면 omp의 기본 모델", "Translation and AI actions · lighter is faster · empty = omp's own default"), G.Speed,
            collected is null ? ListOptions(fast) : [], fast, v =>
            {
                s.OmpFastModel = v;
                Dispatcher.BeginInvoke(() => Build(lastFilter));
            }, filter);
        Field(Tr("빠른 작업 모델 직접 입력", "Type a quick-job model"), Tr("내 모델에 없는 모델: 제공자/모델", "A model not in My models: provider/model"), G.Keyboard, fast.Length > 0 ? fast : null, "provider/model",
            v => s.OmpFastModel = v, filter, v => v.Contains('/') ? null : Tr("제공자/모델 모양이어야 합니다", "Use provider/model"));
        if (AiModels.HasEffort(fastId))
            Choice(Tr("빠른 작업 생각 수준", "Quick-job thinking level"), Tr("끔이 가장 빠르고 사용량이 적습니다 · 번역 창의 칩으로도 바꿉니다", "Off is fastest and uses least · also a chip in the translation window"), G.Bulb,
                AiModels.Efforts(fastId).Select(e => (e, AiModels.EffortName(e))).ToArray(), AiModels.Fit(fastId, s.OmpFastEffort), v => s.OmpFastEffort = v, filter);
    }

    /// 설정 › PC 작업 · MCP: omp chats get Bloom's PC tool too. omp can't ask on its own, so Bloom asks in the chat right before each action.
    private void OmpToolsRow(Settings s, string filter)
    {
        if (collected is null && OmpCli.FindCli(s.OmpPath) is null) return; // no omp here: nothing to switch
        var sw = Toggle(s.AiToolsOmp, v =>
        {
            s.AiToolsOmp = v;
            host.Chats.EndAll(); // the next message starts its session with (or without) the tools
        }, out var t);
        Add(Tr("omp 대화에서", "In omp chats"), Tr("실행 전 AI 채팅에 승인 카드 표시 · 다음 메시지부터 적용", "Approval card in AI chat before actions · applies from the next message"), sw, G.Bolt, t, null, filter);
    }
}
