using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Core;
using Bloom.Music;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > 도구 > 미니 플레이어: how it plays (직접 · JRiver · foobar2000 · MusicBee) and what each of the others needs —
/// JRiver's address, user and password (the password encrypted for this Windows user, like the API keys), the programs' paths.
internal sealed partial class SettingsView
{
    private void PagePlayer(Settings s, string filter)
    {
        Page("player", Tr("미니 플레이어", "Mini player"), PlayerGlyph.Music, Tr("파일 검색 결과의 오디오 파일에서 Ctrl+K › ‘미니 플레이어로 재생’ · 런처 옆 작은 창이 지금 곡과 대기열을 보여 줘요 — 음악 파일은 읽기만 해요", "Ctrl+K on an audio file in the search results › ‘Play in mini player’ · a small window beside the launcher shows the song and the queue — music files are only read"), filter);
        Header(Tr("재생 방식", "How it plays"));
        Choice(Tr("재생 방식", "Playing"), s.PlayerMode switch
        {
            PlayerWays.Jriver => Tr("JRiver — JRiver Media Center의 웹 서비스(MCWS)로 틀고 조작해요 · 대기열은 JRiver 것", "JRiver — plays and controls through JRiver Media Center's web service (MCWS) · the queue is JRiver's"),
            PlayerWays.Foobar => Tr("foobar2000 — 명령줄로 틀고 Windows 미디어 세션으로 지금 곡을 읽고 조작해요 · 곡을 틀면 foobar2000이 파일을 여는 방식(설정에 따라 활성 재생목록 교체)을 따라요", "foobar2000 — plays through its command line, the song and the buttons through Windows' media session · playing a song follows foobar2000's own way of opening files (it may replace the active playlist)"),
            PlayerWays.MusicBee => Tr("MusicBee — 명령줄로 틀고 조작해요 · 지금 곡은 창 제목으로 읽어요 (재생 중인지는 알려 주지 않아 Bloom이 보낸 명령으로 짐작해요)", "MusicBee — plays and controls through its command line · the song is read from its window title (it does not say whether it plays, so Bloom goes by the commands it sent)"),
            _ => Tr("직접 — Bloom이 파일을 읽기만 해서 틀어요 (flac · mp3 · wav · m4a · aac · wma) · 대기열은 Bloom 안 · 다음 곡 자동 재생 · 없는 파일은 건너뛰어요 · ogg · opus는 못 틀어요", "Direct — Bloom opens the file read-only (flac, mp3, wav, m4a, aac, wma) · the queue is Bloom's own · the next song follows · missing files are skipped · ogg and opus cannot be played"),
        }, G.Plug, PlayerWays.All.Select(w => (w.key, w.name)).ToArray(), PlayerWays.Of(s.PlayerMode), v =>
        {
            s.PlayerMode = v;
            MusicPlayer.ModeChanged();
            Dispatcher.BeginInvoke(() => Build(lastFilter));
        }, filter);

        Header("JRiver");
        string address = string.IsNullOrWhiteSpace(s.JriverAddress) ? Mcws.DefaultAddress : s.JriverAddress;
        Add(Tr("JRiver 주소", "JRiver address"), Tr($"{address} · JRiver의 Media Network(웹 서비스)가 켜져 있어야 해요 · 클릭하여 바꾸기", $"{address} · JRiver's Media Network (the web service) must be on · click to change"), null, G.Link, () => EditJriver("address"), null, filter);
        Add(Tr("JRiver 사용자", "JRiver user"), s.JriverUser.Length == 0 ? Tr("없음 — JRiver에 사용자 이름과 비밀번호를 정해 두었을 때만 필요해요 · 클릭하여 입력", "None — only needed when JRiver was given a user name and password · click to enter") : Tr($"{s.JriverUser} · 클릭하여 바꾸기", $"{s.JriverUser} · click to change"), null, G.Lock, () => EditJriver("user"), null, filter);
        string passwordState = collected is not null ? "" : ApiKeys.Has(MusicPlayer.JriverKey) ? Tr("저장됨 · 이 PC에만 암호화 · JRiver에 보내는 요청에만 사용 · 클릭하여 바꾸기", "Saved · encrypted on this PC only · used only for requests to JRiver · click to change") : Tr("없음 · 클릭하여 입력", "None · click to enter");
        Add(Tr("JRiver 비밀번호", "JRiver password"), passwordState, null, G.Lock, () => EditJriver("password"), null, filter);

        Header("foobar2000 · MusicBee");
        ProgramRow(PlayerWays.Foobar, "foobar2000", s.FoobarPath, filter);
        ProgramRow(PlayerWays.MusicBee, "MusicBee", s.MusicBeePath, filter);
    }

