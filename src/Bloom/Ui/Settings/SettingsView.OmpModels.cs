using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 › AI 채팅 › omp › 내 모델: the list the chat's model menu uses (add from `omp models` by provider or by hand,
/// rename, a starting thinking level per model, order, remove) and the button that reads `omp models` again.
internal sealed partial class SettingsView
{
    private bool addOpen; // the add screen is open
    private string addQuery = ""; // what is searched in it
    private string? addProvider; // the provider whose models are listed (when nothing is searched)
    private string? renaming; // the listed model whose name is typed under its row
    private bool refreshing;
    private string refreshNote = "";

    /// The models a picker offers: omp's own default, the models of the menus (OmpModelList.Slugs), and `keep` when it is
    /// not among them (a model typed in by hand).
    private static (string value, string label, string? sub)[] ListOptions(string keep)
    {
        var slugs = OmpModelList.Slugs();
        var list = new List<(string value, string label, string? sub)> { ("", OmpCli.ShortName(""), Tr("omp에 설정된 기본 모델로 답합니다", "Answers with the model omp is set to use")) };
        list.AddRange(slugs.Select(slug => (slug, OmpModelList.NameOf(slug), (string?)slug)));
        if (keep.Length > 0 && !slugs.Contains(keep)) list.Insert(1, (keep, OmpModelList.NameOf(keep), keep));
        return list.ToArray();
    }

    private void PageOmpModels(Settings s, string filter)
    {
        Header(Tr("내 모델", "My models"));
        var items = OmpModelList.Items.ToList();
        foreach (var m in items)
        {
            string slug = m.Slug;
            bool gone = OmpModelList.Gone(slug);
            string sub = slug + (gone ? Tr(" · omp 목록에 없음 (그대로 둠)", " · not in omp's list now (kept)") : "");
            var ctl = new StackPanel { Orientation = Orientation.Horizontal };
            var levels = OmpCatalog.LevelsOf(slug);
            if (levels.Length > 1)
            {
                var opts = new[] { ("", Tr("기본 단계", "Default level"), (string?)null) }.Concat(levels.Select(l => (l, AiModels.EffortName(l), (string?)null))).ToArray();
                var dd = new Dropdown(opts, m.Effort is { Length: > 0 } e && levels.Contains(e) ? e : "") { Margin = new Thickness(8, 0, 0, 0), ToolTip = Tr("이 모델로 새 대화를 시작할 때의 생각 수준", "The thinking level a chat with this model starts at") };
                dd.Picked += v => OmpModelList.SetEffort(slug, v);
                dropdowns.Add(dd);
                ctl.Children.Add(dd);
            }
            ctl.Children.Add(Arrow(G.Up, Tr("위로", "Up"), () => Moved(slug, -1)));
            ctl.Children.Add(Arrow(G.Down, Tr("아래로", "Down"), () => Moved(slug, 1)));
            ctl.Children.Add(Arrow(G.Close, Tr("목록에서 빼기", "Remove from the list"), () =>
            {
                OmpModelList.Remove(slug);
                Dispatcher.BeginInvoke(() => Build(lastFilter));
            }));
            Add(OmpModelList.NameOf(slug), sub, ctl, gone ? G.Warning : G.Model, () =>
            {
                renaming = renaming == slug ? null : slug; // Enter / a click: the name can be typed
                Dispatcher.BeginInvoke(() => Build(lastFilter));
            }, null, filter);
            if (renaming == slug)
                Field(Tr("표시 이름", "Display name"), Tr($"메뉴에 보이는 이름 · 비우면 기본 이름 ({OmpCli.ShortName(slug)})", $"The name in the menus · empty = the usual one ({OmpCli.ShortName(slug)})"), G.Tag, m.Name, OmpCli.ShortName(slug), v =>
                {
                    OmpModelList.Rename(slug, v);
                    renaming = null;
                }, filter);
        }
        if (items.Count == 0)
            Add(Tr("목록이 비어 있습니다", "The list is empty"), Tr("모델을 추가하기 전까지 채팅창 모델 메뉴에는 omp 기본 모델만 표시됩니다", "Until you add one, the chat's model menu shows only omp's default model"), null, G.Info, null, null, filter);

        Add(addOpen ? Tr("추가 화면 닫기", "Close the add screen") : Tr("모델 추가", "Add a model"),
            Tr("omp가 아는 모델을 제공자별로 찾아 골라요", "Pick from the models omp knows, by provider"), null, G.Add, () =>
            {
                addOpen = !addOpen;
                addQuery = "";
                addProvider = null;
                Dispatcher.BeginInvoke(() => Build(lastFilter));
            }, null, filter);
        Field(Tr("직접 추가", "Add by hand"), Tr("제공자/모델 모양으로 적고 Enter (예: anthropic/claude-sonnet-5-5)", "provider/model, then Enter (e.g. anthropic/claude-sonnet-5-5)"), G.Keyboard, null, "provider/model",
            v =>
            {
                if (v.Length > 0) OmpModelList.Add(v);
            }, filter, v => v.Contains('/') ? OmpModelList.Contains(v.Trim()) ? Tr("이미 목록에 있습니다", "Already in the list") : null : Tr("제공자/모델 모양이어야 해요", "Use provider/model"));
        string when = OmpCatalog.Read is { } t ? t.ToString("yyyy-MM-dd HH:mm") : Tr("아직 읽지 않음", "not read yet");
        Add(refreshing ? Tr("불러오는 중…", "Loading…") : Tr("omp 모델 목록 새로고침", "Refresh omp's model list"),
            Tr($"omp models를 지금 다시 읽습니다 · {OmpCatalog.All.Count}개 · 마지막 {when}", $"Reads omp models again now · {OmpCatalog.All.Count} models · last {when}") + refreshNote, null, G.Refresh, () =>
            {
                if (refreshing) return;
                refreshing = true;
                refreshNote = "";
                Dispatcher.BeginInvoke(() => Build(lastFilter));
                _ = OmpCatalog.Refresh(ok => Dispatcher.BeginInvoke(() =>
                {
                    refreshing = false;
                    refreshNote = ok ? "" : Tr(" · 읽지 못했습니다 — omp를 확인하세요", " · could not read — check omp");
                    Build(lastFilter);
                }));
            }, null, filter);
        if (addOpen) AddScreen(filter);
    }

