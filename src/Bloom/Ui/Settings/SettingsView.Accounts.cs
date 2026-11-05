using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Bloom.Ai;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 설정 > AI 계정: the account list and add rows, each account's menu (AI chat pick, hide/show, sign in again, sign out)
/// and an added one's X (sign out), the CLI sign-in, the API keys (SettingsView.ApiKeys), and the usage the home shows
/// (how often, which value, the icons; folded: the ChatGPT checks, the usage file, the prices).
internal sealed partial class SettingsView
{
    private void PageAccounts(Settings s, string filter)
    {
        Page("accounts", Tr("AI 계정", "AI accounts"), G.Account, Tr("Claude · ChatGPT 계정과 API 키, 홈의 사용량", "Claude and ChatGPT accounts, API keys and the usage on the home"), filter);
        foreach (var (kind, title) in new[] { ("claude", "Claude"), ("chatgpt", "ChatGPT") })
        {
            Header(title, kind == "claude" ? Tr("한도에 닿아도 계정은 자동으로 바뀌지 않습니다", "Nothing switches accounts when a limit is reached") : null);
            // the accounts are the user's data, not settings lines: collecting skips them (and leaves AiAccounts alone)
            string chatId = collected is null ? AiAccounts.Chat(kind).Id : "";
            foreach (var a in collected is null ? AiAccounts.All.Where(a => a.Kind == kind).ToList() : [])
            {
                string status = UsageLogin.Running == a.Id ? Tr("로그인 진행 중 · 열린 터미널에서 마치세요", "Signing in · finish in the terminal that opened")
                    : UsageLogin.Open(a) ? Tr("로그인 터미널이 열려 있음 · 닫으면 계정을 바꿀 수 있습니다", "Sign-in terminal still open · close it to change the account")
                    : !AiAccounts.SignedIn(a) || AiUsage.Failed(a.Id) ? AiAccounts.NeedsSignIn(a, true)
                    : a.IsDefault ? (kind == "chatgpt" ? Tr("기본 계정 · Codex의 로그인", "Default account · Codex's own sign-in") : Tr("기본 계정 · Claude Code의 로그인", "Default account · Claude Code's own sign-in"))
                    : Tr("Bloom에서 추가한 계정", "Added in Bloom");
                if (a.Hidden) status += Tr(" · 홈에서 숨김", " · hidden on the home");
                if (!Demo.On && a.Alias is { Length: > 0 } && a.Email is { Length: > 0 } mail) status = mail + " · " + status; // the title is the user's own words
                var chip = a.Id == chatId ? Theme.T(Tr("AI 채팅에 사용 중", "Used for AI chat"), 12, Theme.P.TextDim, Theme.Medium, false) : null;
                var account = a;
                Add(Demo.On ? title : AiAccounts.Label(a), status, a.IsDefault ? chip : SignOutEnd(chip, account), G.Account, () => AccountMenu(account), null, filter);
                UsageIconRow(account, filter);
            }
            Add(Tr($"{title} 계정 추가…", $"Add a {title} account…"), Tr("새 터미널에서 공식 로그인 실행 · 브라우저에서 로그인", "Runs the official sign-in in a new terminal · sign in in the browser"), null, G.Add, () =>
            {
                if (UsageLogin.Blocked(kind) is { } why)
                {
                    ConfirmWindow.Tell(Window.GetWindow(this), Tr($"{title} 계정 추가", $"Add a {title} account"), why);
                    return;
                }
                var a = AiAccounts.Add(kind);
                Build(lastFilter);
                _ = SignIn(a);
            }, null, filter);
        }
        ApiKeyRows(filter);
        Header(Tr("사용량", "Usage"));
        Tip(Tr("받을 때만 창 없이: Claude 약 470 MB 4초 · Codex 약 110 MB 1초", "Hidden, only while asked: Claude ~470 MB for 4 s · Codex ~110 MB for 1 s"));
        Choice(Tr("새로 받는 간격", "Refresh every"), Tr($"추가한 계정은 {AiUsage.ExtraMinutes}분마다 · 홈의 ↻는 모든 계정", $"Added accounts every {AiUsage.ExtraMinutes} min · the home's ↻ checks all"), G.Refresh,
            new[] { ("5", Tr("5분", "5 min")), ("10", Tr("10분", "10 min")) }, s.UsageMinutes == 5 ? "5" : "10", v =>
            {
                s.UsageMinutes = v == "5" ? 5 : 10;
                AiUsage.Schedule();
                UsageExport.Changed();
            }, filter);
        Choice(Tr("보여 줄 값", "Show"), Tr("쓴 만큼(32%) 또는 남은 만큼(68% 남음) · 막대도 같이 바뀝니다", "What's used (32%) or what's left (68% left) · the bars follow"), G.Model,
            new[] { ("used", Tr("쓴 만큼", "Used")), ("left", Tr("남은 만큼", "Left")) }, s.UsageLeft ? "left" : "used", v =>
            {
                s.UsageLeft = v == "left";
                UsageExport.Changed();
                host.Window.Refresh(true);
            }, filter);
        UsageLogoRow(null, Tr("고른 아이콘 파일을 원래 색 또는 단색으로 · 홈 카드와 작업표시줄에 함께 씁니다", "The picked icon files in their own colours or in one · the home cards and the taskbar share it"), filter);
        More("usage", filter, new()
        {
            (s.ChatGptUsage != Defaults.ChatGptUsage, () =>
                Choice(Tr("ChatGPT 조회 방식", "ChatGPT checks"), s.ChatGptUsage == "light"
                        ? Tr("가벼운 방식: Codex 로그인으로 HTTPS 요청 1번 · CLI 실행 없음 · 공식 API가 아니라 바뀔 수 있음", "Lightweight: one HTTPS request with the Codex sign-in · no CLI · not an official API, may change")
                        : Tr("공식 CLI: 계정마다 Codex 실행 · 약 110 MB 1초", "Official CLI: runs Codex per account · ~110 MB for 1 s"), G.Sync,
                    new[] { ("cli", Tr("공식 CLI", "Official CLI")), ("light", Tr("가벼운 방식", "Lightweight")) }, s.ChatGptUsage == "light" ? "light" : "cli", v =>
                    {
                        s.ChatGptUsage = v;
                        _ = AiUsage.Refresh();
                        Dispatcher.BeginInvoke(() => Build(lastFilter));
                    }, filter)),
            (s.ExportUsage != Defaults.ExportUsage, () =>
            {
                var exportSw = Toggle(s.ExportUsage, UsageExport.Set, out var exportT);
                Add(Tr("사용량 파일 내보내기", "Write the usage to a file"), Tr("다른 프로그램이 읽을 수 있게 퍼센트와 리셋 시각만 %LOCALAPPDATA%\\Bloom\\export\\ai-usage.json에 씁니다 (이메일 없음) · 홈 블록이 꺼져 있어도 조회가 돌아 Claude 약 470 MB 4초가 간격마다 듭니다", "Writes the percentages and reset times, no e-mail, to %LOCALAPPDATA%\\Bloom\\export\\ai-usage.json for other programs to read · the lookups run even with the home block off: Claude ~470 MB for 4 s every interval"), exportSw, G.Document, exportT, null, filter);
            }),
            (false, () =>
                Add(Tr("가격표 고치기", "Edit prices"), Tr($"내장 가격({TokenPrices.Checked} 기준) 위에 덮어씀 · prices.json", $"Overrides the built-in token prices (as of {TokenPrices.Checked}) · prices.json"), null, G.Pen, () =>
                {
                    TokenPrices.EnsureOverride();
                    Launch.Open(TokenPrices.OverridePath);
                }, null, filter)),
        });
    }

