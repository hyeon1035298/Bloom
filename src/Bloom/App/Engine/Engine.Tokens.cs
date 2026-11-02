using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// The token blocks of the home (토큰 사용량, 토큰 그래프, 모델별 사용량, 세션별 사용량): one snapshot each from
/// TokenStats, for the span the block keeps (HomeWidget.Period).
internal sealed partial class Engine
{
    private Section TokenBlock(HomeWidget w)
    {
        if (!w.Collapsed) TokenStats.Look();
        var view = TokenStats.Query(PeriodOf(w.Period));
        string? note = TokenStats.Indexing is { } done ? Tr($"세는 중 {Percent(done)}%", $"Counting {Percent(done)}%")
            : TokenStats.Counted is { } counted ? Ago(counted) : null;
        string key = w.Key;
        void Pick(TokenPeriod to)
        {
            Home.SetPeriod(key, to.ToString());
            host.Window.Refresh(true);
        }
        bool none = TokenStats.NoLogs;
        bool counting = TokenStats.Indexing is not null || TokenStats.Counted is null; // the first count is not done
        int rows = Home.Count(w);
        Func<System.Windows.FrameworkElement> widget = key switch
        {
            "tokenchart" => () => Ui.TokenViews.Chart(view, none, counting, Pick),
            "tokenmodels" => () => Ui.TokenViews.Models(view, none, counting, rows, Pick),
            "tokensessions" => SessionsWidget(view, none, counting, rows, Pick),
            _ => () => Ui.TokenViews.Summary(view, none, counting, Pick),
        };
        return new Section(Home.Name(key), note) { Widget = widget, Reload = TokenStats.Refresh };
    }

    /// 세션별 사용량: the sessions it can show (its rows and the rest's tooltip) named here, on the UI thread, from the
    /// chats in memory: a Bloom chat by its session id and provider (the tokens' "codex" is the chat's "chatgpt"), a
    /// click opening it while it is kept.
    private Func<System.Windows.FrameworkElement> SessionsWidget(TokenView view, bool none, bool counting, int rows, Action<TokenPeriod> pick)
    {
        int many = Math.Min(view.Sessions.Count, Math.Max(rows, 1) + Ui.TokenViews.RestTipMost);
        var named = new List<(string Name, Action? Open)>(many);
        for (int i = 0; i < many; i++)
        {
            var s = view.Sessions[i];
            var chat = s.Bloom ? ChatOf(s) : null;
            named.Add((Ui.TokenViews.SessionName(s, chat?.Title), chat is null ? null : () => host.OpenChat(chat)));
        }
        return () => Ui.TokenViews.Sessions(view, named, none, counting, rows, pick);
    }

    private ChatThread? ChatOf(TokenSession s) =>
        host.Chats?.Threads.FirstOrDefault(t => t.SessionId == s.Id && ((t.SessionProvider ?? "claude") == "chatgpt" ? "codex" : t.SessionProvider ?? "claude") == s.Provider);

    /// A block's kept span (HomeWidget.Period) by its name; anything else (none yet, a number, an older name) is the
    /// last 24 hours.
    internal static TokenPeriod PeriodOf(string? stored) =>
        Enum.TryParse<TokenPeriod>(stored, out var p) && Enum.IsDefined(p) && p.ToString() == stored ? p : TokenPeriod.Day;

    /// 0…1 as a whole percent, 99 at most until it is done (100% while still counting reads as finished).
    private static int Percent(double done) => Math.Min(99, (int)Math.Floor(done * 100));
}
