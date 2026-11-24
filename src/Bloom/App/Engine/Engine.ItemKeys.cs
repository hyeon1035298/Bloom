using System.Collections.Generic;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// Ctrl+K › 단축키 지정: a global shortcut for one item (anything that can be a favorite), asked in the search line.
internal sealed partial class Engine
{
    public List<ActionItem> ItemKeyActions(ResultItem item)
    {
        var list = new List<ActionItem>();
        if (item.AsFavorite is not { } entry) return list;
        string? now = ItemKeys.For(item.Id);
        list.Add(new ActionItem
        {
            Title = now is null ? Tr("단축키 지정…", "Assign shortcut…") : Tr($"단축키 바꾸기… ({Hotkeys.Label(now)})", $"Change shortcut… ({Hotkeys.Label(now)})"),
            Glyph = G.Keyboard,
            StayOpen = true,
            Run = () => host.Window.StartForm(ItemKeyForm(entry, now)),
        });
        if (now is not null)
            list.Add(new ActionItem
            {
                Title = Tr("단축키 지우기", "Remove shortcut"),
                Glyph = G.Delete,
                StayOpen = true,
                Run = () =>
                {
                    ItemKeys.Set(entry, null);
                    host.ApplyHotkeys();
                    host.Window.ShowToast(Tr($"‘{Clip(entry.Title, 20)}’ 단축키 제거 완료", $"Removed shortcut for ‘{Clip(entry.Title, 20)}’"));
                },
            });
        return list;
    }

    private FormSpec ItemKeyForm(Favorites.Entry entry, string? now)
    {
        var f = new FormSpec { Title = Tr($"단축키 · {Clip(entry.Title, 24)}", $"Shortcut · {Clip(entry.Title, 24)}") };
        f.Fields.Add(new FormField
        {
            Label = Tr("단축키", "Shortcuts"),
            Hint = Tr("사용할 키를 누르세요 · 예: Ctrl+Alt+M · 어디서든 실행 가능", "Press a shortcut · e.g. Ctrl+Alt+M · Works anywhere"),
            Value = now is null ? "" : Hotkeys.Label(now),
            Keys = true,
            Check = v =>
            {
                if (Hotkeys.Normal(v) is not { } combo)
                    return (null, KeysHint);
                if (host.Shortcuts?.Owner(combo) is { } owner && owner != "item:" + entry.Id)
                    return (null, owner.StartsWith("item:", System.StringComparison.Ordinal)
                        ? Tr($"‘{ItemKeyTitle(owner)}’에 지정된 단축키입니다. 다른 조합을 누르세요.", $"Already assigned to ‘{ItemKeyTitle(owner)}’. Try another key combination.")
                        : Tr("Bloom에서 사용 중인 단축키입니다. 다른 조합을 누르세요.", "Already used by Bloom. Try another key combination."));
                return (combo, null);
            },
        });
        f.Done = form =>
        {
            string combo = form[0];
            ItemKeys.Set(entry, combo);
            host.ApplyHotkeys();
            host.Window.Go(Mode.Root);
            host.Window.ShowToast(host.Shortcuts?.Status("item:" + entry.Id) == "taken"
                ? Tr($"{Hotkeys.Label(combo)}는 다른 앱에서 사용 중입니다. 다른 조합을 누르세요.", $"{Hotkeys.Label(combo)} is already in use by another app. Try another key combination.")
                : Tr($"{Hotkeys.Label(combo)} → ‘{Clip(entry.Title, 20)}’", $"{Hotkeys.Label(combo)} → ‘{Clip(entry.Title, 20)}’"));
        };
        return f;
    }

    /// What a shortcut field says when the keys can't be one.
    public static string KeysHint => Tr("Ctrl, Alt, Shift, Win 중 하나와 함께 누르세요. F키, PrintScreen, Pause는 단독으로 사용할 수 있습니다.", "Include Ctrl, Alt, Shift, or Win. Function keys, PrintScreen, and Pause can be used alone.");

    private static string ItemKeyTitle(string name) =>
        Settings.Current.ItemKeys.Find(k => "item:" + k.Item.Id == name) is { } k ? Clip(k.Item.Title, 20) : name;
}