    /// An added account's line ends in its X (로그아웃), after the AI chat chip when it has one.
    private FrameworkElement SignOutEnd(TextBlock? chip, AiAccount a)
    {
        var end = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (chip is not null)
        {
            chip.VerticalAlignment = VerticalAlignment.Center;
            chip.Margin = new Thickness(0, 0, 8, 0);
            end.Children.Add(chip);
        }
        end.Children.Add(ToolUi.Button(G.Close, Tr("로그아웃", "Sign out"), () => SignOut(a)));
        return end;
    }

    /// The account's sign-in terminal is open: said, and nothing else is done to the account until it closes.
    private bool TerminalOpen(AiAccount a)
    {
        if (!UsageLogin.Open(a)) return false;
        ConfirmWindow.Tell(Window.GetWindow(this), AiAccounts.Label(a), Tr("로그인 터미널이 열려 있습니다. 로그인을 마치거나 터미널을 닫으세요.", "The sign-in terminal is open. Finish signing in or close it."));
        return true;
    }

    /// The X and the menu's 로그아웃: asked once, then the account is off the list at once and its CLI signs it out behind
    /// (UsageLogin.SignOut); AI chat on it goes back to the default account.
    private void SignOut(AiAccount a)
    {
        if (TerminalOpen(a)) return;
        bool chat = AiAccounts.Chat(a.Kind).Id == a.Id;
        string what = Tr("이 계정에서 로그아웃하고 목록에서 뺍니다. 다시 쓰려면 계정을 새로 추가하세요.", "Signs this account out and takes it off the list. Add it again to use it.");
        if (chat) what += " " + Tr("AI 채팅은 기본 계정으로 돌아갑니다.", "AI chat goes back to the default account.");
        if (ConfirmWindow.Ask(Window.GetWindow(this), AiAccounts.Label(a), what, (Tr("로그아웃", "Sign out"), MessageBoxResult.Yes), (Tr("취소", "Cancel"), MessageBoxResult.No)) != MessageBoxResult.Yes) return;
        if (chat)
        {
            AiAccounts.UseForChat(a.Kind, AiAccounts.DefaultId(a.Kind));
            host.Chats.EndAll();
        }
        _ = UsageLogin.SignOut(a);
        AiUsage.Forget(a.Id);
        Build(lastFilter);
    }

