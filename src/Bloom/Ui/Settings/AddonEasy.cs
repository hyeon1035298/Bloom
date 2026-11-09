using System;
using System.Linq;
using Bloom.Addons;

namespace Bloom.Ui;

/// How the add-on pages (설정 > 애드온, drawn from a schema) word and lay out a line: the "easy" parts the add-on's
/// schema ships (AddonSchemaEasy) as the page reads them — a plainer name and line than the schema's, plainer option
/// names, a line shown only while another setting calls for it, rarely changed lines under 자세히 (an option's picture
/// is AddonPictures'). A key without them is drawn from the schema's names. What isn't data stays with the page: 'Win+Alt+Tab도
/// 같게' and 정리 세기 (SettingsView.AddonSettings).
internal static class AddonEasy
{
    internal sealed record Line
    {
        public string? Title { get; init; }
        public string? Sub { get; init; }

        /// An option's own name by its value (null: the schema's).
        public Func<string, string?>? Option { get; init; }

        /// Shown only while this holds, indented under the line it belongs to; searching finds it either way.
        public Func<AddonValues, bool>? When { get; init; }

        /// Under the group's 자세히.
        public bool More { get; init; }

        /// A number of minutes past an hour: typed in minutes or hours (DurationField).
        public bool Minutes { get; init; }
    }

    /// What the schema says of a setting's line ("" for no line under it; null: the schema's own words).
    public static Line? Of(AddonSchema? schema, string key)
    {
        if (schema?[key] is not { } item) return null;
        var e = item.Easy;
        bool named = item.Options.Any(o => o.EasyName is not null);
        if (e is null && !named) return null;
        return new Line
        {
            Title = e?.Name?.Shown,
            Sub = e?.NoAbout == true ? "" : e?.About?.Shown,
            Option = named ? v => item.Options.FirstOrDefault(o => o.Value == v)?.EasyName?.Shown : null,
            When = e?.When is { } when ? when.Holds : null,
            More = e?.More == true,
            Minutes = e?.Minutes == true,
        };
    }

    /// A group's own name (null: the schema's).
    public static string? Group(AddonGroup group) => group.Easy?.Name?.Shown;

    /// A group's own line under its name ("" for none; null: the schema's).
    public static string? GroupNote(AddonGroup group) => group.Easy is { NoAbout: true } ? "" : group.Easy?.About?.Shown;

    /// A group drawn folded under one 자세히 line (rarely changed).
    public static bool Folded(AddonGroup group) => group.Easy?.Folded == true;

    /// Win+Alt+Tab set as Alt+Tab ('Win+Alt+Tab도 같게'), unless the switch was turned off this visit.
    public static bool SameWin(AddonValues v) => !WinApart && v.Text("monitor.winAltTabLocation") == v.Text("monitor.altTabLocation")
        && v.Text("monitor.winAltTabWindows") == v.Text("monitor.altTabWindows");

    /// 'Win+Alt+Tab도 같게' turned off while the two still match: their own lines show until Bloom closes.
    public static bool WinApart { get; set; }

    /// 정리 세기's steps as the add-on's bits (clean.areas): the working sets only, the add-on's default, all seven.
    public static readonly (string key, int bits)[] Strengths = [("light", 1), ("normal", 103), ("strong", 127)];

    /// 정리 세기 '직접' picked: the seven chips show even while they match a step.
    public static bool CustomAreas { get; set; }
}
