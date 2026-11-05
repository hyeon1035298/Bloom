using System;

namespace Bloom.App;

internal sealed class Options
{
    public string? Data;
    public bool NoHook, NoTray, Settings, Show;
    public bool Autostart; // --autostart: started at sign-in (the Run entry, the logon task): add-ons first, and a second start stays quiet
    public string? Open; // --open <file>: a Markdown file to open in its document window (Explorer's 연결 프로그램)
    public bool Demo; // --demo: the recording mode (Core.Demo), on a --data folder only
    public double? QaScale; // --qa-scale <n>: QA pictures at n× (README and promo pictures want 2)
    public int? Display;
    public string? CaptureDir;
    public string? GlassFile;
    public string? ChatFile;
    public string? WarmFile; // --qa-warm: time to the first text of an answer, cold vs pre-started (WarmBench)
    public string? QaModel, QaEffort, QaAsk;
    public string? QaSet; // --capture: "new" = only the newest screens
    public string? PasteFile; // --qa-paste: paste into a test window of our own
    public string? OcrFile; // --qa-ocr: read a drawn picture back
    public bool QaTranslate; // --qa-translate: and translate one line with each engine
    public string? SpaceDir, SpacePath; // --qa-space <dir> --qa-space-path <folder>
    public string? Lang; // --lang ko|en: this run's language whatever the settings say (dev, QA)
    public int? MeasureSeconds; // --qa-measure <s>: run as usual (notice cards off-screen, no sounds), write memory.txt, quit
    public int? SummonRounds; // --qa-summon <n>: n cold + warm openings timed (SummonBench), summon.txt, quit
    public int? ResponsiveRounds; // --qa-responsive <n>: isolated synthetic search/render/dispatcher measurement, then quit
    public string? MdBenchFile, MdBenchIn; // --qa-md <out.txt> [--qa-md-in "label=path|…"]: the Markdown view timed (MdBench)
    public int MdBenchRounds = 5; // --qa-md-rounds <n>
}

/// Owns the services and the window and carries out commands.
/// One Bloom Lens pick: the screen area, its pixels (BGRA, physical size) and the PNG saved of it.
internal sealed record LensShot(Ocr.ScreenRect Rect, byte[] Bgra, string Png, DateTime At);
