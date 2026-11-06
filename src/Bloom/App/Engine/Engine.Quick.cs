using System.Collections.Generic;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// AI 빠른 동작: the actions for the text (or the user's own words), then the answer and what to do with it.
/// The preview pane shows the text before, the answer after.
internal sealed partial class Engine
{
    public List<Section> QuickView(string text)
    {
        var st = host.Quick;
        var sections = new List<Section>();
        if (st.Source.Length == 0 && !st.NoSource)
        {
            sections.Add(new Section(null)
            {
                Items =
                {
                    new ResultItem
                    {
                        Id = "quick:none",
                        Kind = ItemKind.Info,
                        Title = Tr("선택·복사된 글 없음", "No selected or copied text"),
                        Subtitle = Tr($"앱에서 글을 선택하고 {Hotkeys.Label(Settings.Current.QuickHotkey)}를 누르세요. 또는 글을 복사한 뒤 다시 여세요.", $"Select text in any app and press {Hotkeys.Label(Settings.Current.QuickHotkey)}, or copy it and come back"),
                        Icon = IconSpec.Of(G.Info),
                    },
                },
            });
            return sections;
        }
        if (st.Busy)
        {
            sections.Add(new Section(Tr("하는 중", "Working"))
            {
                Items =
                {
                    new ResultItem
                    {
                        Id = "quick:busy",
                        Kind = ItemKind.Quick,
                        Title = st.Progress is { } step ? Tr($"‘{st.ActionTitle}’ 하는 중… {step}단계", $"‘{st.ActionTitle}’ in progress… step {step}") : Tr($"‘{st.ActionTitle}’ 하는 중…", $"‘{st.ActionTitle}’ in progress…"),
                        Subtitle = RunsOn(st),
                        Icon = IconSpec.Of(G.Sparkle, true),
                        PrimaryLabel = Tr("취소", "Cancel"),
                        StayOpen = true,
                        Run = host.CancelQuick,
                        Detail = SourceDetail,
                    },
                },
            });
            return sections;
        }
        if (st.Result is not null || st.Error is not null)
        {
            var done = new Section(st.Error is null ? Tr($"‘{st.ActionTitle}’ 결과", $"‘{st.ActionTitle}’ result") : Tr("실패", "Failed"));
            if (st.Result is { } result)
            {
                Detail Answer()
                {
                    var d = new Detail { Text = result };
                    d.Add(Tr("동작", "Action"), st.ActionTitle);
                    d.Add(Tr("길이", "Length"), Tr($"{st.Source.Length}자 → {result.Length}자", $"{st.Source.Length} → {result.Length} chars"));
                    return d;
                }
                done.Items.Add(QuickRow("quick:paste", st.FromSelection ? Tr("선택한 글을 이걸로 바꾸기", "Replace the selection") : Tr("앞의 창에 붙여넣기", "Paste into the window"), st.FromSelection ? Tr("원래 앱에 붙여넣기 (Ctrl+V)", "Pastes back into the app (Ctrl+V)") : null, G.Paste, host.PasteQuick, Answer, false));
                done.Items.Add(QuickRow("quick:copy", Tr("복사", "Copy"), null, G.Copy, () => { Launch.Copy(result); host.Window.ShowToast(Tr("복사 완료", "Copied")); }, Answer, true));
                done.Items.Add(QuickRow("quick:chat", Tr("채팅으로 이어 가기", "Continue in a chat"), null, G.Chat, host.QuickToChat, Answer, true));
                done.Items.Add(QuickRow("quick:again", Tr("다시 하기", "Try again"), null, G.Refresh, () => host.RunQuick(st.Action ?? "summarize", st.Custom), Answer, true));
                done.Items.Add(QuickRow("quick:back", Tr("다른 동작 고르기", "Pick another action"), null, G.Back, host.CancelQuick, Answer, true));
            }
            else
            {
                done.Items.Add(QuickRow("quick:error", st.Error ?? "", Tr("Enter로 다시 하기", "Enter tries again"), G.Info, () => host.RunQuick(st.Action ?? "summarize", st.Custom), SourceDetail, true));
                done.Items.Add(QuickRow("quick:back", Tr("다른 동작 고르기", "Pick another action"), null, G.Back, host.CancelQuick, SourceDetail, true));
            }
            sections.Add(done);
            return sections;
        }

        // the actions, filtered by what is typed; anything else typed becomes the user's own request
        var q = new Query(text);
        var list = new Section(Tr("동작", "Actions"), Tr("결과는 오른쪽에 · 원래 글은 그대로", "The answer shows on the right"));
        if (!q.IsEmpty && !QuickActions.Known(text))
        {
            string ask = text.Trim();
            sections.Add(new Section(null)
            {
                Items = { QuickRow("quick:custom", Tr($"“{ask}”", $"“{ask}”"), Tr("AI 작업 지시 입력", "Enter an AI instruction"), G.Sparkle, () => host.RunQuick("custom", ask), SourceDetail, true) },
            });
        }
        foreach (var a in QuickActions.All)
        {
            if (!q.IsEmpty && Matcher.Score(q, QuickActions.Match(a)) < 560) continue;
            string key = a.Key;
            string? sub = key == "translate" ? Tr($"{Translator.TargetName(Translator.TargetFor(st.Source, Settings.Current.TranslateTo))}로", $"into {Translator.TargetName(Translator.TargetFor(st.Source, Settings.Current.TranslateTo))}") : null;
            list.Items.Add(QuickRow("quick:" + key, QuickActions.Title(a), sub, a.Glyph, () => host.RunQuick(key), SourceDetail, true));
        }
        // 내 AI 명령 that work on a text, above the built-in ones: the user made them for this
        var mine = new Section(Tr("내 AI 명령", "My AI commands"), AiCommandRun.Who());
        foreach (var c in Features.On("mine") ? UserCommands.Current.Items : new())
        {
            if (!c.IsAi || c.Input == AiInput.None || (!q.IsEmpty && Matcher.Score(q, c.Match) <= 0)) continue;
            var command = c;
            mine.Items.Add(QuickRow("quick:my:" + c.Id, c.Name, string.Join("  →  ", AiCommandRun.Split(c.Body)), G.Sparkle, () => host.RunMineOn(command), MineDetail, true));
        }
        if (mine.Items.Count > 0) sections.Add(mine);
        if (list.Items.Count > 0) sections.Add(list);
        return sections;
    }

