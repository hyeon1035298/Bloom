using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Ui;

/// Files and pictures waiting for the next AI message, shared by the launcher's quick chat and the AI chat window:
/// what may go (no folders, at most 50 MB each, 10 at a time) and what a Ctrl+V holds.
internal static class ChatAttach
{
    /// Adds the files that may go to `into`; → how many were left out.
    public static int Add(List<string> into, IEnumerable<string> paths)
    {
        int skipped = 0;
        foreach (string p in paths)
        {
            if (!File.Exists(p) || into.Contains(p, StringComparer.OrdinalIgnoreCase) || new FileInfo(p).Length > Chats.MaxFileBytes || into.Count >= Chats.MaxFiles)
            {
                skipped++;
                continue;
            }
            into.Add(p);
        }
        return skipped;
    }

    public static string Skipped(int n) => Tr($"{n}개 첨부 실패 · 50 MB 이하 파일을 한 번에 {Chats.MaxFiles}개까지 선택하세요. 폴더는 첨부할 수 없습니다.", $"Failed to attach {n} file(s) · Select up to {Chats.MaxFiles} files under 50 MB each. Folders cannot be attached.");

    /// What a Ctrl+V in a conversation attaches: copied files, or a copied picture saved as a PNG first; null when the
    /// clipboard holds text (it pastes as usual) or nothing to attach.
    public static IReadOnlyList<string>? FromClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsFileDropList())
                return System.Windows.Clipboard.GetFileDropList().Cast<string>().ToList();
            if (System.Windows.Clipboard.ContainsImage() && !System.Windows.Clipboard.ContainsText() && System.Windows.Clipboard.GetImage() is { } img)
            {
                string file = Paths.InLocal(Tr($@"ai\paste\붙여넣은 그림 {DateTime.Now:HHmmss}.png", $@"ai\paste\Pasted picture {DateTime.Now:HHmmss}.png"));
                using (var fs = File.Create(file))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(img));
                    enc.Save(fs);
                }
                return new[] { file };
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            Log.Error("paste attachment", e);
        }
        return null;
    }

    /// The Windows file picker for files to send (several at once); null when cancelled.
    public static string[]? Pick(System.Windows.Window owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Tr("AI에게 보낼 파일", "Files to send to AI"), Multiselect = true };
        return dlg.ShowDialog(owner) == true ? dlg.FileNames : null;
    }

    /// The words sent when only files or a picture go: the AI is asked to look at them.
    public static string DefaultText(bool image, bool files) =>
        image ? Tr("이 화면에 뭐가 있는지 설명해줘", "Describe what is on this screen")
        : files ? Tr("첨부한 파일을 살펴보고 무엇인지 알려줘", "Examine the attached file and tell me what it is") : "";
}
