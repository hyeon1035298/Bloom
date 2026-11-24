using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Search;

namespace Bloom.App;

/// 앱 설치: winget's catalogue (winget + Microsoft Store) searched from the launcher; Enter asks, then installs in the
/// background and a card says how it went. "설치 vlc" / "vlc 설치" / "install vlc" in the root search leads here.
internal sealed partial class Engine
{

    private string? wingetAsked; // the words whose answer is in wingetFound, or on its way
    private List<WingetPackage>? wingetFound; // null: still searching
    private bool wingetFailed;
    private CancellationTokenSource? wingetStop;
    private readonly HashSet<string> installing = new();

    [GeneratedRegex(@"^\s*(?:(?:설치|install|winget|앱\s*설치)\s+(?<q>\S.*?)|(?<q>\S.*?)\s+(?:설치|설치하기|install))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WingetLine();

    public List<Section> WingetView(string text)
    {
        string q = text.Trim();
        var s = new Section(Tr("앱 설치", "Install Apps"), "winget");
        if (Winget.Exe is null)
        {
            s.Items.Add(new ResultItem
            {
                Id = "winget:missing",
                Kind = ItemKind.Info,
                Title = Tr("winget이 없습니다", "winget is not installed"),
                Subtitle = Tr("Microsoft Store의 ‘앱 설치 관리자’에 들어 있습니다 · Enter로 열기", "Included with “App Installer” in Microsoft Store · Press Enter to open"),
                Icon = IconSpec.Of(G.Info),
                Run = () => Launch.Open("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1"),
            });
            return new List<Section> { s };
        }
        if (q.Length < 2)
        {
            s.Items.Add(WingetInfo("winget:hint", Tr("설치할 앱 이름을 입력하세요", "Enter an app name to install"), Tr("winget과 Microsoft Store 목록에서 검색합니다 · Enter로 설치", "Search winget and Microsoft Store · Press Enter to install")));
            return new List<Section> { s };
        }
        if (q != wingetAsked) AskWinget(q);
        if (wingetFound is null) s.Items.Add(WingetInfo("winget:busy", Tr($"‘{q}’ 찾는 중…", $"Searching for ‘{q}’…"), Tr("앱 목록 조회 중 · 몇 초 소요", "Searching app catalog · Takes a few seconds")));
        else if (wingetFailed) s.Items.Add(WingetInfo("winget:failed", Tr("winget 응답 없음 · 잠시 후 다시 검색하세요", "winget is not responding · Try again in a moment"), Tr("잠시 후 다시 입력해 보세요", "Try typing again in a moment")));
        else if (wingetFound.Count == 0) s.Items.Add(WingetInfo("winget:none", Tr("검색된 앱 없음", "No matching apps"), Tr("영어 이름이나 회사 이름으로도 찾아 보세요", "Try searching by English name or publisher")));
        else
        {
            s.Note = Tr($"{wingetFound.Count}개 · winget", $"{wingetFound.Count} · winget");
            foreach (var p in wingetFound) s.Items.Add(WingetItem(p));
        }
        return new List<Section> { s };
    }

    private static ResultItem WingetInfo(string id, string title, string sub) => new() { Id = id, Kind = ItemKind.Info, Title = title, Subtitle = sub, Icon = IconSpec.Of(G.Download) };

    /// One search at a time: the one before is stopped (and its winget ended) as the words change.
    private void AskWinget(string q)
    {
        wingetStop?.Cancel();
        wingetStop?.Dispose();
        var stop = wingetStop = new CancellationTokenSource();
        wingetAsked = q;
        wingetFound = null;
        wingetFailed = false;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, stop.Token); // still typing: wait for a pause
                var found = await Winget.Search(q, 20, stop.Token);
                _ = host.Window.Dispatcher.BeginInvoke(() =>
                {
                    if (wingetAsked != q) return;
                    wingetFound = found ?? new();
                    wingetFailed = found is null;
                    if (host.Window.IsVisible && host.Window.Mode == Mode.Winget) host.Window.Refresh(true);
                });
            }
            catch (OperationCanceledException)
            {
                // the words changed
            }
        });
    }

    private ResultItem WingetItem(WingetPackage p) => new()
    {
        Id = "winget:" + p.Id,
        Kind = ItemKind.Command,
        Title = p.Name,
        Subtitle = Join(p.Id, p.Version is "Unknown" or "" ? null : p.Version),
        Accessory = p.Source == "msstore" ? "Microsoft Store" : "winget",
        Icon = IconSpec.Of(G.Download),
        PrimaryLabel = Tr("설치", "Install"),
        StayOpen = true,
        Run = () => host.Window.Confirm(Tr($"‘{Clip(p.Name, 28)}’ 설치 시 패키지 사용 조건에 동의합니다.", $"By installing ‘{Clip(p.Name, 28)}’, you agree to the package license terms."), new ActionItem
        {
            Title = Tr("설치", "Install"),
            Keys = "Enter",
            Glyph = G.Download,
            Run = () => InstallPackage(p),
        }),
        MoreActions = () =>
        {
            var list = new List<ActionItem>
            {
                new() { Title = Tr("터미널에서 설치 (진행 보기)", "Install in terminal (show progress)"), Glyph = G.Code, Run = () => Bloom.Ai.ChildProcess.Spawn(UserCommands.StartInfo(Winget.CommandLine(p), true))?.Dispose() },
                new() { Title = Tr("ID 복사", "Copy ID"), Keys = "Ctrl+Shift+C", Glyph = G.Copy, Run = () => Launch.Copy(p.Id) },
                new() { Title = Tr("설치 명령 복사", "Copy install command"), Glyph = G.Copy, Run = () => Launch.Copy(Winget.CommandLine(p)) },
            };
            if (p.Source == "msstore")
                list.Add(new ActionItem { Title = Tr("Microsoft Store에서 보기", "View in Microsoft Store"), Glyph = G.NewWindow, Run = () => Launch.Open("ms-windows-store://pdp/?ProductId=" + p.Id) });
            return list;
        },
    };

    private void InstallPackage(WingetPackage p)
    {
        if (!installing.Add(p.Id))
        {
            host.Window.ShowToast(Tr($"‘{Clip(p.Name, 20)}’ 설치 중", $"Installing ‘{Clip(p.Name, 20)}’"));
            return;
        }
        host.Window.ShowToast(Tr($"‘{Clip(p.Name, 20)}’ 설치 시작 · 완료 시 알림", $"Started installing ‘{Clip(p.Name, 20)}’ · You’ll be notified when done"));
        _ = Task.Run(async () =>
        {
            var (ok, said) = await Winget.Install(p);
            _ = host.Window.Dispatcher.BeginInvoke(() =>
            {
                installing.Remove(p.Id);
                Notices.Result(ok, ok ? Tr("앱 설치 · 완료", "App Install · Done") : Tr("앱 설치 · 실패", "App Install · Failed"), p.Name, said);
                if (ok) host.Apps.Rescan(); // Store apps aren't watched: the new one shows in the search now
            });
        });
    }

    /// The root search: "설치 vlc" → a row that looks it up in winget.
    internal Section? RootWinget(string text)
    {
        var m = WingetLine().Match(text);
        if (!m.Success) return null;
        string q = m.Groups["q"].Value.Trim();
        return new Section(Tr("앱 설치", "Install Apps"))
        {
            Items =
            {
                new ResultItem
                {
                    Id = "winget:find",
                    Kind = ItemKind.Command,
                    Title = Tr($"winget에서 ‘{q}’ 찾아 설치", $"Search and install ‘{q}’ via winget"),
                    Subtitle = Tr("winget · Microsoft Store 앱 목록", "winget · Microsoft Store catalog"),
                    Accessory = "winget",
                    Icon = IconSpec.Of(G.Download),
                    PrimaryLabel = Tr("찾기", "Search"),
                    StayOpen = true,
                    Run = () => host.OpenView(Mode.Winget, q),
                },
            },
        };
    }
}
