using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Core;

namespace Bloom.App;

/// `Bloom.exe --open <file>` (Explorer's 연결 프로그램, FileAssoc) while a Bloom runs: this start hands the file's full
/// path to the running Bloom over a pipe only this Windows user can open, lets it take the foreground, waits for its
/// one-line answer and ends; the running Bloom opens the file (Host.OpenHanded: its document window, the mini player for
/// an audio file).
internal static class DocHandoff
{
    public const string Arg = "--open";

    /// The running Bloom's pipe for files handed over.
    public static string Pipe => Paths.Pipe("open");

    /// A path the pipe takes: a full one, of a file that is there.
    public static bool Acceptable(string path) => path.Length is > 0 and < 32768 && Path.IsPathFullyQualified(path) && File.Exists(path);

    /// The running Bloom, from its start: every path handed over that is Acceptable goes to `open` (true: opened).
    public static void Listen(string name, Func<string, Task<bool>> open) => LinePipe.Listen(name, Acceptable, open, "open");

    /// The handing start: Done once the running Bloom opened the file (LinePipe.Send).
    public static LinePipe.Answer? Send(string name, string path, int answerMs = 15000) => LinePipe.Send(name, path, answerMs);

    /// A file that didn't open, said in a plain message box (from the start that was asked to open it): never silently.
    public static void Tell(string path, string why)
    {
        Log.Write("open: failed " + path + " · " + why);
        MessageBox.Show(Tr("Bloom이 파일을 열지 못했습니다", "Bloom couldn't open the file") + "\n\n" + path + "\n\n" + why, "Bloom", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// Why: the file isn't there, isn't a full path, or the running Bloom said no.
    public static string Refused => Tr("파일이 없거나 열 수 없는 파일입니다", "The file isn't there, or it can't be opened");
}
