using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Bloom.Search;

internal sealed class CalcAnswer
{
    public string Expr = "", Value = "", Copy = "";
    public string? Detail;
    public string? Caption; // what the left side is (null: 수식): 날짜 계산, 세계 시간
}

/// The launcher's calculator, kept small:
/// arithmetic with precedence, %, functions, constants, factorial, 1,234 separators, 만/억/조 amounts,
/// unit and currency conversion ("10 km to mi", "100 usd", "5만원 to usd", "30평 to m2") and bases.
internal static partial class Calculator
{
    [GeneratedRegex(@"\d")]
    private static partial Regex AnyDigit();

    [GeneratedRegex(@"^\d{4}-\d{1,2}-\d{1,2}$")]
    private static partial Regex DateLike();

    [GeneratedRegex(@"^\d{2,4}-\d{3,4}-\d{4}$")]
    private static partial Regex PhoneLike();

    [GeneratedRegex(@"^(.*\S)\s*(?:\s(?:to|in|as|into)\s|->|→|=>)\s*(\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Conversion();

    [GeneratedRegex(@"\s*(더하기|플러스|plus)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PlusWord();

    [GeneratedRegex(@"\s*(빼기|마이너스|minus)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex MinusWord();

    [GeneratedRegex(@"\s*(곱하기|times)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TimesWord();

    [GeneratedRegex(@"\s*(나누기|divided by)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex DivideWord();

    [GeneratedRegex(@"(?<=\d)\s+[xX]\s+(?=[\d(])")]
    private static partial Regex SpacedX();

    [GeneratedRegex(@"(?<![\w.])(\d+(?:\.\d+)?)[xX](?=\d)")]
    private static partial Regex JoinedX();

    [GeneratedRegex(@"\s*\*\s*|\s*×\s*")]
    private static partial Regex TimesSign();

    [GeneratedRegex(@"(?<=[\d)\s])/(?=[\d(\s])")]
    private static partial Regex DivideSign();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static CalcAnswer? Evaluate(string input)
    {
        string text = Normalize(input);
        if (text.Length == 0 || text.Length > 200) return null;
        if (!AnyDigit().IsMatch(text)) return null;
        if (DateLike().IsMatch(text) || PhoneLike().IsMatch(text)) return null; // dates, phone numbers
        try
        {
            string left = text, right = "";
            var conv = Conversion().Match(text);
            if (conv.Success)
            {
                left = conv.Groups[1].Value.Trim();
                right = conv.Groups[2].Value.Trim();
            }
            var p = new Parser(left);
            if (!p.Parse(out double value, out string? unit, out bool interesting)) return null;
            string expr = Pretty(input.Trim());

            if (right.Length > 0) return Convert(value, unit, right, expr, p.IntegerLiteral);
            if (unit is not null && Units.Currency(unit) is { } cur)
            {
                string target = cur == "KRW" ? "USD" : "KRW";
                return Money(value, cur, target, expr);
            }
            if (unit is not null) return null; // "10 km" alone: nothing to convert to
            if (!interesting) return null; // a bare number
            return Plain(value, expr);
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.Trim())
        {
            char c = ch;
            if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0); // full-width → ASCII
            c = c switch
            {
                '×' or '✕' or '∙' or '·' => '*',
                '÷' => '/',
                '−' or '–' => '-',
                _ => c,
            };
            sb.Append(c);
        }
        string t = sb.ToString().Replace("**", "^");
        t = PlusWord().Replace(t, "+");
        t = MinusWord().Replace(t, "-");
        t = TimesWord().Replace(t, "*");
        t = DivideWord().Replace(t, "/");
        // "3 x 4", "3x4" → 3*4 (x between numbers only; "0x1F" stays a hex literal)
        t = SpacedX().Replace(t, "*");
        t = JoinedX().Replace(t, m => m.Groups[1].Value == "0" ? m.Value : m.Groups[1].Value + "*");
        return t.Trim();
    }

    private static string Pretty(string s)
    {
        string t = TimesSign().Replace(s, " × ");
        t = DivideSign().Replace(t, " ÷ ");
        t = Spaces().Replace(t, " ");
        return t.Trim();
    }

    // ── formatting ──────────────────────────────────────────────────────────────────────────────
    public static string Format(double x, bool group = true)
    {
        if (double.IsNaN(x)) return Tr("정의되지 않음", "Undefined");
        if (double.IsInfinity(x)) return x > 0 ? "∞" : "−∞";
        double ax = Math.Abs(x);
        if (ax != 0 && (ax >= 1e16 || ax < 1e-9)) return x.ToString("0.#########E+0", Inv);
        double r = RoundSig(x, 12);
        decimal d = (decimal)r;
        return d.ToString(group ? "#,0.##########" : "0.##########", Inv);
    }

    private static double RoundSig(double x, int digits)
    {
        if (x == 0) return 0;
        int mag = (int)Math.Floor(Math.Log10(Math.Abs(x))) + 1;
        int dec = Math.Clamp(digits - mag, 0, 15);
        return Math.Round(x, dec);
    }

    /// 350000000 → "3억 5,000만"; null under 1만 (and in English, where the grouped digits say it).
    public static string? KoreanReading(double x)
    {
        double ax = Math.Abs(x);
        if (En || ax < 10_000 || ax >= 1e20 || Math.Abs(x - Math.Round(x)) > 1e-9) return null;
        long n = (long)Math.Round(ax);
        var parts = new List<string>();
        long jo = n / 1_000_000_000_000, eok = n / 100_000_000 % 10_000, man = n / 10_000 % 10_000, rest = n % 10_000;
        if (jo > 0) parts.Add($"{jo:#,0}조");
        if (eok > 0) parts.Add($"{eok:#,0}억");
        if (man > 0) parts.Add($"{man:#,0}만");
        if (rest > 0) parts.Add($"{rest:#,0}");
        return (x < 0 ? "−" : "") + string.Join(" ", parts);
    }

    private static CalcAnswer Plain(double v, string expr) => new()
    {
        Expr = expr,
        Value = Format(v),
        Copy = Format(v, false),
        Detail = KoreanReading(v),
    };

    // ── conversions ─────────────────────────────────────────────────────────────────────────────
    private static CalcAnswer? Convert(double value, string? unit, string target, string expr, bool integerLiteral)
    {
        string t = target.Trim().ToLowerInvariant();
        switch (t)
        {
            case "hex" or "16진수" or "16진법" or "hexadecimal":
                return Base(value, 16, expr);
            case "bin" or "binary" or "2진수" or "2진법":
                return Base(value, 2, expr);
            case "oct" or "octal" or "8진수" or "8진법":
                return Base(value, 8, expr);
            case "dec" or "decimal" or "10진수" or "10진법":
                return Plain(value, expr) is { } a ? new CalcAnswer { Expr = a.Expr, Value = a.Value, Copy = a.Copy, Detail = "10진수" } : null;
        }
        if (Units.Currency(t) is { } toCur)
        {
            if (unit is not null && Units.Currency(unit) is null) return null; // "10 km to usd"
            string from = unit is not null ? Units.Currency(unit)! : (toCur == "KRW" ? "USD" : "KRW");
            return Money(value, from, toCur, expr);
        }
        if (unit is null) return null;
        var a1 = Units.Find(unit);
        var b1 = Units.Find(t);
        if (a1 is null || b1 is null || a1.Dim != b1.Dim) return null;
        double result = a1.Dim == "temp" ? Units.Temp(value, a1.Name, b1.Name) : value * a1.Factor / b1.Factor;
        return new CalcAnswer
        {
            Expr = expr,
            Value = $"{Format(result)} {b1.Label}",
            Copy = Format(result, false),
            Detail = $"{Units.DimName(a1.Dim)} · {a1.Label} → {b1.Label}",
        };
    }

    private static CalcAnswer? Base(double value, int radix, string expr)
    {
        if (Math.Abs(value - Math.Round(value)) > 1e-9 || Math.Abs(value) > long.MaxValue) return null;
        long n = (long)Math.Round(value);
        string digits = System.Convert.ToString(n, radix).ToUpperInvariant();
        string prefix = radix switch { 16 => "0x", 2 => "0b", 8 => "0o", _ => "" };
        string label = radix switch { 16 => "16진수", 2 => "2진수", 8 => "8진수", _ => "" };
        return new CalcAnswer { Expr = expr, Value = prefix + digits, Copy = prefix + digits, Detail = Tr($"{label} · 10진수 {n:#,0}", $"{label} · Decimal {n:#,0}") };
    }

    private static CalcAnswer Money(double amount, string from, string to, string expr)
    {
        if (!Rates.TryGet(out var rates, out string age))
        {
            Rates.EnsureFresh();
            return new CalcAnswer { Expr = expr, Value = Tr("환율 불러오는 중…", "Loading exchange rates…"), Copy = "", Detail = $"{from} → {to}" };
        }
        Rates.EnsureFresh();
        if (!rates.TryGetValue(from, out double rf) || !rates.TryGetValue(to, out double rt))
            return new CalcAnswer { Expr = expr, Value = Tr("지원하지 않는 통화", "Unsupported currency"), Copy = "", Detail = $"{from} → {to}" };
        double v = amount / rf * rt;
        int decimals = to is "KRW" or "JPY" or "VND" ? 0 : 2;
        double rounded = Math.Round(v, decimals);
        string sym = Units.Symbol(to);
        double one = rt / rf;
        return new CalcAnswer
        {
            Expr = expr,
            Value = sym.Length > 0 ? $"{sym}{Format(rounded)}" : $"{Format(rounded)} {to}",
            Copy = Format(rounded, false),
            Detail = $"1 {from} = {Format(Math.Round(one, one < 1 ? 6 : 2))} {to} · {age} · Rates By Exchange Rate API",
        };
    }
}
