using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// 시스템: switches that act at once (dark mode, mute, Bluetooth …), each showing where it stands now.
/// They are found by name in the root search too ("다크", "mute", "볼륨 30").
internal sealed partial class Engine
{
    private sealed record SysToggle(string Id, string Ko, string En, char Glyph, string Words)
    {
        public MatchTarget Match { get; } = MatchTarget.Named(Ko, En, Words);
    }

    private static readonly SysToggle[] Toggles =
    {
        new("sys:dark", "다크 모드", "Dark Mode", '', "dark mode light theme 다크 모드 라이트 테마 어두운 밝은 야간"),
        new("sys:mute", "소리 끄기", "Mute", '', "mute sound speaker audio 음소거 소리 끄기 스피커 무음"),
        new("sys:volume", "볼륨", "Volume", '', "volume sound louder quieter 볼륨 소리 크게 작게 음량"),
        new("sys:mic", "마이크 끄기", "Mute mic", '', "microphone mic mute 마이크 음소거 끄기"),
        new("sys:output", "소리 출력 장치", "Audio Output Device", '', "output device speaker headphones switch audio 출력 장치 스피커 헤드폰 이어폰 바꾸기"),
        new("sys:bluetooth", "블루투스", "Bluetooth", '', "bluetooth bt 블루투스 무선"),
        new("sys:wifi", "Wi-Fi", "Wi-Fi", '', "wifi wi-fi wireless 와이파이 무선 인터넷"),
        new("sys:hidden", "숨긴 파일 보기", "Show Hidden Files", '', "hidden files explorer show 숨김 숨긴 파일 보기 탐색기"),
        new("sys:ext", "파일 확장자 보기", "Show File Extensions", '', "file extensions explorer show 확장자 보기 탐색기"),
        new("sys:sleep", "절전", "Sleep", '', "sleep suspend 절전 잠자기 수면"),
        new("sys:display", "화면 끄기", "Turn Off Display", '', "display off monitor screen 화면 끄기 모니터 끄기"),
        new("sys:recycle", "휴지통 비우기", "Empty Recycle Bin", G.Delete, "empty recycle bin trash 휴지통 비우기 삭제"),
    };

    private static bool? bluetooth, wifi;
    private static long radiosAt; // when they were read last (0 = not yet)
    private static int radiosBusy;

    public List<Section> SystemView(string text)
    {
        RefreshRadios();
        var q = new Query(text);
        var sections = new List<Section>();
        if (VolumeAsk(text) is { } set) sections.Add(new Section(null) { Items = { set } });
        var s = new Section(Tr("시스템", "System"));
        foreach (var t in Toggles)
        {
            if (!q.IsEmpty && Matcher.Score(q, t.Match) < 560) continue;
            if (SysItem(t.Id) is { } item) s.Items.Add(item);
        }
        if (s.Items.Count > 0) sections.Add(s);
        if (sections.Count == 0)
            sections.Add(new Section(null) { Items = { new ResultItem { Id = "sys:none", Kind = ItemKind.Info, Title = Tr("검색 결과 없음", "No results found"), Icon = IconSpec.Of(G.Info) } } });
        return sections;
    }

    /// The root search: the switches whose name matches, scored like commands.
    internal IEnumerable<ResultItem> RootToggles(Query q)
    {
        foreach (var t in Toggles)
        {
            int sc = Matcher.Score(q, t.Match);
            if (sc < 560 || SysItem(t.Id) is not { } item) continue;
            item.Score = sc + Usage.Boost(t.Id);
            yield return item;
        }
    }

    // "볼륨 30", "소리 30%", "volume 30"
    [GeneratedRegex(@"^\s*(?:볼륨|소리|음량|volume|vol)\s*(\d{1,3})\s*%?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeLine();

    private ResultItem? VolumeAsk(string text)
    {
        var m = VolumeLine().Match(text);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out int v) || v > 100) return null;
        return new ResultItem
        {
            Id = "sys:setvolume",
            Kind = ItemKind.Toggle,
            Title = Tr($"볼륨을 {v}%로", $"Set volume to {v}%"),
            Subtitle = SystemControl.Volume is { } now ? Tr($"지금 {now}%", $"Currently {now}%") : Tr("출력 장치 없음", "No output device"),
            Icon = IconSpec.Of(''),
            PrimaryLabel = Tr("바꾸기", "Change"),
            Run = () => Done(SystemControl.SetVolume(v), Tr($"볼륨 {v}%", $"Volume {v}%")),
        };
    }

    /// The root's volume line ("볼륨 30") as its own section.
    internal Section? RootVolume(string text) => VolumeAsk(text) is { } item ? new Section(Tr("시스템", "System")) { Items = { item } } : null;

