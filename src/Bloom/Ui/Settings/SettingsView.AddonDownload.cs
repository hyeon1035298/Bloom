using System;
using Bloom.Addons;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 애드온: an add-on's download row, under its line (SettingsView.Addons). The taskbar add-on and the others come
/// from their repositories' latest GitHub releases (AddonCatalog, AddonLatest, AddonDownload); the installer carries none
/// of them.
internal sealed partial class SettingsView
{
    /// Under an add-on's line: 받기 while this Bloom doesn't have it, 업데이트 when its latest release is newer than the
    /// version here, how far a look or a download is, why one failed (with 다시 받기), and while it is failing, a way to
    /// report it in its repository. `name`: the add-on's name in the rows. Not while the launcher's search collects. Works
    /// in a dev or QA copy too (into its own data folder): its switch still starts nothing there.
    private void AddonDownload(Addon addon, string name, AddonStatus status, string filter)
    {
        if (collected is not null || addon.Catalog is not { } catalog) return;
        var fetch = addon.Fetch;
        void Get()
        {
            _ = AddonHub.Update(addon);
            Build(lastFilter);
        }
        if (fetch.Busy)
        {
            long done = fetch.Done, total = fetch.Total;
            if (total < 0 && done == 0)
                Add(Tr($"{name} 새 버전 확인 중", $"Looking for a new {name}"), Tr("GitHub 릴리스", "GitHub release"), null, G.Download, null, null, filter);
            else
                Add(Tr($"{name} 받는 중", $"Downloading {name}"), total > 0 ? $"{fetch.Percent}% · {Engine.Size(done)} / {Engine.Size(total)}" : Engine.Size(done),
                    null, G.Download, null, null, filter);
            return;
        }
        var offer = addon.Offer();
        string get = Tr("받기", "Download");
        // a failed look while the version here is the latest is said nowhere: it is tried again tomorrow
        if (fetch.Failed is { } why && (offer is not null || addon.Manifest is null))
            Add(Tr($"{name} 받기 실패", $"Couldn't download {name}"), FailureText(why), RowButton(Tr("다시 받기", "Try again"), Get), G.Warning, Get, null, filter);
        else if (offer is { } e && addon.Manifest is { } now && !addon.Broken)
            Add(Tr($"{name} {e.Version} 업데이트", $"{name} {e.Version} update"), Tr($"지금 {now.Version} · GitHub 릴리스에서 받습니다", $"Now {now.Version} · from its GitHub release"),
                RowButton($"{Tr("업데이트", "Update")} · {Engine.Size(e.Size)}", Get), G.Download, Get, null, filter);
        else if (offer is { } first)
            Add(Tr($"{name} {first.Version} 받기", $"Download {name} {first.Version}"), Tr("GitHub 릴리스에서 받아 Bloom이 확인한 뒤 설치합니다", "From its GitHub release, checked by Bloom before it is installed"),
                RowButton($"{get} · {Engine.Size(first.Size)}", Get), G.Download, Get, null, filter);
        else if (addon.Manifest is null)
            Add(Tr($"{name} 받기", $"Download {name}"), Tr("GitHub 릴리스에서 받아 Bloom이 확인한 뒤 설치합니다", "From its GitHub release, checked by Bloom before it is installed"),
                RowButton(get, Get), G.Download, Get, null, filter);
        if (addon.Manifest is { } m && status.State is AddonState.SafeMode or AddonState.Stopped or AddonState.Broken or AddonState.PortCrashed)
        {
            void Report() => Launch.Open(IssueUrl(catalog, m.Version, status.State));
            Add(Tr("GitHub에 문제 알리기", "Report it on GitHub"), Tr("이 애드온 저장소에 이슈를 엽니다 · 버전과 상태가 미리 채워집니다", "Opens an issue in this add-on's repository, with its version and status filled in"),
                RowButton(Tr("이슈 열기", "Open an issue"), Report), G.Chat, Report, null, filter);
        }
    }

    /// A new issue in the add-on's repository, its title and body filled in with the add-on's id, version and status,
    /// Bloom's version and Windows' (nothing else: no path, name or log), for the user to finish and send on GitHub.
    internal static string IssueUrl(AddonCatalog.Entry catalog, string version, AddonState state)
    {
        string title = $"{catalog.Id} {version}: {state}";
        string body = $"Add-on: {catalog.Id} {version}\nStatus in Bloom: {state}\nBloom: {Addon.Bloom}\nWindows: {Environment.OSVersion.Version}\n\nWhat happened:\n";
        return $"{catalog.Page}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }

    private static string FailureText(AddonFailure why) => why switch
    {
        AddonFailure.Network => Tr("인터넷 연결을 확인하고 다시 받으세요", "Check your connection and try again"),
        AddonFailure.Missing => Tr("아직 받을 수 있는 릴리스가 없습니다", "There is no release to download yet"),
        AddonFailure.Hash => Tr("받은 파일이 릴리스가 알린 파일과 달라 설치하지 않았습니다", "The downloaded file isn't the one its release names, so it wasn't installed"),
        AddonFailure.Content => Tr("받은 파일 안의 내용이 올바르지 않아 설치하지 않았습니다", "What the download holds isn't valid, so it wasn't installed"),
        _ => Tr("받은 파일을 저장하지 못했습니다 · 디스크 공간을 확인하세요", "Couldn't save the download · check the free disk space"),
    };
}