    private Detail SourceDetail() => SourceDetail(RunsOn(host.Quick));

    /// A 내 AI 명령's row: it runs on the chat's model, whatever ran before.
    private Detail MineDetail() => SourceDetail(AiCommandRun.Who());

    private Detail SourceDetail(string engine)
    {
        var st = host.Quick;
        var d = new Detail { Text = st.Source };
        d.Add(Tr("가져온 곳", "From"), st.FromSelection ? Tr("선택한 글", "The selection") : Tr("클립보드", "The clipboard"));
        d.Add(Tr("길이", "Length"), Tr($"{st.Source.Length}자", $"{st.Source.Length} chars"));
        d.Add(Tr("엔진", "Engine"), engine);
        return d;
    }

    /// The model a run uses: the chat's for 내 AI 명령, the translation engine's for the rest.
    private static string RunsOn(QuickState st) => st.Action == "mine" ? AiCommandRun.Who() : Translator.Who(Translator.Provider());

    private static ResultItem QuickRow(string id, string title, string? sub, char glyph, System.Action run, System.Func<Detail> detail, bool stay) => new()
    {
        Id = id,
        Kind = ItemKind.Quick,
        Title = title,
        Subtitle = sub,
        Icon = IconSpec.Of(glyph, id is "quick:custom"),
        PrimaryLabel = Tr("실행", "Run"),
        StayOpen = stay,
        Run = run,
        Detail = detail,
    };
}
