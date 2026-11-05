using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > AI 계정's API keys: a line per provider with a key (ApiProviders), a click opens its menu (test and refresh
/// the models, change the key, remove it); the ones without a key in one list (API 키 추가…) that asks for the key.
/// The key itself is never shown; the line says only that one is stored.
internal sealed partial class SettingsView
{
    private void ApiKeyRows(string filter)
    {
        Header(Tr("API 키", "API keys"));
        var missing = new List<ApiProvider>();
        foreach (var p in ApiProviders.All)
        {
            if (collected is not null || !ApiKeys.Has(p.Id)) // collecting reads no keys
            {
                missing.Add(p);
                continue;
            }
            int count = ApiCatalog.Models(p.Id).Count;
            string status = ApiCatalog.Refreshing(p.Id) ? Tr("모델 목록 불러오는 중…", "Loading the model list…")
                : ApiCatalog.Failure(p.Id) is { } failure ? failure.Split('\n')[0]
                : Tr($"키 저장됨 · 모델 {count}개 · 이 PC에만 암호화 저장", $"Key saved · {count} models · encrypted on this PC only");
            var provider = p;
            Add(p.Name, status, null, G.Lock, () => ApiKeyMenu(provider), null, filter);
        }
        if (missing.Count == 0) return;
        // the providers with no key in one list: picking one asks for its key (ApiKeyWindow); their names find the line
        string sub = Tr($"키가 없는 제공자: {string.Join(", ", missing.Select(p => p.Name))}", $"Providers with no key: {string.Join(", ", missing.Select(p => p.Name))}");
        if (Record(Tr("API 키 추가…", "Add an API key…"), sub, filter)) return;
        var dd = new Dropdown(missing.Select(p => (p.Id, p.Name, (string?)null)).ToArray(), Tr("제공자 고르기", "Pick a provider"));
        dd.Picked += id =>
        {
            if (missing.Find(p => p.Id == id) is { } p) EnterApiKey(p);
            Dispatcher.BeginInvoke(() => Build(lastFilter)); // the list shows its prompt again (or the new key's line)
        };
        dropdowns.Add(dd);
        Add(Tr("API 키 추가…", "Add an API key…"), sub, dd, G.Add, dd.Open, null, filter);
    }

    /// No key yet: the key entry. Else: test (the model list asked again), another key, or no key.
    private void ApiKeyMenu(ApiProvider p)
    {
        var owner = Window.GetWindow(this);
        if (!ApiKeys.Has(p.Id))
        {
            EnterApiKey(p);
            return;
        }
        switch (ConfirmWindow.Ask(owner, p.Name, Tr($"이 PC에만 암호화하여 저장된 키 · {p.Name}에 보내는 요청에만 사용", $"Stored encrypted on this PC only · used only for requests to {p.Name}"),
            (Tr("연결 테스트 · 모델 새로 고침", "Test · refresh models"), MessageBoxResult.OK),
            (Tr("키 바꾸기", "Change key"), MessageBoxResult.Yes),
            (Tr("키 삭제", "Remove key"), MessageBoxResult.No),
            (Tr("닫기", "Close"), MessageBoxResult.Cancel)))
        {
            case MessageBoxResult.OK:
                _ = RefreshApiModels(p);
                break;
            case MessageBoxResult.Yes:
                EnterApiKey(p);
                break;
            case MessageBoxResult.No:
                if (ConfirmWindow.Ask(owner, Tr("키 삭제", "Remove key"), Tr($"이 PC에서 {p.Name} API 키와 모델 목록을 삭제합니다.", $"Deletes the {p.Name} API key and model list from this PC."),
                    (Tr("삭제", "Remove"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.No)) != MessageBoxResult.Yes) break;
                ApiCatalog.Forget(p.Id); // at once: a refresh under way publishes nothing
                _ = RemoveApiKey(p);
                break;
        }
    }

    private async Task RemoveApiKey(ApiProvider p)
    {
        await ApiKeys.RemoveAsync(p.Id); // DPAPI and the file off the UI thread
        ApiBalances.Forget(p.Id); // its card on the home goes with it
        if (p.Balance != ApiBalanceWay.None) _ = AiUsage.RefreshBalances();
        Build(lastFilter);
    }

    private void EnterApiKey(ApiProvider p)
    {
        if (ApiKeyWindow.Ask(Window.GetWindow(this), p) is not { } key) return;
        _ = StoreApiKey(p, key);
    }

    private async Task StoreApiKey(ApiProvider p, string key)
    {
        await ApiKeys.SetAsync(p.Id, key); // stored before the look, which reads it
        ApiCatalog.ClearUnusable(p.Id); // models the old key was refused may open to this one
        Build(lastFilter);
        if (p.Balance != ApiBalanceWay.None)
        {
            ApiBalances.Forget(p.Id); // another key's balance is not this one's
            _ = AiUsage.RefreshBalances(); // its card on the home, from the new key (no CLI is asked for it)
        }
        await RefreshApiModels(p);
    }

    /// Asks the provider for its models with the stored key (the key's test) and tells how it went; a look that a newer
    /// one (another key, a removal) overtook says nothing.
    private async Task RefreshApiModels(ApiProvider p)
    {
        var (current, error) = await ApiCatalog.RefreshAsync(p);
        if (!current) return;
        Build(lastFilter);
        int count = ApiCatalog.Models(p.Id).Count;
        ConfirmWindow.Tell(Window.GetWindow(this), p.Name, error ?? Tr($"연결되었습니다. 모델 {count}개를 불러왔습니다.", $"Connected. {count} models loaded."));
    }
}
