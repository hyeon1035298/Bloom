using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Bloom.Native;
using static Bloom.Native.WinRt;

namespace Bloom.Ocr;

/// One recognized line and where it is in the picture (pixels).
internal sealed record OcrLine(string Text, double X, double Y, double Width, double Height);

internal sealed record OcrOutput(string Text, List<OcrLine> Lines, string Language);

/// Windows' own OCR (Windows.Media.Ocr) through Native\WinRt (no projection packages).
/// Everything runs on one MTA thread of its own; nothing leaves the PC.
internal static unsafe class WinOcr
{
    private static readonly Guid IID_IOcrEngineStatics = new("5bffa85a-3384-3540-9940-699120d428a8");
    private static readonly Guid IID_ISoftwareBitmapStatics = new("df0385db-672f-4a9d-806e-c2442f343e86");
    private static readonly Guid IID_ICryptographicBufferStatics = new("320b7e22-3cb0-4cdf-8663-1d28910065eb");
    private static readonly Guid IID_ILanguageFactory = new("9b0252ac-0c27-44f8-b792-9793fb66c63e");
    private const int First = WinRt.First;

    /// The language tag, or null = the user's profile languages.
    public static Task<OcrOutput?> Recognize(byte[] bgra, int width, int height, string? language) =>
        WinRt.OnMta(() => Run(bgra, width, height, language), null, "ocr");

    private static List<string>? languages;

    /// The recognizer languages installed on this PC ("ko", "ja", …), asked once.
    public static List<string> Languages() => languages ??= ReadLanguages();

    private static List<string> ReadLanguages()
    {
        var task = WinRt.OnMta(() =>
        {
            var list = new List<string>();
            var release = new List<IntPtr>();
            try
            {
                IntPtr statics = Keep(release, Factory("Windows.Media.Ocr.OcrEngine", IID_IOcrEngineStatics));
                IntPtr view = Keep(release, WinRt.Call(statics, First + 1)); // get_AvailableRecognizerLanguages
                foreach (IntPtr lang in Items(view))
                {
                    Keep(release, lang);
                    list.Add(GetString(lang, First)); // ILanguage: get_LanguageTag is its first method
                }
            }
            finally
            {
                WinRt.ReleaseAll(release);
            }
            return list;
        }, new List<string>(), "ocr languages");
        return task.Wait(5000) ? task.Result : new List<string>();
    }

    private static OcrOutput? Run(byte[] bgra, int width, int height, string? language)
    {
        var release = new List<IntPtr>();
        try
        {
            IntPtr statics = Keep(release, Factory("Windows.Media.Ocr.OcrEngine", IID_IOcrEngineStatics));
            IntPtr engine;
            if (language is { Length: > 0 })
            {
                IntPtr langFactory = Keep(release, Factory("Windows.Globalization.Language", IID_ILanguageFactory));
                IntPtr tag = WinRt.HString(language);
                IntPtr lang;
                try
                {
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(langFactory, First))(langFactory, tag, &lang));
                }
                finally
                {
                    WinRt.FreeHString(tag);
                }
                Keep(release, lang);
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(statics, First + 3))(statics, lang, &engine));
            }
            else engine = WinRt.Call(statics, First + 4);
            if (engine == IntPtr.Zero) return null; // the language's recognizer is not installed
            Keep(release, engine);
            string used = language ?? "";
            try
            {
                // IOcrEngine.get_RecognizerLanguage → ILanguage.get_LanguageTag
                IntPtr cur = Keep(release, WinRt.Call(engine, First + 1));
                used = GetString(cur, First);
            }
            catch
            {
                /* only a label */
            }

            // the pixels → IBuffer → SoftwareBitmap (Bgra8 = 87)
            IntPtr crypt = Keep(release, Factory("Windows.Security.Cryptography.CryptographicBuffer", IID_ICryptographicBufferStatics));
            IntPtr buffer;
            fixed (byte* p = bgra)
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, byte*, IntPtr*, int>)Slot(crypt, First + 3))(crypt, (uint)bgra.Length, p, &buffer));
            Keep(release, buffer);
            IntPtr bmpStatics = Keep(release, Factory("Windows.Graphics.Imaging.SoftwareBitmap", IID_ISoftwareBitmapStatics));
            IntPtr bitmap;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, int, IntPtr*, int>)Slot(bmpStatics, First + 3))(bmpStatics, buffer, 87, width, height, &bitmap));
            Keep(release, bitmap);

            // RecognizeAsync, then wait for it (15 s at most)
            IntPtr op;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(engine, First))(engine, bitmap, &op));
            Keep(release, op);
            if (!WinRt.Await(op, 15000)) return null;
            IntPtr result = Keep(release, WinRt.Results(op)); // IAsyncOperation<OcrResult>.GetResults

            string text = GetString(result, First + 2);
            var lines = new List<OcrLine>();
            IntPtr lineView = Keep(release, WinRt.Call(result, First));
            foreach (IntPtr line in Items(lineView))
            {
                Keep(release, line);
                string lt = GetString(line, First + 1);
                IntPtr wordView = Keep(release, WinRt.Call(line, First));
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = 0, y1 = 0;
                foreach (IntPtr word in Items(wordView))
                {
                    Keep(release, word);
                    Rect r;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, Rect*, int>)Slot(word, First))(word, &r));
                    x0 = Math.Min(x0, r.X);
                    y0 = Math.Min(y0, r.Y);
                    x1 = Math.Max(x1, r.X + r.Width);
                    y1 = Math.Max(y1, r.Y + r.Height);
                }
                if (x1 > 0) lines.Add(new OcrLine(lt, x0, y0, x1 - x0, y1 - y0));
            }
            // one line per line (OcrResult.Text joins them with spaces); Japanese comes back with a space
            // between every character, which is taken out again
            bool cjk = used.StartsWith("ja", StringComparison.OrdinalIgnoreCase) || used.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
                if (cjk) lines[i] = lines[i] with { Text = JoinCjk(lines[i].Text) };
            string joined = lines.Count > 0 ? string.Join("\n", lines.ConvertAll(l => l.Text)) : text;
            return new OcrOutput(joined, lines, used);
        }
        finally
        {
            WinRt.ReleaseAll(release);
        }
    }

    // kana, CJK ideographs, full-width forms, CJK punctuation
    private static bool Wide(char c) => c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '鿿' or >= '＀' and <= '￯' or >= '　' and <= '〿';

    private static string JoinCjk(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ' ' && i > 0 && i + 1 < s.Length && (Wide(s[i - 1]) || Wide(s[i + 1]))) continue;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public float X, Y, Width, Height;
    }
}
