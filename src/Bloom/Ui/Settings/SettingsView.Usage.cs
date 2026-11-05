using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bloom.Addons;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.Ui;

/// The usage settings the home cards and the taskbar share (one setting each, wherever its row is): 아이콘 색 (on
/// 설정 > 홈 화면 and the taskbar page), 이름표 and each account's bar (the taskbar page), and each account's logo
/// (설정 > AI 계정). They reach the taskbar through the usage file (UsageExport) and the add-on's values (아이콘 색).
internal sealed partial class SettingsView
{
    /// 아이콘 색: Settings.UsageLogo, written to the add-on's usage.logoColor too.
    private void UsageLogoRow(AddonValues? values, string sub, string filter) =>
        Choice(Tr("아이콘 색", "Icon colour"), sub, G.Palette, new[] { ("brand", Tr("원래 색", "Original")), ("mono", Tr("단색", "Single colour")) },
            Settings.Current.UsageLogo == "mono" ? "mono" : "brand", v =>
            {
                SetUsageLogo(v, values ?? AddonHub.Taskbar.Values);
                host.Window.Refresh(true);
            }, filter);

    internal static void SetUsageLogo(string v, AddonValues? values)
    {
        Settings.Current.UsageLogo = v == "mono" ? "mono" : "brand";
        values?.Set("usage.logoColor", Settings.Current.UsageLogo);
    }

    /// 이름표: the usage file's tag.
    private void UsageTagRow(string filter) =>
        Choice(Tr("이름표", "Label"), Tr("아이콘 옆에 적습니다. 이름이 없는 계정은 요금제를 적고, 길면 …로 줄입니다. 아일랜드에도 같이 나옵니다", "Written beside the icon. An account without a name shows its plan; a long one is cut with …. The island shows it too"), G.Tag,
            new[] { ("none", Tr("없음", "None")), ("name", Tr("이름", "Name")), ("plan", Tr("요금제", "Plan")) }, Settings.Current.UsageTag is "name" or "plan" ? Settings.Current.UsageTag : "none", v =>
            {
                Settings.Current.UsageTag = v;
                UsageExport.Changed();
            }, filter);

    /// One line per account with usage: which of its limits the taskbar shows (the week until one is picked). The
    /// accounts are the user's data: collecting skips them.
    private void UsageBarRows(string filter)
    {
        if (collected is not null) return;
        foreach (var card in AiUsage.Accounts.Where(c => c.Balance is null))
        {
            if (AiAccounts.Find(card.Id) is not { } a) continue;
            var bars = UsageExport.Bars(card);
            if (bars.Count == 0) continue;
            string title = a.Alias is { Length: > 0 } alias && !Demo.On ? alias : card.Name;
            if (bars.Count == 1)
            {
                Add(title, Tr($"이 요금제는 {bars[0].name} 한도만 있습니다", $"This plan has only the {bars[0].name} limit"), null, G.Speed, null, null, filter);
                continue;
            }
            string now = bars.Exists(b => b.key == a.Bar) ? a.Bar! : bars.Exists(b => b.key == "week") ? "week" : bars[0].key;
            Choice(title, card.Plan, G.Speed, bars.ToArray(), now, v =>
            {
                a.Bar = v;
                UsageExport.Changed();
            }, filter);
        }
    }

    /// 설정 > AI 계정: under an account, its logo — the picture, 파일 고르기… and, for a picked one, ✕ to go back to the
    /// company's logo (OfficialLogos).
    private void UsageIconRow(AiAccount a, string filter)
    {
        var end = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        string? shown = UsageIcons.Shown(a);
        if (UsageLogo.Of(shown, 20) is { } logo)
        {
            logo.Margin = new Thickness(0, 0, 10, 0);
            logo.VerticalAlignment = VerticalAlignment.Center;
            end.Children.Add(logo);
        }
        end.Children.Add(RowButton(Tr("파일 고르기…", "Choose a file…"), () => PickUsageIcon(a)));
        if (a.Icon is not null)
        {
            var clear = ToolUi.Button(G.Close, Tr("아이콘 지우기", "Clear the icon"), () =>
            {
                UsageIcons.Clear(a);
                host.Window.Refresh(true);
                Build(lastFilter);
            });
            clear.Margin = new Thickness(6, 0, 0, 0);
            end.Children.Add(clear);
        }
        string sub = a.Icon is not null ? Tr("홈 카드와 작업표시줄에 이 그림을 씁니다", "The home card and the taskbar use this picture")
            : shown is not null ? Tr("회사 사이트에서 받은 공식 로고를 씁니다. 다른 그림을 쓰려면 파일을 고르세요", "Uses the company's logo from its website. Choose a file to use another picture")
            : Tr("공식 로고를 아직 받지 못해 글자로 보입니다. 로고 파일(SVG, PNG, ICO)을 직접 고를 수도 있습니다", "The company's logo isn't fetched yet, so a letter shows. You can also choose a logo file (SVG, PNG, ICO)");
        Add(Tr("아이콘", "Icon"), sub, end, G.Palette, () => PickUsageIcon(a), null, filter);
    }

    private void PickUsageIcon(AiAccount a)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = AiAccounts.Label(a), Filter = Tr("로고 (*.svg;*.png;*.ico)|*.svg;*.png;*.ico", "Logos (*.svg;*.png;*.ico)|*.svg;*.png;*.ico") };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            UsageIcons.Pick(a, dlg.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("usage icon", e);
            ConfirmWindow.Tell(Window.GetWindow(this), AiAccounts.Label(a), Tr("그 파일을 아이콘으로 가져오지 못했습니다.", "That file couldn't be brought in as the icon."));
            return;
        }
        host.Window.Refresh(true);
        Build(lastFilter);
    }
}
