using System;
using Bloom.Core;
using Bloom.Native;
using Bloom.Search;

namespace Bloom.App;

/// The launcher's own commands (cmd:…).
internal sealed partial class Host
{
    // ── commands ────────────────────────────────────────────────────────────────────────────────
    /// An item's own shortcut was pressed: it runs as if picked in the launcher (a gone app or file says so on a card).
    public void RunItemKey(Favorites.Entry entry)
    {
        if (Engine.FromFavorite(entry) is not { Run: { } run } item)
        {
            Notices.Result(false, Tr("단축키 · 오류", "Shortcut · Error"), entry.Title, Tr("항목을 찾지 못했습니다. 위치를 확인하거나 단축키를 다시 지정하세요.", "Item not found. Check its location or reassign the shortcut."));
            return;
        }
        try
        {
            run();
        }
        catch (Exception e)
        {
            Log.Error("item key " + item.Id, e);
        }
    }

    public void RunCommand(string id)
    {
        switch (id)
        {
            case "cmd:ai":
                OpenChatWindow();
                break;
            case "cmd:threads":
                OpenView(Mode.Threads);
                break;
            case "cmd:files":
                OpenView(Mode.Files);
                break;
            case "cmd:clipboard":
                OpenView(Mode.Clipboard);
                break;
            case "cmd:calendar":
                OpenView(Mode.Calendar);
                break;
            case "cmd:quicklinks":
                OpenView(Mode.Quicklinks);
                break;
            case "cmd:commands":
                OpenView(Mode.Commands);
                break;
            case "cmd:processes":
                OpenProcesses();
                break;
            case "cmd:space":
                OpenSpace(null);
                break;
            case "cmd:notes":
                Tools.Open("notes");
                break;
            case "cmd:player":
                Tools.Open("player");
                break;
            case "cmd:tools":
                Window.OpenTools();
                break;
            case "cmd:lens":
                StartLens("menu");
                break;
            case "cmd:pin":
                StartLens("pin");
                break;
            case "cmd:capture":
                StartLens("capture");
                break;
            case "cmd:windows":
                OpenWindows.Invalidate();
                OpenView(Mode.Windows);
                break;
            case "cmd:winget":
                OpenView(Mode.Winget);
                break;
            case "cmd:layout":
                OpenView(Mode.Layout);
                break;
            case "cmd:emoji":
                OpenView(Mode.Emoji);
                break;
            case "cmd:recent":
                OpenView(Mode.Recent);
                break;
            case "cmd:system":
                OpenView(Mode.System);
                break;
            case "cmd:quick":
                StartQuick(false);
                break;
            case "cmd:timer":
                OpenView(Mode.Timers);
                break;
            case "cmd:color":
                StartColor(); // the screen first (magnifier + code under the arrow); the colour view after the click
                break;
            case "cmd:export":
                Window.Dismiss();
                ExportSettings(null);
                break;
            case "cmd:import":
                Window.Dismiss();
                ImportSettings(null);
                break;
            case "cmd:welcome":
                Window.Dismiss();
                ShowOnboarding();
                break;
            case "cmd:settings":
                OpenSettings();
                break;
            case "cmd:reindex":
                Apps.Rescan();
                Window.ShowToast(Tr("앱 목록을 다시 읽는 중…", "Reading the app list again…"));
                break;
            case "cmd:lock":
                Window.Dismiss();
                Win32.LockWorkStation();
                break;
            case "cmd:restart":
            case "cmd:shutdown":
                // asked once more, as the Start menu's power button is two clicks away
                bool restart = id == "cmd:restart";
                Window.Confirm(restart ? Tr("PC를 다시 시작합니다. 저장하지 않은 내용이 사라질 수 있습니다.", "Restarting the PC may discard unsaved work.") : Tr("PC를 종료합니다. 저장하지 않은 내용이 사라질 수 있습니다.", "Shutting down the PC may discard unsaved work."), new ActionItem
                {
                    Title = restart ? Tr("다시 시작", "Restart") : Tr("시스템 종료", "Shut down"),
                    Keys = "Enter",
                    Glyph = restart ? G.Restart : G.Power,
                    Danger = true,
                    Run = () => Power(restart),
                });
                break;
            case "cmd:quit":
                QuitForGood();
                break;
        }
    }

    private void Power(bool restart)
    {
        Log.Write(restart ? "power: restart" : "power: shut down");
        SaveAll();
        if (Win32.ExitWindows(restart)) return;
        Log.Write("power: refused");
        Summon();
        Window.ShowToast(restart ? Tr("다시 시작 실패 · 작업을 저장하고 Windows 시작 메뉴에서 다시 시작하세요", "Restart failed. Save your work and restart from the Windows Start menu.") : Tr("시스템 종료 실패 · 작업을 저장하고 Windows 시작 메뉴에서 종료하세요", "Shutdown failed. Save your work and shut down from the Windows Start menu."));
    }
}
