using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Browser;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// External search providers (providers.json, Search\Providers.cs) in the root search: "ff 보고서" asks the adapter script of
/// the "ff" provider and lists what it prints. A provider's keyword wins over every other use of the same word
/// (quicklinks such as "g", commands, notes): a line that starts with it answers alone, from the provider only.
internal sealed partial class Engine
{
    private string? provAsked; // keyword + words whose answer is in provReply, or on its way
    private ProviderReply? provReply; // null: still running
    private CancellationTokenSource? provStop;

    /// The root line "<keyword> <words>": the provider's rows (or its hint / waiting / error line). Null: not a provider's line.
    private Section? RootProvider(string text)
    {
        if (Providers.Match(text) is not { } found)
        {
            provAsked = null; // the line left the provider: a later identical line asks again
            provStop?.Cancel();
            return null;
        }
        var (p, words) = found;
        var s = new Section(p.Name, p.Keyword);
        if (words.Length < p.MinChars)
        {
            s.Items.Add(ProviderInfo(p, "hint", Tr($"{p.Name} — 검색어 입력", $"{p.Name} — enter words"), p.MinChars > 1 ? Tr($"{p.MinChars}글자 이상", $"At least {p.MinChars} characters") : null));
            provAsked = null;
            provStop?.Cancel();
            return s;
        }
        string key = p.Keyword + "\n" + words;
        if (key != provAsked) AskProvider(p, words, key);
        if (provReply is not { } reply)
        {
            s.Items.Add(ProviderInfo(p, "busy", Tr($"‘{words}’ 찾는 중…", $"Looking for ‘{words}’…"), null));
            return s;
        }
        for (int i = 0; i < reply.Items.Count; i++) s.Items.Add(ProviderItemRow(p, reply.Items[i], i));
        if (reply.Error is { } err) s.Items.Add(ProviderInfo(p, "error", err, null, G.Info));
        else if (reply.Items.Count == 0) s.Items.Add(ProviderInfo(p, "none", Tr("결과 없음", "No results"), null));
        if (reply.Items.Count > 0) s.Note = Tr($"{reply.Items.Count}개 · {p.Keyword}", $"{reply.Items.Count} · {p.Keyword}");
        return s;
    }

    /// A provider's keyword as a row among the results ("ff" → "파일 찾기 — 검색어 입력"); Enter types the keyword and a space.
    private IEnumerable<ResultItem> ProviderKeywordItems(Query q)
    {
        foreach (var p in Providers.Current)
        {
            int sc = Matcher.Score(q, p.Match);
            if (sc <= 0) continue;
            var pp = p;
            yield return new ResultItem
            {
                Id = "prov:" + p.Keyword,
                Kind = ItemKind.Command,
                Title = Tr($"{p.Name} — 검색어 입력", $"{p.Name} — enter words"),
                Subtitle = p.Keyword + " " + Tr("＋ 스페이스 ＋ 검색어", "+ space + words"),
                Accessory = p.Keyword,
                Icon = IconSpec.Of(G.Search),
                PrimaryLabel = Tr("검색어 입력", "Enter words"),
                Score = sc + 6,
                StayOpen = true,
                Run = () => host.OpenView(Mode.Root, pp.Keyword + " "),
            };
        }
    }

    private static ResultItem ProviderInfo(Provider p, string id, string title, string? sub, char glyph = '\0') => new()
    {
        Id = "prov:" + p.Keyword + ":" + id,
        Kind = ItemKind.Info,
        Title = title,
        Subtitle = sub,
        Accessory = p.Name,
        Icon = IconSpec.Of(glyph == '\0' ? G.Search : glyph),
    };

    private ResultItem ProviderItemRow(Provider p, ProviderItem it, int index)
    {
        var a = it.Action;
        var row = new ResultItem
        {
            Id = $"prov:{p.Keyword}:{index}:{it.Title}",
            Kind = a?.Type == "url" ? ItemKind.Url : ItemKind.Command,
            Title = it.Title,
            Subtitle = it.Subtitle,
            Accessory = p.Name,
            Icon = IconSpec.Of(a?.Type switch { "url" => G.Globe, "copy" => G.Copy, "run" => G.NewWindow, _ => G.Search }),
        };
        switch (a?.Type)
        {
            case "url":
                row.PrimaryLabel = Tr("열기", "Open");
                row.Run = () =>
                {
                    if (!Providers.IsSafeUrl(a.Text))
                    {
                        Notices.Result(false, p.Name, Tr("열기 실패", "Not opened"), Tr("http·https 주소만 열 수 있습니다", "Only http and https addresses open"));
                        return;
                    }
                    Web.Open(a.Text!.Trim());
                };
                row.MoreActions = () => new List<ActionItem> { new() { Title = Tr("링크 복사", "Copy link"), Keys = "Ctrl+Shift+C", Glyph = G.Link, Run = () => Launch.Copy(a.Text!) } };
                break;
            case "copy":
                row.PrimaryLabel = Tr("복사", "Copy");
                row.Run = () =>
                {
                    Launch.Copy(a.Text!);
                    Notices.Result(true, p.Name, Tr("복사됨", "Copied"), Clip(a.Text!.ReplaceLineEndings(" "), 80));
                };
                break;
            case "run":
                row.PrimaryLabel = Tr("실행", "Run");
                row.Run = () =>
                {
                    try
                    {
                        Providers.StartDetached(a.Args!, p.Cwd);
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        Notices.Result(false, p.Name, Tr("실행 실패", "Could not run"), e.Message);
                    }
                };
                break;
        }
        return row;
    }

    /// One run at a time: still typing → wait 250 ms; new words → the process before is killed.
    private void AskProvider(Provider p, string words, string key)
    {
        provStop?.Cancel();
        provStop?.Dispose();
        var stop = provStop = new CancellationTokenSource();
        provAsked = key;
        provReply = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, stop.Token);
                var reply = await Providers.Run(p, words, stop.Token);
                _ = host.Window.Dispatcher.BeginInvoke(() =>
                {
                    if (provAsked != key || stop.IsCancellationRequested) return;
                    provReply = reply;
                    if (host.Window.IsVisible && host.Window.Mode == Mode.Root) host.Window.Refresh(true);
                });
            }
            catch (OperationCanceledException)
            {
                // the words changed
            }
        });
    }
}
