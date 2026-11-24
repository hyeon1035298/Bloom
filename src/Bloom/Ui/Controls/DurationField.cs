using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Bloom.Ui;

/// A length of time: a number typed in (DigitBox) and its unit picked beside it (분 · 시간 · 일), kept in minutes.
/// Enter or leaving the field keeps the number, Esc puts the old one back; another unit keeps the number and changes
/// what it means (30 분 → 30 시간). It opens on the largest unit the value divides into (120 분 shows as 2 시간).
/// The field's Tag answers the keys while it is typed in, the way 설정's HandleKey asks number fields.
internal sealed class DurationField : StackPanel
{
    public static (string key, string label, int minutes)[] MinutesHours => new[] { ("min", Tr("분", "min"), 1), ("h", Tr("시간", "hr"), 60) };

    public static (string key, string label, int minutes)[] MinutesHoursDays => new[] { ("min", Tr("분", "min"), 1), ("h", Tr("시간", "hr"), 60), ("d", Tr("일", "days"), 1440) };

    private readonly TextBox box;
    private readonly Segmented unit;
    private readonly (string key, string label, int minutes)[] units;
    private readonly int max;
    private int minutes;

    /// The new value in minutes (0 included), after Enter, leaving the field or another unit.
    public event Action<int>? Changed;

    /// Enter or Esc in the field: where the keyboard goes next (설정's filter line).
    public Action? Done;

    public DurationField(int minutes, int maxMinutes, (string key, string label, int minutes)[] units)
    {
        this.units = units;
        this.minutes = minutes;
        max = maxMinutes;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        var (shown, per) = Split(minutes);
        box = DigitBox.Make(shown.ToString(CultureInfo.InvariantCulture), 6, 14, Theme.Medium, 36);
        var well = new SmoothBorder
        {
            Background = Theme.P.Well,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5, 10, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.IBeam,
            Child = box,
        };
        well.MouseLeftButtonDown += (_, e) =>
        {
            FocusBox();
            e.Handled = true;
        };
        unit = new Segmented(units.Select(u => (u.key, u.label)), per.key) { Margin = new Thickness(8, 0, 0, 0) };
        unit.Picked += _ => Keep();
        Children.Add(well);
        Children.Add(unit);
        box.Tag = (Func<Key, bool>)(key =>
        {
            switch (key)
            {
                case Key.Enter:
                    Keep();
                    Done?.Invoke();
                    return true;
                case Key.Escape:
                    box.Text = Split(this.minutes).value.ToString(CultureInfo.InvariantCulture);
                    Done?.Invoke();
                    return true;
                default:
                    return false; // the caret moves, the digits go in
            }
        });
        box.LostKeyboardFocus += (_, _) => Keep();
    }

    public void FocusBox()
    {
        box.Focus();
        Keyboard.Focus(box);
        box.SelectAll();
    }

    /// The value as the largest unit it divides into: 120 → (2, 시간); 0 and odd minutes stay in the first unit.
    private (int value, (string key, string label, int minutes) unit) Split(int m)
    {
        var u = units.LastOrDefault(x => m > 0 && m % x.minutes == 0, units[0]);
        return (m / u.minutes, u);
    }

    private void Keep()
    {
        var per = units.First(u => u.key == unit.Value);
        int typed = int.TryParse(box.Text, out int n) ? n : Split(minutes).value;
        int v = (int)Math.Clamp((long)typed * per.minutes, 0, max);
        box.Text = (v / per.minutes).ToString(CultureInfo.InvariantCulture); // "007", nothing or past the top reads as kept
        if (v == minutes) return;
        minutes = v;
        Changed?.Invoke(v);
    }
}
