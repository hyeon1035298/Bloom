using System;
using System.Collections.Generic;
using System.IO;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// What Ctrl+K does to a file or folder besides opening it: a real rename, a move or copy to another folder, the
/// Recycle Bin — each through Explorer's own file operation (its progress, its name-clash questions).
internal sealed partial class Engine
{
    private const char RenameGlyph = '', MoveGlyph = '';

    private IEnumerable<ActionItem> FileActions(string path, string name, bool folder)
    {
        yield return new ActionItem { Title = Tr("이름 바꾸기…", "Rename…"), Glyph = RenameGlyph, StayOpen = true, Run = () => host.Window.StartForm(RenameForm(path, name, folder)) };
        yield return new ActionItem { Title = Tr("다른 폴더로 옮기기…", "Move to another folder…"), Glyph = MoveGlyph, Run = () => host.MoveOrCopy(path, true) };
        yield return new ActionItem { Title = Tr("다른 폴더에 복사…", "Copy to another folder…"), Glyph = G.Copy, Run = () => host.MoveOrCopy(path, false) };
        yield return new ActionItem
        {
            Title = Tr("휴지통으로 보내기", "Move to Recycle Bin"),
            Glyph = G.Delete,
            Danger = true,
            StayOpen = true,
            Run = () => host.Window.Confirm(Tr($"‘{name}’을 휴지통으로 옮깁니다.", $"Move '{name}' to the Recycle Bin."), new ActionItem
            {
                Title = Tr("휴지통으로", "Move to bin"),
                Keys = "Enter",
                Glyph = G.Delete,
                Danger = true,
                StayOpen = true,
                Run = () =>
                {
                    bool ok = host.Recycle(path);
                    host.Window.Refresh(false);
                    host.Window.ShowToast(ok ? Tr($"‘{name}’ 휴지통 이동 완료 · 휴지통에서 복원 가능", $"Moved '{name}' to Recycle Bin · Can be restored from Recycle Bin")
                        : Tr("휴지통 이동 실패 · 파일을 사용하는 앱을 닫고 권한을 확인한 뒤 다시 시도하세요", "Failed to move to Recycle Bin · Close any app using the file, check permissions, and try again"));
                },
            }),
        };
    }

    /// The file's (or folder's) real name in its folder; afterwards the view searches for the new name.
    public FormSpec RenameForm(string path, string name, bool folder)
    {
        var back = host.Window.Mode == Mode.Form ? Mode.Root : host.Window.Mode;
        string dir = Path.GetDirectoryName(path.TrimEnd('\\')) ?? "";
        var f = new FormSpec { Title = folder ? Tr("폴더 이름 바꾸기", "Rename folder") : Tr("파일 이름 바꾸기", "Rename file"), Back = back };
        f.Fields.Add(new FormField
        {
            Label = Tr("새 이름", "New name"),
            Hint = folder ? Tr("새 폴더 이름", "New folder name") : Tr("새 파일 이름 — 확장자까지 적은 그대로 바뀝니다", "New file name — Changes exactly as typed, including extension"),
            Value = name,
            Check = v =>
            {
                string n = v.Trim();
                if (n.Length == 0) return (null, Tr("이름을 입력하세요", "Enter a name"));
                if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return (null, Tr("이름에 \\ / : * ? \" < > | 는 쓸 수 없습니다", "Names cannot contain \\ / : * ? \" < > |"));
                if (n == name) return (null, Tr("현재 이름과 같습니다. 다른 이름을 입력하거나 Esc를 누르세요.", "Same as the current name. Enter a different name or press Esc."));
                string to = Path.Combine(dir, n);
                if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase) && (File.Exists(to) || Directory.Exists(to)))
                    return (null, Tr($"‘{n}’ 이름이 이미 있습니다. 다른 이름을 입력하세요.", $"'{n}' already exists. Enter a different name."));
                return (n, null);
            },
        });
        f.Done = form =>
        {
            string n = form[0];
            bool ok = ShellFiles.Rename(path, n, IntPtr.Zero);
            host.Window.Go(back, ok ? Path.GetFileNameWithoutExtension(n) : "");
            host.Window.ShowToast(ok ? Tr($"‘{name}’ → ‘{n}’", $"‘{name}’ → ‘{n}’") : Tr("이름 변경 실패 · 파일을 사용하는 앱을 닫고 권한을 확인한 뒤 다시 시도하세요", "Rename failed · Close any app using the file, check permissions, and try again"));
        };
        return f;
    }
}
