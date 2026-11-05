using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 › AI 채팅's omp lines: the chat's model and thinking level (under 기본 while new chats use omp, else folded),
/// the quick jobs' model (번역 · AI 빠른 동작), web search, and under 다른 AI와 고급 where omp is and its model list
/// (SettingsView.OmpModels.cs).
internal sealed partial class SettingsView
{
    private const string OmpHome = "https://github.com/can1357/oh-my-pi";

    /// The model menus' last choice: a model typed by hand in a field that opens under the menu.
    private const string TypeModel = "\u0001type";

    private string? ompTyping; // the menu whose field is open: "chat" or "fast"
    private bool ompListOpen; // omp 모델 목록 · N개 is open

    /// An omp model from the list (OmpModelList) or, last, 직접 입력… which opens a field under it for provider/model.
    private void OmpModelPick(string which, string title, string sub, string value, Action<string> set, string filter)
    {
        var options = collected is null ? ListOptions(value).Append((TypeModel, Tr("직접 입력…", "Type a model…"), Tr("목록에 없는 모델을 제공자/모델로 적습니다", "A model not in the list, as provider/model"))).ToArray() : [];
        Menu(title, sub, G.Model, options, value, v =>
        {
            if (v == TypeModel) ompTyping = which;
            else
            {
                set(v);
                ompTyping = null;
            }
            Dispatcher.BeginInvoke(() => Build(lastFilter)); // the levels follow the model; the field opens or closes
        }, filter);
        if (ompTyping != which && collected is null) return;
        Field(Tr("모델 직접 입력", "Type a model"), Tr("omp 모델 목록에 없는 모델: 제공자/모델 · 비우면 omp의 기본 모델", "A model not in the omp model list: provider/model · empty = omp's own default"), G.Keyboard, value.Length > 0 ? value : null, "provider/model",
            v =>
            {
                set(v);
                ompTyping = null;
            }, filter, v => v.Length == 0 || v.Contains('/') ? null : Tr("제공자/모델 모양이어야 합니다", "Use provider/model"));
    }

    /// The chat's model and level: 모델 and 생각 수준 under 기본 while new chats use omp (`primary`), else with omp's name.
    private void OmpChatRows(Settings s, bool primary, List<(bool, Action)> list, string filter)
    {
        string chatId = AiModels.Omp(s.OmpChatModel);
        list.Add((s.OmpChatModel != Defaults.OmpChatModel, () =>
            OmpModelPick("chat", primary ? Tr("모델", "Model") : Tr("omp 채팅 모델", "omp chat model"), Tr("새 대화의 기본값 · 비우면 omp의 기본 모델 · 대화 중에는 Ctrl+M", "For new chats · empty = omp's own default · Ctrl+M during a chat"),
                s.OmpChatModel, v => s.OmpChatModel = v, filter)));
        if (AiModels.HasEffort(chatId))
            list.Add((s.OmpChatEffort != Defaults.OmpChatEffort, () =>
                Choice(primary ? Tr("생각 수준", "Thinking level") : Tr("omp 채팅 생각 수준", "omp chat thinking level"), Tr("모델마다 단계가 다릅니다 · 대화 중 Ctrl+E", "The levels depend on the model · Ctrl+E during a chat"), G.Bulb,
                    AiModels.Efforts(chatId).Select(e => (e, AiModels.EffortName(e))).ToArray(), AiModels.Fit(chatId, s.OmpChatEffort), v => s.OmpChatEffort = v, filter)));
    }

    /// omp's chats may search the web; what they may do on the PC is Bloom's (설정 › PC 작업 · MCP).
    private void OmpSearchRow(Settings s, string filter)
    {
        var searchSw = Toggle(s.OmpSearch, v =>
        {
            s.OmpSearch = v;
            host.Chats.EndAll(); // omp reads its tools when the session starts
        }, out var searchT);
        Add(Tr("omp 채팅에서 웹 검색", "Web search in omp chats"), Tr("omp의 파일 읽기와 쓰기, 명령 실행은 막습니다. Bloom의 PC 작업과 켠 MCP 서버는 PC 작업 · MCP에서 정합니다.", "omp's own file reading, writing and commands are blocked. Bloom's PC actions and the MCP servers switched on are set in PC actions · MCP."), searchSw, G.Globe, searchT, null, filter);
    }

    /// Under 다른 AI와 고급: where omp is (or where to get it), its path, and omp 모델 목록 · N개, whose Enter opens the
    /// list's editor under it.
    private void OmpMore(Settings s, string? exe, List<(bool, Action)> more, string filter)
    {
        if (exe is not null)
            more.Add((false, () => Add("omp", (Demo.On ? Tr("설치됨", "Installed") : exe) + Tr(" · 로그인해 둔 모델로 답합니다", " · answers with the models you signed in to"), null, G.Info, null, null, filter))); // recording mode: no path with the account name
        else
            more.Add((false, () => Add(Tr("omp 설치 안내", "omp installation guide"), AiErrors.OmpMissing + Tr(" 설치 후 omp login으로 로그인하세요.", " Install it and sign in with omp login."), null, G.Info, () => Launch.Open(OmpHome), null, filter)));
        more.Add((s.OmpPath is not null, () =>
            Field(Tr("omp 경로", "omp path"), Tr("비워 두면 PATH에서 찾습니다", "Empty = looked up on the PATH"), G.Folder, s.OmpPath, "C:\\...\\omp.exe", v => s.OmpPath = v.Length == 0 ? null : v, filter,
                v => File.Exists(v) ? null : Tr("그 파일이 없습니다", "No such file"))));
        if (exe is null && collected is null) return; // nothing to list until omp is there
        more.Add((false, () =>
        {
            int n = OmpModelList.Items.Count;
            Add(Tr($"omp 모델 목록 · {n}개", $"omp model list · {n}"), ompListOpen ? Tr("Enter로 닫기", "Enter closes it") : Tr("모델 메뉴에 보이는 omp 모델 · Enter로 편집", "The omp models in the model menus · Enter edits them"), null, G.Model, () =>
            {
                ompListOpen = !ompListOpen;
                Dispatcher.BeginInvoke(() => Build(lastFilter));
            }, null, filter);
            if (ompListOpen || collected is not null || filter.Length > 0) PageOmpModels(s, filter);
        }));
    }
}
