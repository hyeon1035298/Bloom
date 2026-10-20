using System.IO;
using Bloom.Core;
using Bloom.Music;
using Bloom.Search;
using Bloom.Ui;

namespace Bloom.App;

/// Markdown files in Bloom's document window (Ui\DocWindow.cs): from Explorer (`--open`, DocHandoff), from the search.
/// Audio files and playlists (.m3u, .m3u8) Explorer hands over go to the mini player.
internal sealed partial class Host
{
    /// The file's document window, made or brought forward; false for anything but a full path to a file that is there.
    public bool OpenDoc(string path)
    {
        if (!DocHandoff.Acceptable(path))
        {
            Log.Write("open: not a file " + path);
            return false;
        }
        Window.Dismiss();
        DocWindow.Show(path);
        Log.Write("open: " + path);
        return true;
    }

    /// A file Explorer handed over (`--open`: 연결 프로그램, by DocHandoff or with this start): an audio file plays in
    /// the mini player (refused while it is switched off), a text file opens in the document window, anything else is
    /// refused — the window would take its bytes for text and hold the UI thread for minutes laying them out (the
    /// start handing it over gave up: "didn't answer"). Never Launch.Open: with Bloom as Windows' app for the file that
    /// would hand it straight back.
    public bool OpenHanded(string path)
    {
        if (!DocHandoff.Acceptable(path)) return false;
        if (Playlists.IsPlaylist(path))
        {
            if (!Features.On("player")) return false;
            Tools.Open("player");
            MusicPlayer.OpenPlaylist(path); // one it can't play says why in the player and a notice
            Log.Write("open: playlist " + path);
            return true;
        }
        if (AudioFiles.IsAudio(path))
        {
            if (!Features.On("player")) return false;
            Tools.Open("player");
            MusicPlayer.OpenHanded(path);
            Log.Write("open: play " + path);
            return true;
        }
        if (!DocFile.IsMarkdown(path) && DocFile.LooksBinary(path)) return false;
        return OpenDoc(path);
    }

    /// A file from the search's results: a Markdown one in the document window when Windows opens .md files with this
    /// Bloom anyway (the user made it the default), anything else as Windows opens it.
    public void OpenFile(string path)
    {
        if (DocFile.IsMarkdown(path) && File.Exists(path) && FileAssoc.DefaultIsBloom()) OpenDoc(path);
        else Launch.Open(path);
    }
}
