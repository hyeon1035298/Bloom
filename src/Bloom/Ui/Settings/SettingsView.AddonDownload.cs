using Bloom.Addons;
using Bloom.App;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 애드온: an add-on's download row, under its line (SettingsView.Addons). The taskbar add-on and the others come
/// from the release Bloom's built-in catalog pins (AddonCatalog, AddonDownload); the installer carries none of them.
internal sealed partial class SettingsView
{
    /// Under an add-on's line: 받기 while this Bloom doesn't have it, 업데이트 when the catalog's version is newer than the
    /// downloaded one, how far a download is, why one failed (with 다시 받기), and after an update while the host runs,
    /// that the new version comes with its next start (`switchHint` says how). `name`: the add-on's name in the rows. Not
    /// while the launcher's search collects. Works in a dev or QA copy too (into its own data folder): its switch still
    /// starts nothing there.
    private void AddonDownload(Addon addon, string name, string switchHint, AddonStatus status, string filter)
    {
        if (collected is not null) return;
        var fetch = addon.Fetch;
        if (fetch.Busy)
        {
            long done = fetch.Done, total = fetch.Total;
            Add(Tr($"{name} 받는 중", $"Downloading {name}"), total > 0 ? $"{fetch.Percent}% · {Engine.Size(done)} / {Engine.Size(total)}" : Engine.Size(done),
                null, G.Download, null, null, filter);
            return;
        }
        if (addon.Offer() is { } e)
        {
            void Get()
            {
                _ = AddonHub.Download(addon, e);
                Build(lastFilter);
            }
            string get = Tr("받기", "Download");
            if (fetch.Failed is { } why)
                Add(Tr($"{name} 받기 실패", $"Couldn't download {name}"), FailureText(why), RowButton(Tr("다시 받기", "Try again"), Get), G.Warning, Get, null, filter);
            else if (addon.Manifest is { } now && !addon.Broken)
                Add(Tr($"{name} {e.Version} 업데이트", $"{name} {e.Version} update"), Tr($"지금 {now.Version} · GitHub 릴리스에서 받습니다", $"Now {now.Version} · from its GitHub release"),
                    RowButton(e.Size > 0 ? $"{Tr("업데이트", "Update")} · {Engine.Size(e.Size)}" : Tr("업데이트", "Update"), Get), G.Download, Get, null, filter);
            else
                Add(Tr($"{name} {e.Version} 받기", $"Download {name} {e.Version}"), Tr("GitHub 릴리스에서 받아 Bloom이 확인한 뒤 설치합니다", "From its GitHub release, checked by Bloom before it is installed"),
                    RowButton(e.Size > 0 ? $"{get} · {Engine.Size(e.Size)}" : get, Get), G.Download, Get, null, filter);
            return;
        }
        if (addon.Manifest is { } m && addon.StartedVersion is { } started && started != m.Version && status.State is AddonState.Running or AddonState.Starting)
            Add(Tr($"{name} {m.Version} 받음", $"{name} {m.Version} downloaded"), switchHint, null, G.Check, null, null, filter);
    }

    private static string FailureText(AddonFailure why) => why switch
    {
        AddonFailure.Network => Tr("인터넷 연결을 확인하고 다시 받으세요", "Check your connection and try again"),
        AddonFailure.Hash => Tr("받은 파일이 Bloom이 기대한 파일과 달라 설치하지 않았습니다", "The downloaded file isn't the one Bloom expects, so it wasn't installed"),
        AddonFailure.Content => Tr("받은 파일 안의 내용이 올바르지 않아 설치하지 않았습니다", "What the download holds isn't valid, so it wasn't installed"),
        _ => Tr("받은 파일을 저장하지 못했습니다 · 디스크 공간을 확인하세요", "Couldn't save the download · check the free disk space"),
    };
}
