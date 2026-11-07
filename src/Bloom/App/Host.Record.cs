using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Core;
using Bloom.Native;
using Bloom.Ocr;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// 화면 녹화: an area picked with Bloom Lens is recorded to an MP4 in Videos\Screen Recordings, like Windows' Snipping
/// Tool. The bar asks first (녹화 시작, the sounds); 3 · 2 · 1, then it records until 멈춤 or the same shortcut again.
/// The recording itself runs in a process of its own (RecorderProcess), so its memory goes when it ends.
internal sealed partial class Host
{
    private RecordSession? recording;

    /// A recording is being set up, counted down to or made.
    public bool IsRecording => recording is not null;

    /// The bar for recording `area` (or, while one is open, its end: stopped when recording, closed before).
    public void Record(ScreenRect area)
    {
        if (recording is { } open)
        {
            open.Finish();
            return;
        }
        recording = new RecordSession(this, area);
    }

    /// The same shortcut, menu item or command again while a recording is open: stop it (or close its bar).
    public void StopRecording() => recording?.Finish();

    private sealed class RecordSession
    {
        private enum Step { Menu, Counting, Recording, Saving, Done }

        private readonly Host host;
        private readonly ScreenRect area;
        private readonly RecordingFrame frame;
        private readonly RecordBar bar;
        private readonly IntPtr before; // the app in front before the bar took the keyboard, given it back at the start
        private readonly CancellationTokenSource counting = new();
        private RecorderProcess? process;
        private string? file;
        private Step step = Step.Menu;

        public RecordSession(Host host, ScreenRect area)
        {
            this.host = host;
            this.area = area;
            before = Win32.GetForegroundWindow();
            var s = Settings.Current;
            frame = new RecordingFrame(area);
            frame.Show(Theme.P.Accent.Color);
            var surface = RecordSurface.Find();
            bar = new RecordBar(surface, area, surface.Kind == RecordSurfaceKind.Island ? IslandInk() : Theme.P, s.RecordSystemAudio, s.RecordMic);
            bar.StartClicked += () => _ = Start();
            bar.CancelClicked += Finish;
            bar.StopClicked += Finish;
            bar.SoundsChanged += (system, mic) =>
            {
                s.RecordSystemAudio = system;
                s.RecordMic = mic;
                Settings.Save();
            };
            bar.Show();
            bar.Activate(); // Enter starts, Esc cancels
        }

        /// The island's own colours (its 색 setting, black by default), whatever Bloom's theme.
        private static Palette IslandInk()
        {
            bool white = Addons.AddonHub.Taskbar.Values?.Get("island.color")?.ToString() == "white";
            var p = new Palette();
            p.Apply(!white, null, 0);
            return p;
        }

        /// 녹화 시작: the recorder starts while 3 · 2 · 1 counts, and records from the end of it.
        private async Task Start()
        {
            if (step != Step.Menu) return;
            step = Step.Counting;
            file = NewFile();
            process = RecorderProcess.Start(area, file, bar.SystemAudio, bar.Mic);
            process.Failed += (code, message) => host.Window.Dispatcher.BeginInvoke(() => Fail(code, message));
            // a recorder that can't start (no sound device, no encoder) is told at once, not after the countdown
            _ = process.Ready.ContinueWith(t => host.Window.Dispatcher.BeginInvoke(() =>
            {
                var e = t.Exception?.InnerException;
                if (step == Step.Counting) Fail((e as RecorderException)?.Code ?? "start", e?.Message);
            }), TaskContinuationOptions.OnlyOnFaulted);
            bar.Counting();
            frame.Recording(Theme.P.DangerFill.Color);
            if (before != IntPtr.Zero) Win32.ForceForeground(before); // the keyboard back to the app being recorded
            var count = new RecordCountdown(area);
            bool counted = await count.Run(counting.Token);
            if (!counted || step != Step.Counting) return;
            try
            {
                await process.Ready;
            }
            catch (Exception)
            {
                return; // told by the continuation above
            }
            if (step != Step.Counting) return;
            process.Go();
            step = Step.Recording;
            bar.Recording();
        }

        /// 멈춤, Esc, or the shortcut again: before the start the bar just closes; while counting nothing was recorded
        /// yet and the recorder is let go; while recording the file is finished and told about.
        public void Finish()
        {
            switch (step)
            {
                case Step.Menu:
                    _ = Close();
                    break;
                case Step.Counting:
                    counting.Cancel();
                    process?.Dispose(); // nothing recorded yet: the recorder ends without a file
                    _ = Close();
                    break;
                case Step.Recording:
                    _ = Save();
                    break;
            }
        }

        private async Task Save()
        {
            step = Step.Saving;
            bar.Saving();
            frame.Close();
            var result = await process!.Stop();
            await Close();
            if (!result.Ok)
            {
                Tell(result.ErrorCode ?? "encoder", result.Message);
                return;
            }
            bool copied = CopyFile(result.File);
            string length = Length(result.Duration);
            string name = Path.GetFileName(result.File);
            Notices.Done("Bloom Lens", Tr("녹화 저장 완료", "Recording saved"),
                copied ? Tr($"{length} · 파일을 클립보드에 복사함 · Screen Recordings", $"{length} · file copied to the clipboard · Screen Recordings")
                    : Tr($"{length} · {name} · Screen Recordings", $"{length} · {name} · Screen Recordings"),
                new (string, Action)[]
                {
                    (Tr("열기", "Open"), () => Launch.Open(result.File)),
                    (Tr("폴더에서 보기", "Show in folder"), () => Launch.Reveal(result.File)),
                });
        }

        private void Fail(string code, string? message)
        {
            if (step == Step.Done) return;
            counting.Cancel();
            process?.Dispose();
            _ = Close();
            Tell(code, message);
        }

        private static void Tell(string code, string? message)
        {
            Log.Write($"record failed: {code} {message}");
            string what = code switch
            {
                "audio" => Tr("소리를 녹음하지 못했습니다", "Could not record the sound"),
                "capture" => Tr("화면을 읽지 못했습니다", "Could not read the screen"),
                "io" => Tr("파일을 저장하지 못했습니다", "Could not save the file"),
                _ => Tr("영상을 만들지 못했습니다", "Could not make the video"),
            };
            Notices.Result(false, "Bloom Lens", what, code == "audio"
                ? Tr("소리 장치를 확인하거나 소리 없이 다시 녹화하세요", "Check the sound devices, or record again without sound")
                : Tr("다시 시도하세요", "Try again"));
        }

        private async Task Close()
        {
            if (step == Step.Done) return;
            step = Step.Done;
            frame.Close();
            await bar.Leave();
            process?.Dispose();
            if (ReferenceEquals(host.recording, this)) host.recording = null;
        }

        /// "Bloom <date> <time>.mp4" in Screen Recordings, " (2)" … when that name is taken.
        private static string NewFile()
        {
            string dir = Paths.ScreenRecordings;
            Directory.CreateDirectory(dir);
            string name = "Bloom " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, name + ".mp4");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name} ({i}).mp4");
            return path;
        }

        /// The video on the clipboard as a file, to paste into a chat or a folder.
        private static bool CopyFile(string path)
        {
            for (int i = 0; i < 3; i++)
                try
                {
                    System.Windows.Clipboard.SetFileDropList(new StringCollection { path });
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(60); // another app holds the clipboard for a moment
                }
            return false;
        }

        internal static string Length(TimeSpan t) => t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : t.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