    private ResultItem? SysItem(string id)
    {
        var t = Array.Find(Toggles, x => x.Id == id);
        if (t is null) return null;
        var item = new ResultItem
        {
            Id = id,
            Kind = ItemKind.Toggle,
            Title = Tr(t.Ko, t.En),
            Icon = IconSpec.Of(t.Glyph),
            StayOpen = true,
            AsFavorite = new Favorites.Entry { Id = id, Kind = "sys", Title = Tr(t.Ko, t.En) },
        };
        void Switch(bool? now, Func<bool, bool> set, string onWord, string offWord)
        {
            item.Accessory = now is null ? Tr("없음", "None") : now.Value ? Tr("켜짐", "On") : Tr("꺼짐", "Off");
            item.PrimaryLabel = now == true ? offWord : onWord;
            if (now is null) return;
            item.Run = () =>
            {
                Usage.Touch(id);
                Done(set(!now.Value), Switched(t, !now.Value));
            };
        }
        switch (id)
        {
            case "sys:dark":
                bool dark = SystemControl.DarkMode;
                item.Subtitle = Tr("Windows 앱과 작업 표시줄의 색", "Color theme for Windows apps and taskbar");
                Switch(dark, SystemControl.SetDarkMode, Tr("다크로", "Switch to Dark"), Tr("라이트로", "Switch to Light"));
                break;
            case "sys:mute":
                var muted = SystemControl.Muted(false);
                item.Subtitle = SystemControl.Volume is { } vol ? Tr($"볼륨 {vol}%", $"Volume {vol}%") : null;
                Switch(muted, m => SystemControl.SetMuted(false, m), Tr("소리 끄기", "Mute"), Tr("소리 켜기", "Unmute"));
                item.Accessory = muted is null ? Tr("없음", "None") : muted.Value ? Tr("음소거", "Muted") : Tr("소리 켜짐", "Unmuted");
                break;
            case "sys:mic":
                var micMuted = SystemControl.Muted(true);
                item.Subtitle = Tr("기본 마이크", "Default microphone");
                Switch(micMuted, m => SystemControl.SetMuted(true, m), Tr("마이크 끄기", "Mute mic"), Tr("마이크 켜기", "Unmute mic"));
                item.Accessory = micMuted is null ? Tr("없음", "None") : micMuted.Value ? Tr("음소거", "Muted") : Tr("켜짐", "On");
                break;
            case "sys:volume":
                int? v = SystemControl.Volume;
                item.Accessory = v is null ? Tr("없음", "None") : $"{v}%";
                item.Subtitle = Tr("Enter 10% 크게 · Ctrl+K 작게 · \"볼륨 30\"처럼 적어도 됩니다", "Enter +10% · Ctrl+K lower · Or type e.g. \"volume 30\"");
                item.PrimaryLabel = Tr("크게", "Large");
                if (v is { } cur)
                {
                    item.Run = () => Done(SystemControl.SetVolume(Math.Min(100, cur + 10)), Tr($"볼륨 {Math.Min(100, cur + 10)}%", $"Volume {Math.Min(100, cur + 10)}%"));
                    item.MoreActions = () => new List<ActionItem>
                    {
                        new() { Title = Tr("10% 작게", "10% lower"), Glyph = '', StayOpen = true, Run = () => Done(SystemControl.SetVolume(Math.Max(0, cur - 10)), Tr($"볼륨 {Math.Max(0, cur - 10)}%", $"Volume {Math.Max(0, cur - 10)}%")) },
                        new() { Title = Tr("50%로", "Set to 50%"), Glyph = '', StayOpen = true, Run = () => Done(SystemControl.SetVolume(50), Tr("볼륨 50%", "Volume 50%")) },
                    };
                }
                break;
            case "sys:output":
                var outs = SystemControl.Outputs();
                var def = outs.Find(o => o.Default);
                item.Subtitle = def?.Name ?? Tr("출력 장치 없음", "No output device");
                item.Accessory = Tr($"{outs.Count}개", $"{outs.Count}");
                item.PrimaryLabel = Tr("바꾸기", "Change");
                if (outs.Count > 1)
                {
                    // Enter: the next one; Ctrl+K: pick any
                    var next = outs[(outs.IndexOf(def!) + 1) % outs.Count];
                    item.Run = () => Done(SystemControl.SetDefaultOutput(next.Id), Tr($"출력: {next.Name}", $"Output: {next.Name}"));
                    item.MoreActions = () => outs.Select(o => new ActionItem
                    {
                        Title = o.Name,
                        Glyph = o.Default ? G.Check : '',
                        StayOpen = true,
                        Run = () => Done(SystemControl.SetDefaultOutput(o.Id), Tr($"출력: {o.Name}", $"Output: {o.Name}")),
                    }).ToList();
                }
                break;
            case "sys:bluetooth":
            case "sys:wifi":
                int kind = id == "sys:bluetooth" ? SystemControl.Bluetooth : SystemControl.Wifi;
                bool? on = id == "sys:bluetooth" ? bluetooth : wifi;
                item.Subtitle = radiosAt == 0 ? Tr("확인하는 중…", "Checking…") : on is null ? Tr("이 PC에서 사용 불가", "Not available on this PC") : null;
                item.Accessory = on is null ? (radiosAt == 0 ? "…" : Tr("없음", "None")) : on.Value ? Tr("켜짐", "On") : Tr("꺼짐", "Off");
                item.PrimaryLabel = on == true ? Tr("끄기", "Turn off") : Tr("켜기", "Turn On");
                if (on is { } radioOn)
                    item.Run = async () =>
                    {
                        Usage.Touch(id);
                        bool ok = await SystemControl.SetRadio(kind, !radioOn);
                        radiosAt = 0;
                        Done(ok, Switched(t, !radioOn));
                    };
                break;
            case "sys:hidden":
                item.Subtitle = Tr("탐색기 · 열린 창은 F5로 새로고침", "File Explorer · Press F5 to refresh open windows");
                Switch(SystemControl.HiddenFilesShown, SystemControl.SetHiddenFiles, Tr("보이기", "Show"), Tr("숨기기", "Hide"));
                break;
            case "sys:ext":
                item.Subtitle = Tr("탐색기 · 열린 창은 F5로 새로고침", "File Explorer · Press F5 to refresh open windows");
                Switch(SystemControl.ExtensionsShown, SystemControl.SetExtensions, Tr("보이기", "Show"), Tr("숨기기", "Hide"));
                break;
            case "sys:sleep":
                item.Subtitle = Tr("PC를 절전 상태로", "Put PC to sleep");
                item.PrimaryLabel = Tr("절전", "Sleep");
                item.StayOpen = false;
                item.Run = () =>
                {
                    host.SaveAll();
                    _ = System.Threading.Tasks.Task.Delay(400).ContinueWith(_ => SystemControl.Sleep(), System.Threading.Tasks.TaskScheduler.Default);
                };
                break;
            case "sys:display":
                item.Subtitle = Tr("키나 마우스를 움직이면 다시 켜집니다", "Press a key or move the mouse to wake");
                item.PrimaryLabel = Tr("끄기", "Turn off");
                item.StayOpen = false;
                item.Run = SystemControl.DisplayOff;
                break;
            case "sys:recycle":
                // No count on the row: SHQueryRecycleBin walks every item (0.3 s for a few thousand, seconds on a busy PC)
                // and this row is built on the UI thread for nearly any first letter typed in the root search.
                item.Subtitle = Tr("휴지통의 모든 항목 영구 삭제", "Permanently delete all items in the Recycle Bin");
                item.PrimaryLabel = Tr("휴지통 비우기", "Empty Recycle Bin");
                item.Run = () => host.Window.Confirm(Tr("휴지통의 모든 항목을 영구 삭제합니다.", "Permanently delete all items in the Recycle Bin?"), new ActionItem
                {
                    Title = Tr("휴지통 비우기", "Empty Recycle Bin"),
                    Keys = "Enter",
                    Glyph = G.Delete,
                    Danger = true,
                    StayOpen = true,
                    // a full bin takes seconds to empty: off the UI thread
                    Run = async () => Done(await System.Threading.Tasks.Task.Run(SystemControl.EmptyRecycleBin), Tr("휴지통 비우기 완료", "Recycle Bin emptied")),
                });
                break;
        }
        return item;
    }