    private void Moved(string slug, int d)
    {
        OmpModelList.Move(slug, d);
        Dispatcher.BeginInvoke(() => Build(lastFilter));
    }

    /// Search and pick from the catalog; nothing searched = the providers in a list (one opens to its models), else the
    /// matches grouped under their provider.
    private void AddScreen(string filter)
    {
        var all = OmpCatalog.All;
        Header(Tr("추가할 모델", "Model to add"));
        Field(Tr("찾기", "Search"), Tr("글자로 찾습니다 (제공자 이름도 가능) · 비우면 제공자별로 표시합니다", "Matches model and provider names · empty shows the providers"), G.Search, addQuery, Tr("예: gemini", "e.g. gemini"), v =>
        {
            addQuery = v;
            addProvider = null;
        }, filter);
        if (all.Count == 0)
        {
            Add(Tr("모델 목록이 아직 없습니다", "No model list yet"), Tr("위의 ‘omp 모델 목록 새로고침’을 누르세요", "Press ‘Refresh omp's model list’ above"), null, G.Info, null, null, filter);
            return;
        }
        var words = addQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            Header(Tr("제공자", "Providers"));
            foreach (var g in all.GroupBy(e => e.Provider).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                string provider = g.Key;
                bool open = addProvider == provider;
                Add(provider, Tr($"{g.Count()}개 모델", $"{g.Count()} models"), null, open ? G.Up : G.Down, () =>
                {
                    addProvider = open ? null : provider;
                    Dispatcher.BeginInvoke(() => Build(lastFilter));
                }, null, filter);
                if (open) ModelRows(g, 80, filter);
            }
            return;
        }
        int shown = 0;
        foreach (var g in all.Where(e => words.All(w => e.Slug.Contains(w, StringComparison.OrdinalIgnoreCase))).GroupBy(e => e.Provider).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Header(g.Key + " · " + g.Count());
            ModelRows(g, 30, filter);
            shown += g.Count();
        }
        if (shown == 0) Add(Tr("찾은 모델이 없습니다", "No model found"), Tr("‘직접 추가’에 제공자/모델을 적어도 됩니다", "You can also type provider/model in ‘Add by hand’"), null, G.Info, null, null, filter);
    }

    private void ModelRows(IEnumerable<OmpCatalog.Entry> models, int cap, string filter)
    {
        var list = models.ToList();
        foreach (var e in list.Take(cap))
        {
            string slug = e.Slug;
            bool have = OmpModelList.Contains(slug);
            string levels = e.Thinking.Length == 0 ? Tr("생각 단계 없음", "no thinking") : string.Join(" · ", e.Thinking.Select(AiModels.EffortName));
            Add(OmpCli.ShortName(slug), slug + " · " + levels + (have ? Tr(" · 목록에 있음", " · in the list") : ""), null, have ? G.Check : G.Add, () =>
            {
                if (have) OmpModelList.Remove(slug);
                else OmpModelList.Add(slug);
                Dispatcher.BeginInvoke(() => Build(lastFilter));
            }, null, filter);
        }
        if (list.Count > cap)
            Add(Tr($"…외 {list.Count - cap}개", $"…and {list.Count - cap} more"), Tr("위 찾기에 글자를 넣어 좁히세요", "Narrow it with the search above"), null, G.Info, null, null, filter);
    }
}
