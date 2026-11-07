using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Addons;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set taskbar-folders`: 설정 > 애드온 whole, over a made-up taskbar (a state.json written into this dev copy's
/// add-on folder, the status drawn as running): four folders, none yet, the running folder off, and a command that
/// failed — in both themes and the run's language. Needs the add-on's addon.json and settings.schema.json next to Bloom.
internal static partial class Captures
{
    private static readonly (string id, string ko, string en)[] QaApps =
    {
        ("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "메모장", "Notepad"),
        ("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "계산기", "Calculator"),
        ("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "터미널", "Terminal"),
        ("Microsoft.WindowsCamera_8wekyb3d8bbwe!App", "카메라", "Camera"),
        ("Microsoft.MicrosoftPCManager_8wekyb3d8bbwe!App", "PC Manager", "PC Manager"),
        ("Microsoft.WindowsStore_8wekyb3d8bbwe!App", "Microsoft Store", "Microsoft Store"),
        ("Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe!App", "녹음기", "Sound Recorder"),
        ("Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", "Xbox", "Xbox"),
        ("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", "설정", "Settings"),
        ("Microsoft.SecHealthUI_8wekyb3d8bbwe!SecHealthUI", "Windows 보안", "Windows Security"),
        ("Microsoft.PowerShell_8wekyb3d8bbwe!App", "PowerShell", "PowerShell"),
    };

    private static async Task TaskbarFolderScreens(Host host, Launcher w, string dir)
    {
        string l = Lang.Code;
        var addon = AddonHub.Taskbar;
        if (addon.Values is not { } values)
        {
            Log.Write("qa taskbar-folders: no add-on schema next to Bloom");
            return;
        }
        Directory.CreateDirectory(addon.DataDir);
        string stateFile = Path.Combine(addon.DataDir, AddonProtocol.StateFile);
        addon.QaStatus = new AddonStatus(AddonState.Running);
        Settings.Current.AddonsOn.Add(addon.Id); // this dev copy's own settings only; nothing starts from it
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false, Height = 2300 };
        sw.Show();
        var folders = new (string ko, string en, int[] apps)[] { ("작업", "Work", new[] { 0, 1, 2, 3 }), ("관리", "Manage", new[] { 4, 5 }), ("미디어", "Media", new[] { 6, 7, 3 }), ("도구", "Tools", new[] { 8, 9, 10 }) };
        // a recording may seed its own folders for the folders shot: qa-folders.json next to state.json, in its shape,
        // with the folder to open as qaSelect
        string seeded = Path.Combine(addon.DataDir, "qa-folders.json");
        var mine = File.Exists(seeded) ? JsonNode.Parse(File.ReadAllText(seeded)) as JsonObject : null;
        string? pick = mine?["qaSelect"]?.GetValue<string>();
        mine?.Remove("qaSelect");
        var others = mine is null ? [] : AddonHub.Listed.ToList(); // a seeded page draws the catalog's add-ons on too
        foreach (var a in others)
        {
            a.QaStatus = new AddonStatus(AddonState.Running);
            Settings.Current.AddonsOn.Add(a.Id);
        }
        foreach (string theme in new[] { "light", "dark" })
        {
            Theme.Apply(theme);
            foreach (string name in new[] { "folders", "empty", "running-off", "failed" })
            {
                var state = name == "folders" && mine is not null ? mine : QaState(name == "empty" ? [] : folders, name != "running-off");
                File.WriteAllText(stateFile, state.ToJsonString());
                values.Set("folders.autoRunning", name != "running-off");
                values.Flush();
                sw.TaskbarForQa(name == "running-off" ? TaskbarEditor.Running : name == "folders" ? pick : null,
                    name == "failed" ? TaskbarEditor.ErrorText(Tr("이름 바꾸기", "Rename"), "exists") : null);
                await TaskbarShot(sw, Path.Combine(dir, $"{l}-{theme}-taskbar-{name}.png"), SettingsView.AddonPageKey(addon.Id));
            }
        }
        values.Set("folders.autoRunning", true);
        values.Flush();
        File.Delete(stateFile);
        addon.QaStatus = null;
        Settings.Current.AddonsOn.Remove(addon.Id);
        foreach (var a in others)
        {
            a.QaStatus = null;
            Settings.Current.AddonsOn.Remove(a.Id);
        }
        sw.Close();
    }

    /// `--qa-set taskbar-usage`: the same page over made-up usage (Claude with a Fable week, ChatGPT with one limit), for
    /// its 트레이 rows: each account's bar, 이름표, 아이콘 색 — both themes, the run's language.
    private static async Task TaskbarUsageScreens(Host host, Launcher w, string dir)
    {
        string l = Lang.Code;
        var addon = AddonHub.Taskbar;
        addon.QaStatus = new AddonStatus(AddonState.Running);
        Settings.Current.AddonsOn.Add(addon.Id); // this dev copy's own settings only; nothing starts from it
        Settings.Current.ExportUsage = true; // set directly: no lookup starts from a QA run
        Settings.Current.UsageTag = "name";
        var resets = DateTime.Now.AddHours(3);
        Ai.AiUsage.SetForQa(
            new Ai.UsageAccount("Claude", "Max", new() { new("5시간", 32, resets, Ai.UsageLimit.SessionMins), new("주간", 47, resets, Ai.UsageLimit.WeekMins), new("Fable 주간", 61, resets, Ai.UsageLimit.WeekMins, "Fable") }, DateTime.Now) { Id = AiAccounts.DefaultClaude },
            new Ai.UsageAccount("ChatGPT", "Pro 200", new() { new("주간", 21, resets, Ai.UsageLimit.WeekMins) }, DateTime.Now) { Id = AiAccounts.DefaultChatGpt });
        var sw = new SettingsWindow(host) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false, Height = 2300 };
        sw.Show();
        foreach (string theme in new[] { "light", "dark" })
        {
            Theme.Apply(theme);
            await TaskbarShot(sw, Path.Combine(dir, $"{l}-{theme}-taskbar-usage.png"), SettingsView.AddonPageKey(addon.Id));
        }
        addon.QaStatus = null;
        Settings.Current.AddonsOn.Remove(addon.Id);
        sw.Close();
    }

    /// state.json as the mod writes it (protocol §5), with the apps drawn from the shell's app list.
    private static JsonObject QaState((string ko, string en, int[] apps)[] folders, bool running)
    {
        JsonObject App(int i) => new() { ["file"] = Tr(QaApps[i].ko, QaApps[i].en) + ".lnk", ["name"] = Tr(QaApps[i].ko, QaApps[i].en), ["appId"] = QaApps[i].id };
        var pinned = new JsonArray { new JsonObject { ["kind"] = "app", ["name"] = Tr("파일 탐색기", "File Explorer"), ["appId"] = "Microsoft.Windows.Explorer" } };
        var items = new JsonObject();
        foreach (var f in folders)
        {
            string n = Tr(f.ko, f.en);
            pinned.Add(new JsonObject { ["kind"] = "folder", ["name"] = n });
            items[n] = new JsonObject { ["icon"] = new JsonObject { ["kind"] = "auto" }, ["apps"] = new JsonArray(f.apps.Select(i => (JsonNode)App(i)).ToArray()) };
        }
        if (folders.Length == 0)
            foreach (int i in new[] { 4, 0, 1 }) pinned.Add(new JsonObject { ["kind"] = "app", ["name"] = Tr(QaApps[i].ko, QaApps[i].en), ["appId"] = QaApps[i].id });
        pinned.Add(new JsonObject { ["kind"] = "running" });
        return new JsonObject
        {
            ["v"] = 1,
            ["pid"] = 1,
            ["folders"] = new JsonObject { ["enabled"] = true, ["path"] = @"%USERPROFILE%\TaskbarFolders" },
            ["pinned"] = pinned,
            ["items"] = items,
            ["running"] = new JsonObject { ["enabled"] = running, ["name"] = Tr("실행 중", "Running"), ["apps"] = running ? new JsonArray(App(2), App(5)) : new JsonArray() },
        };
    }
}