    /// After a switch: a toast, and the rows show the new state.
    private void Done(bool ok, string what)
    {
        host.Window.ShowToast(ok ? what : Tr("설정을 바꾸지 못했습니다. 다시 시도하세요.", "Could not change the setting. Please try again."));
        host.Window.Refresh(true);
    }

    /// "다크 모드 켬" / "Dark mode on": the switch's name as shown, then where it went.
    private static string Switched(SysToggle t, bool on)
    {
        string name = Tr(t.Ko, t.En);
        return on ? Tr($"{name} 켬", $"{name} turned on") : Tr($"{name} 끔", $"{name} turned off");
    }

    /// The radios answer slowly (WinRT, ~100 ms): read in the background, the list follows when they change.
    private void RefreshRadios()
    {
        if (radiosAt != 0 && Environment.TickCount64 - radiosAt < 3000) return;
        if (System.Threading.Interlocked.Exchange(ref radiosBusy, 1) == 1) return;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            bool? b = await SystemControl.Radio(SystemControl.Bluetooth), w = await SystemControl.Radio(SystemControl.Wifi);
            bool changed = b != bluetooth || w != wifi || radiosAt == 0;
            bluetooth = b;
            wifi = w;
            radiosAt = Environment.TickCount64;
            radiosBusy = 0;
            if (changed) _ = host.Window.Dispatcher.BeginInvoke(() =>
            {
                if (host.Window.IsVisible && host.Window.Mode is Mode.System or Mode.Root) host.Window.Refresh(true);
            });
        });
    }
}
