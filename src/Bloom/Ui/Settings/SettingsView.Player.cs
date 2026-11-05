using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Core;
using Bloom.Music;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 도구 > 미니 플레이어: how it plays (직접 · JRiver · foobar2000 · MusicBee) and what the picked way needs —
/// JRiver's address, user and password (the password encrypted for this Windows user, like the API keys; its lines stay
/// while any JRiver value is saved, so it can be cleared), the programs' paths.
internal sealed partial class SettingsView
{
    private void PlayerGroup(Settings s, string filter)
    {
        Header(Tr("미니 플레이어", "Mini player"), Tr("파일 검색 결과의 오디오 파일에서 Ctrl+K › ‘미니 플레이어로 재생’을 고릅니다. 음악 파일은 읽기만 합니다.", "Ctrl+K on an audio file in the search results › ‘Play in mini player’. Music files are only read."));
        if (FeatureOff(filter, "player")) return;
        string way = PlayerWays.Of(s.PlayerMode);
        Choice(Tr("재생 방식", "Playing"), way switch
        {
            PlayerWays.Jriver => Tr("JRiver Media Center의 웹 서비스(MCWS)로 틀고 조작합니다. 대기열은 JRiver의 것입니다.", "Plays and controls through JRiver Media Center's web service (MCWS). The queue is JRiver's."),
            PlayerWays.Foobar => Tr("명령줄로 틀고, Windows 미디어 세션으로 지금 곡을 읽고 조작합니다. 곡을 틀면 foobar2000이 파일을 여는 방식을 따릅니다.", "Plays through its command line; the song and the buttons go through Windows' media session. Playing a song follows foobar2000's own way of opening files."),
            PlayerWays.MusicBee => Tr("명령줄로 틀고 조작합니다. 지금 곡은 창 제목으로 읽습니다.", "Plays and controls through its command line. The song is read from its window title."),
            _ => Tr("Bloom이 직접 재생합니다. flac, mp3, wav, m4a, aac, wma 파일을 엽니다.", "Bloom plays it itself. Opens flac, mp3, wav, m4a, aac and wma files."),
        }, G.Plug, PlayerWays.All.Select(w => (w.key, w.name)).ToArray(), way, v =>
        {
            s.PlayerMode = v;
            MusicPlayer.ModeChanged();
            Dispatcher.BeginInvoke(() => Build(lastFilter)); // the lines of the picked way
        }, filter);

        // JRiver's lines: while it is the way, or while a JRiver value is saved (its password can be cleared here)
        bool password = collected is null && ApiKeys.Has(MusicPlayer.JriverKey);
        if (way == PlayerWays.Jriver || s.JriverAddress.Length > 0 || s.JriverUser.Length > 0 || password)
        {
            string address = string.IsNullOrWhiteSpace(s.JriverAddress) ? Mcws.DefaultAddress : s.JriverAddress;
            Add(Tr("JRiver 주소", "JRiver address"), Tr($"{address} · JRiver의 Media Network(웹 서비스)가 켜져 있어야 합니다 · 클릭하여 바꾸기", $"{address} · JRiver's Media Network (the web service) must be on · click to change"), null, G.Link, () => EditJriver("address"), null, filter);
            Add(Tr("JRiver 사용자", "JRiver user"), s.JriverUser.Length == 0 ? Tr("없음 · JRiver에 사용자 이름과 비밀번호를 정했을 때만 필요 · 클릭하여 입력", "None · only needed when JRiver was given a user name and password · click to enter") : Tr($"{s.JriverUser} · 클릭하여 바꾸기", $"{s.JriverUser} · click to change"), null, G.Lock, () => EditJriver("user"), null, filter);
            string passwordState = collected is not null ? "" : password ? Tr("저장됨 · 이 PC에만 암호화 · JRiver에 보내는 요청에만 사용 · 클릭하여 바꾸기", "Saved · encrypted on this PC only · used only for requests to JRiver · click to change") : Tr("없음 · 클릭하여 입력", "None · click to enter");
            Add(Tr("JRiver 비밀번호", "JRiver password"), passwordState, null, G.Lock, () => EditJriver("password"), null, filter);
        }
        if (way == PlayerWays.Foobar) ProgramRow(PlayerWays.Foobar, "foobar2000", s.FoobarPath, filter);
        if (way == PlayerWays.MusicBee) ProgramRow(PlayerWays.MusicBee, "MusicBee", s.MusicBeePath, filter);
    }