    private void ProgramRow(string way, string name, string path, string filter)
    {
        string found = collected is not null || path.Length > 0 ? "" : MusicPlayer.FindExe(way) ?? "";
        string sub = path.Length > 0 ? Tr($"{path} · 클릭하여 바꾸기", $"{path} · click to change")
            : found.Length > 0 ? Tr($"자동으로 찾았어요: {found} · 다른 곳에 있다면 클릭하여 알려 주기", $"Found by itself: {found} · click if it is somewhere else")
            : Tr("찾지 못했어요 — 클릭하여 .exe 경로 입력", "Not found — click to enter the .exe path");
        Add(Tr($"{name} 프로그램", $"{name} program"), sub, null, G.Folder, () =>
        {
            string? v = ValueWindow.Ask(Window.GetWindow(this), Tr($"{name} 프로그램 경로", $"{name} program path"), Tr($"{name}.exe의 전체 경로예요. 비워 두면 Bloom이 실행 중인 것이나 보통의 설치 폴더에서 찾아요.", $"The full path of {name}.exe. Left empty, Bloom looks at the running one and the usual install folders."), path, false, path.Length > 0, p => p.Length == 0 || (Path.IsPathFullyQualified(p.Trim('"')) && File.Exists(p.Trim('"')) && p.Trim('"').EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase)) ? null : Tr("있는 .exe 파일의 전체 경로여야 해요", "Use the full path of an .exe file that exists"));
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
                string? v = ValueWindow.Ask(owner, Tr("JRiver 주소", "JRiver address"), Tr($"JRiver가 도는 PC의 주소예요 (기본 {Mcws.DefaultAddress}). JRiver의 Media Network를 켜야 웹 서비스(MCWS)가 열려요.", $"The address of the PC where JRiver runs (default {Mcws.DefaultAddress}). Turn on JRiver's Media Network to open its web service (MCWS)."), s.JriverAddress, false, s.JriverAddress.Length > 0,
                    a => a.Length == 0 || Mcws.Base(a) is not null ? null : Tr("http://주소:포트 형식으로 적어 주세요", "Write it as http://address:port"));
                if (v is null) return;
                s.JriverAddress = v;
                break;
            }
            case "user":
            {
                string? v = ValueWindow.Ask(owner, Tr("JRiver 사용자", "JRiver user"), Tr("JRiver에 사용자 이름과 비밀번호를 정해 두었을 때만 적어요. 없으면 비워 두세요.", "Only when JRiver was given a user name and password. Leave it empty if not."), s.JriverUser, false, s.JriverUser.Length > 0);
                if (v is null) return;
                s.JriverUser = v;
                break;
            }
            default:
            {
                string? v = ValueWindow.Ask(owner, Tr("JRiver 비밀번호", "JRiver password"), Tr("이 PC에만 암호화하여 저장하며 JRiver에 보내는 요청에만 사용해요. 보이지 않게 입력돼요.", "Stored encrypted on this PC only and used only for requests to JRiver. It is not shown while typed."), "", true, ApiKeys.Has(MusicPlayer.JriverKey));
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
