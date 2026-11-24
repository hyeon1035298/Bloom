using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Bloom.App;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// 단축키: the shortcuts given to single items (Ctrl+K › 단축키 지정), each with the app, folder or command it opens and
/// its keys — to look up what was set. A click runs the item; 바꾸기 opens 설정 › 도구, where they change.
internal sealed class KeysTool : ITool
{
    private readonly Host host;
    private readonly Grid view = new();
    private readonly StackPanel list = new() { Margin = new Thickness(8, 4, 8, 8) };
    private readonly TextBox search;
    private string drawn = "\0"; // what the list shows now (the search, each item, its keys and their state): unchanged = not redrawn

    public string Key => "keys";
    public string Name => Tr("단축키", "Shortcuts");
    public char Glyph => G.Keyboard;
    public FrameworkElement View => view;
    public FrameworkElement? Head => null;
    public string? State => null;

    public KeysTool(Host host)
    {
        this.host = host;
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var (field, box) = ToolUi.Field(Tr("앱 이름 또는 단축키로 찾기", "Search by app name or shortcut"));
        search = box;
        field.Margin = new Thickness(12, 10, 12, 2);
        view.Children.Add(field);
        var note = Theme.T(Tr("지정된 단축키 · 클릭하여 실행", "Assigned shortcuts · Click to run"), 11.5, Theme.P.TextFaint);
        note.Margin = new Thickness(16, 4, 16, 2);
        Grid.SetRow(note, 1);
        view.Children.Add(note);
        var scroll = ToolUi.Scroll(list);
        Grid.SetRow(scroll, 2);
        view.Children.Add(scroll);
        var change = ToolUi.TextButton(Tr("단축키 바꾸기 · 지우기", "Change or remove shortcut"), () => host.OpenSettings("tools"));
        change.Margin = new Thickness(16, 2, 16, 12);
        Grid.SetRow(change, 3);
        view.Children.Add(change);
        search.TextChanged += (_, _) => Fill();
        // set or changed elsewhere (Ctrl+K, 설정): looked at again whenever the window comes up or the mouse comes in,
        // redrawn only when something changed
        view.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) Fill();
        };
        view.MouseEnter += (_, _) => Fill();
        Fill();
    }

    private void Fill()
    {
        var all = ItemKeys.All.ToList();
        string q = search.Text.Trim();
        string now = q + "\n" + string.Join("\n", all.Select(k => $"{k.Item.Id}={k.Combo}:{host.Shortcuts?.Status("item:" + k.Item.Id)}"));
        if (now == drawn) return;
        drawn = now;
        list.Children.Clear();
        if (all.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(Tr("지정된 단축키 없음 · 런처에서 항목을 선택하고 Ctrl+K로 지정하세요", "No shortcuts assigned · Select an item in the launcher and press Ctrl+K to assign one")));
            list.Children.Add(ToolUi.TextButton(Tr("런처에서 항목 선택", "Select item in launcher"), () => host.OpenView(Mode.Root)));
            return;
        }
        var shown = all
            .Where(k => q.Length == 0 || k.Item.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || Hotkeys.Label(k.Combo).Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (shown.Count == 0)
        {
            list.Children.Add(ToolUi.Empty(Tr("검색 결과 없음", "No results found")));
            return;
        }
        foreach (var k in shown) list.Children.Add(Row(k));
    }

    /// The item's icon and name (what it is under it, or that another app holds the keys), its key caps on the right.
    private SmoothBorder Row(ItemKey k)
    {
        var item = host.Engine.FromFavorite(k.Item);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = item?.Icon is { } spec ? Icons.Build(spec, 22) : Theme.Glyph(G.Info, 13, Theme.P.TextFaint);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Theme.T(k.Item.Title, 13, Theme.P.Text, Theme.Medium));
        string? sub = item is null ? Tr("항목을 찾지 못했습니다. 위치를 확인하거나 단축키를 다시 지정하세요.", "Item not found. Check its location or reassign the shortcut.")
            : host.Shortcuts?.Status("item:" + k.Item.Id) == "taken" ? Tr("다른 앱에서 사용 중인 키", "Key already in use by another app")
            : item.Accessory;
        if (sub is { Length: > 0 })
        {
            var s = Theme.T(sub, 11.5, item is null || sub != item.Accessory ? Theme.P.Danger : Theme.P.TextFaint);
            s.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(s);
        }
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var caps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        foreach (string part in Hotkeys.Parts(k.Combo)) caps.Children.Add(Keys.Cap(part));
        Grid.SetColumn(caps, 2);
        g.Children.Add(caps);
        var entry = k.Item;
        return ToolUi.Row(g, () => host.RunItemKey(entry));
    }

    public void FocusFirst() => search.Dispatcher.BeginInvoke(() =>
    {
        search.Focus();
        Keyboard.Focus(search);
    }, DispatcherPriority.Input);

    public void Closed()
    {
    }
}