    private void ProgramRow(string way, string name, string path, string filter)
    {
        string found = collected is not null || path.Length > 0 ? "" : MusicPlayer.FindExe(way) ?? "";
        string sub = path.Length > 0 ? Tr($"{path} · 클릭하여 바꾸기", $"{path} · click to change")
            : found.Length > 0 ? Tr($"자동으로 찾음: {found} · 다른 곳에 있다면 클릭하여 경로 입력", $"Found by itself: {found} · click if it is somewhere else")
            : Tr("찾지 못함 · 클릭하여 .exe 경로 입력", "Not found · click to enter the .exe path");
        Add(Tr($"{name} 프로그램", $"{name} program"), sub, null, G.Folder, () =>
        {
            string? v = ValueWindow.Ask(Window.GetWindow(this), Tr($"{name} 프로그램 경로", $"{name} program path"), Tr($"{name}.exe의 전체 경로입니다. 비워 두면 Bloom이 실행 중인 것이나 보통의 설치 폴더에서 찾습니다.", $"The full path of {name}.exe. Left empty, Bloom looks at the running one and the usual install folders."), path, false, path.Length > 0, p => p.Length == 0 || (Path.IsPathFullyQualified(p.Trim('"')) && File.Exists(p.Trim('"')) && p.Trim('"').EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase)) ? null : Tr("있는 .exe 파일의 전체 경로여야 합니다", "Use the full path of an .exe file that exists"));
            if (v is null) return;
            if (way == PlayerWays.Foobar) Settings.Current.FoobarPath = v.Trim('"');
            else Settings.Current.MusicBeePath = v.Trim('"');
            Settings.Save();
            Build(lastFilter);
        }, null, filter);
    }

    /// JRiver's address, user or password, typed in a ValueWindow; the player drops what it knew of JRiver.
    private void EditJriver(string what)
    {
        var s = Settings.Current;
        var owner = Window.GetWindow(this);
        switch (what)
        {
            case "address":
                {
                    string? v = ValueWindow.Ask(owner, Tr("JRiver 주소", "JRiver address"), Tr($"JRiver가 실행되는 PC의 주소입니다 (기본 {Mcws.DefaultAddress}). JRiver의 Media Network를 켜야 웹 서비스(MCWS)가 열립니다.", $"The address of the PC where JRiver runs (default {Mcws.DefaultAddress}). Turn on JRiver's Media Network to open its web service (MCWS)."), s.JriverAddress, false, s.JriverAddress.Length > 0,
                        a => a.Length == 0 || Mcws.Base(a) is not null ? null : Tr("http://주소:포트 형식으로 적으세요", "Write it as http://address:port"));
                    if (v is null) return;
                    s.JriverAddress = v;
                    break;
                }
            case "user":
                {
                    string? v = ValueWindow.Ask(owner, Tr("JRiver 사용자", "JRiver user"), Tr("JRiver에 사용자 이름과 비밀번호를 정했을 때만 적습니다. 없으면 비워 두세요.", "Only when JRiver was given a user name and password. Leave it empty if not."), s.JriverUser, false, s.JriverUser.Length > 0);
                    if (v is null) return;
                    s.JriverUser = v;
                    break;
                }
            default:
                {
                    string? v = ValueWindow.Ask(owner, Tr("JRiver 비밀번호", "JRiver password"), Tr("이 PC에만 암호화하여 저장하며 JRiver에 보내는 요청에만 사용합니다. 입력하는 동안 보이지 않습니다.", "Stored encrypted on this PC only and used only for requests to JRiver. It is not shown while typed."), "", true, ApiKeys.Has(MusicPlayer.JriverKey));
                    if (v is null) return;
                    _ = StoreJriverPassword(v);
                    return;
                }
        }
        Settings.Save();
        MusicPlayer.JriverChanged();
        Build(lastFilter);
    }

    private async Task StoreJriverPassword(string password)
    {
        if (password.Length == 0) await ApiKeys.RemoveAsync(MusicPlayer.JriverKey);
        else await ApiKeys.SetAsync(MusicPlayer.JriverKey, password); // DPAPI and the file off the UI thread
        MusicPlayer.JriverChanged();
        Build(lastFilter);
    }
}