    /// An account's choices: use it for AI chat, sign in again, hide it from the home or sign it out.
    private void AccountMenu(AiAccount a)
    {
        var owner = Window.GetWindow(this);
        if (TerminalOpen(a)) return;
        bool chat = AiAccounts.Chat(a.Kind).Id == a.Id;
        var choices = new List<(string, MessageBoxResult)>();
        if (!chat) choices.Add((Tr("AI 채팅에 사용", "Use for AI chat"), MessageBoxResult.OK));
        choices.Add(a.IsDefault ? (a.Hidden ? Tr("홈에 표시", "Show on the home") : Tr("홈에서 숨기기", "Hide on the home"), MessageBoxResult.Yes) : (Tr("다시 로그인", "Sign in again"), MessageBoxResult.Yes));
        if (!a.IsDefault) choices.Add((Tr("로그아웃", "Sign out"), MessageBoxResult.No));
        choices.Add((Tr("닫기", "Close"), MessageBoxResult.Cancel));
        switch (ConfirmWindow.Ask(owner, AiAccounts.Label(a), a.IsDefault ? Tr("기본 계정", "Default account") : Tr("Bloom에서 추가한 계정", "Added in Bloom"), choices.ToArray()))
        {
            case MessageBoxResult.OK:
                if (a.Kind == "chatgpt" && CodexAuth.Unsaved(a.Home))
                {
                    // Codex would start on the spent sign-in in auth.json
                    ConfirmWindow.Tell(owner, AiAccounts.Label(a), Tr("이 계정의 갱신된 로그인을 아직 저장하지 못했습니다. 잠시 후 다시 시도하세요.", "This account's renewed sign-in isn't saved yet. Try again in a moment."));
                    return;
                }
                AiAccounts.UseForChat(a.Kind, a.Id);
                Settings.Save();
                host.Chats.EndAll(); // the next message starts in the new account
                break;
            case MessageBoxResult.Yes when a.IsDefault:
                a.Hidden = !a.Hidden;
                Settings.Save();
                if (!a.Hidden) _ = AiUsage.Refresh(all: false); // its card comes back with today's numbers (a default account is in every look)
                else UsageExport.Changed();
                host.Window.Refresh(true);
                break;
            case MessageBoxResult.Yes:
                _ = SignIn(a);
                break;
            case MessageBoxResult.No:
                SignOut(a);
                return;
            default:
                return;
        }
        Build(lastFilter);
    }

    /// Runs the CLI's sign-in for an added account; the line under it follows, a failure is told.
    private async Task SignIn(AiAccount a)
    {
        string? error = await UsageLogin.Run(a); // the lines follow UsageLogin.Changed (SettingsView)
        Build(lastFilter);
        if (error is not null) ConfirmWindow.Tell(Window.GetWindow(this), AiAccounts.Label(a), error);
    }
}
