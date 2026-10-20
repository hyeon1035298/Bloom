using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

namespace Bloom.Core;

internal sealed class Settings
{
    public string Theme { get; set; } = "system"; // system | light | dark
    public string Language { get; set; } = "auto"; // auto (Windows' display language) | ko | en | ja | zh — read once per run (Core\Lang.cs)
    public bool? Onboarded { get; set; } // the first-run screens were seen (null: a settings file from before they existed)
    public string IconStyle { get; set; } = "tile"; // favorites' picture icons: soft (rose tint) | tile (Bloom's neutral tile) | card (raised card) | black
    public string Accent { get; set; } = "rose"; // Ui.Accents key: rose | lavender | sky | mint | tangerine | lemon | gray | custom
    public string? AccentCustom { get; set; } // "#RRGGBB" for Accent = "custom"
    public bool WinKey { get; set; } = true;
    public bool WinKeyAdmin { get; set; } // the Win key over apps run as administrator: the elevated helper (App\ElevatedWinKey.cs)
    public bool AltSpace { get; set; } = true;
    public bool Autostart { get; set; } = true;
    public bool UpdateCheck { get; set; } = true; // one look at GitHub Releases a little after each start (App\Updates.cs)
    public bool FilesInRoot { get; set; } = true;
    public bool WindowsInRoot { get; set; } = true; // open windows whose title matches show under the results
    public bool LearnQueries { get; set; } = true; // what was picked for what was typed ranks first next time (Core\QueryMemory.cs)
    public string WindowsHotkey { get; set; } = "off"; // 창 전환 straight from anywhere
    public string EmojiHotkey { get; set; } = "off"; // 이모지 · 기호
    public string QuickHotkey { get; set; } = "ctrl+alt+a"; // AI 빠른 동작 on the selected text
    public string ChatHotkey { get; set; } = "ctrl+alt+c"; // the AI 채팅 window: open / bring forward / tuck away
    public List<ItemKey> ItemKeys { get; set; } = new(); // shortcuts given to single items (Core\ItemKeys.cs)
    public string WebBrowser { get; set; } = "default"; // default | a Browser.Web.Installed key — where searches and links open
    public string SearchEngine { get; set; } = "google"; // Browser.Web.Engines key
    public string AiProvider { get; set; } = "claude"; // claude | chatgpt | omp | api: the side new conversations use
    public string OmpChatModel { get; set; } = ""; // omp: "provider/model" for the chat (`omp models`); empty = omp's own default model
    public string OmpChatEffort { get; set; } = "medium"; // omp: off | minimal | low | medium | high | xhigh | max (fitted to the model's levels)
    public string OmpFastModel { get; set; } = ""; // omp: translation and AI 빠른 동작; empty = omp's own default model
    public string OmpFastEffort { get; set; } = "off";
    public bool OmpSearch { get; set; } = true; // omp chats may search the web (their only tool of omp's own)
    public List<Ai.OmpModel> OmpModels { get; set; } = new(); // omp: the user's own model list (Ai\OmpModelList.cs); empty = the menus show omp's own default model only
    public string? ApiModel { get; set; } // API: an AiModels.Api id ("api:<provider>:<model>"); null = none picked yet
    public string ApiEffort { get; set; } = "auto"; // API: auto (none sent) or a level the provider takes (ApiProviders)
    public string AiModel { get; set; } = "sonnet"; // Claude: sonnet | opus | haiku
    public string AiEffort { get; set; } = "fast"; // Claude: fast (thinking off) | low | medium | high | xhigh | max
    public string? GptModel { get; set; } // ChatGPT: a Codex model slug; null = Codex's own default
    public string GptEffort { get; set; } = "low"; // ChatGPT: the model's reasoning levels (low … ultra)
    public bool TabContinuesLastThread { get; set; } = true;
    public string? SeenVersion { get; set; } // the version Bloom last started as: a newer one shows 바뀐 점 once (ReleaseNotes)
    public bool GpuDrawing { get; set; } // WPF draws with the GPU; off = software drawing, ~30 MB less (measured 10-09), read at start. Off by default since 10-14: GPU drawing froze the whole screen for seconds on one PC, software drawing kept the same fps there
    public int AiIdleMinutes { get; set; } = 10; // a chat's CLI closes after this long unused (its memory back); 0 = stays open
    public bool AiTitles { get; set; } = true; // the AI names each task "[◆ Opus • Code ] …" (ChatTitles)
    public bool AiTools { get; set; } = true; // Claude chats get Bloom's MCP tools (actions asked in the chat first)
    public bool AiToolsGpt { get; set; } = true; // ChatGPT (Codex) chats get them too (Bloom asks in the chat itself)
    public bool AiToolsApi { get; set; } = true; // API chats get them too, as a function (Bloom asks in the chat itself)
    public bool AiToolsOmp { get; set; } = true; // omp chats get them too, through .omp/mcp.json (Bloom asks in the chat itself)
    public Dictionary<string, string> Permissions { get; set; } = new(); // 설정 > PC 작업 · MCP > 권한: action → ask | allow | off; missing = ask (Core\ToolPermissions.cs)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ApiFileTools { get; set; } // a settings file from before 권한: its switch off reads once as the four file and command actions off
    public List<string> AiToolsOff { get; set; } = new(); // BloomTools groups the AI may not use: look | open | files | clip
    public List<string> McpOn { get; set; } = new(); // other MCP servers the chats may use: "source|name" (Ai\McpCatalog.cs)
    public List<string> McpFiles { get; set; } = new(); // .mcp.json files brought in on 설정 > PC 작업 · MCP
    public List<string> FeaturesOff { get; set; } = new(); // Core\Features.cs keys switched off in 설정 > 기능
    public string? ClaudePath { get; set; }
    public string? CodexPath { get; set; }
    public string? OmpPath { get; set; } // omp.exe; null = the PATH
    public List<HomeWidget>? Home { get; set; } // the empty view's blocks, in order (Core\Home.cs)
    public int UsageMinutes { get; set; } = 10; // the home's AI usage: asked of Claude Code / Codex every 5 or 10 minutes (App\AiUsage.cs)
    public bool UsageLeft { get; set; } // the usage cards count what's left ("68% 남음", the bar shrinking) instead of what's used
    public List<AiAccount> AiAccounts { get; set; } = new(); // whose usage the home shows (Core\AiAccounts.cs); the two defaults are added on read
    public string ClaudeChatAccount { get; set; } = "claude"; // the AiAccount id AI chat, Bloom Lens and translation use for Claude
    public string GptChatAccount { get; set; } = "chatgpt"; // … and for ChatGPT (Codex)
    public string ChatGptUsage { get; set; } = "cli"; // cli (Codex's app-server per account) | light (one HTTPS request with the account's Codex sign-in, Ai\ChatGptLight.cs)
    public bool ClipHistory { get; set; } = true;
    public int ClipKeepMinutes { get; set; } = 30 * 24 * 60; // how long unpinned entries stay; 0 = for good (pinned ones always stay)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ClipKeepDays { get; set; } // a settings file from before minutes: read once into ClipKeepMinutes
    public string ClipHotkey { get; set; } = "ctrl+alt+v"; // off | ctrl+alt+v | ctrl+shift+v
    public bool TimerSound { get; set; } = true; // a due timer / reminder rings (Windows Alarm01) besides the card
    public string ColorHotkey { get; set; } = "off"; // the colour picker straight from anywhere
    public bool ColorCopy { get; set; } = true; // a picked colour's HEX goes to the clipboard at once
    public string LensHotkey { get; set; } = "ctrl+alt+l"; // Bloom Lens: pick an area → what to do with it
    public string AskHotkey { get; set; } = "off"; // Bloom Lens straight to "AI에게 묻기"
    public string LiveHotkey { get; set; } = "off"; // Bloom Lens straight to live translation
    public string CaptureHotkey { get; set; } = "off"; // Bloom Lens straight to "캡처" (the picture to the clipboard)
    public string OcrHotkey { get; set; } = "ctrl+alt+o"; // Bloom Lens straight to "글자 복사"
    public string TranslateHotkey { get; set; } = "ctrl+alt+t"; // Bloom Lens straight to "번역"
    public string OcrLanguage { get; set; } = "auto"; // auto (the user's languages) | an installed recognizer tag
    public bool OcrCopy { get; set; } = true; // recognized text goes to the clipboard at once
    public string DrawTool { get; set; } = "pen"; // 화면 캡처's drawing: pen | mark (the highlighter) in hand last
    public int DrawPenColor { get; set; } // 0 red · 1 yellow · 2 blue · 3 green
    public int DrawMarkColor { get; set; } = 1;
    public int DrawPenSize { get; set; } = 1; // 0 thin · 1 medium · 2 thick
    public int DrawMarkSize { get; set; } = 1;
    public string TranslateProvider { get; set; } = "claude"; // claude (Haiku) | chatgpt | api
    public string? TranslateApiModel { get; set; } // an AiModels.Api id; null = the first API model listed
    public string TranslateApiEffort { get; set; } = "auto"; // auto (none sent) or a level the provider takes
    public string? TranslateGptModel { get; set; } // a Codex slug; null = the lightest in the catalog
    public string TranslateClaudeModel { get; set; } = "haiku"; // haiku | sonnet | opus
    public string TranslateClaudeEffort { get; set; } = "fast"; // fast (thinking off) … max; Haiku has none
    public string TranslateGptEffort { get; set; } = "minimal"; // fitted to the model's lowest it takes
    public string TranslateTo { get; set; } = "auto"; // auto (Korean → English, else → Korean; Translator.TargetFor) | ko | en | ja | zh
    public double LiveSeconds { get; set; } = 1.5; // realtime translation: how often the area is read
    // 알림: Claude Code / Codex turns and plan limits as cards in a screen corner (App\Notices.cs)
    public bool Notices { get; set; } = true;
    public string NoticeSpot { get; set; } = "bottom-right"; // top-left | top-center | top-right | bottom-left | bottom-center | bottom-right
    public string NoticeSize { get; set; } = "m"; // s | m | l | xl
    public int NoticeSeconds { get; set; } = 6; // how long a card stays (승인 대기 stays while it waits)
    public string NoticeSound { get; set; } = "m"; // off | s | m | l (volume)
    public bool NoticeStart { get; set; } = true; // 진행 중: a turn started (what it works on)
    public bool NoticeDone { get; set; } = true; // 작업 완료
    public bool NoticeWaiting { get; set; } = true; // 승인 대기
    public bool NoticeApprove { get; set; } // 알림 카드에서 승인 (App\ClaudeApprovals): Bloom's hook in Claude Code's settings.json while on
    public bool NoticeLimit { get; set; } = true; // 한도 임박 · 소진 (20 % left, used up)
    public bool NoticeRecover { get; set; } = true; // 한도 복구 (a low limit back after its reset)
    // 성능 (Ui\Theme\Motion.cs)
    public string Animation { get; set; } = "system"; // system (Windows' animation effects) | on | reduced (fades only) | off
    public int AnimationFps { get; set; } // 0 = the screen's own rate | 30 | 60
    public string Transparency { get; set; } = "system"; // system (Windows' transparency effects) | off (solid, lighter on the GPU)
    public int GlassTransparency { get; set; } = 30; // 설정 > 성능 > 유리 투명도: % of the screen behind that shows through the glass (15 … 75; Ui\Theme\Palette.cs Tint)
    public double Scale { get; set; } = 1; // 설정 > 일반 > 크기: 0.8 … 1.5 (Ui\Theme\Theme.cs Zoom)
    public int MdTextSize { get; set; } = MdTextDefault; // 메모 and 문서's text, Markdown or being written: Ctrl+wheel, Ctrl+0 back to the default (Ui\MdPane.cs)
    public const int MdTextMin = 10, MdTextMax = 28, MdTextDefault = 14;
    public int[] DocSize { get; set; } = { 760, 720 }; // the last document window's width and height (Ui\DocWindow.cs): dip at 100 % (Scale multiplies it)
    public List<ToolPlace> Tools { get; set; } = new(); // the open tool windows (Ui\Tools\ToolDock.cs)
    // 미니 플레이어 (Music\MusicPlayer.cs)
    public string PlayerMode { get; set; } = "direct"; // direct (Bloom plays the file) | jriver | foobar | musicbee (PlayerWays)
    public int PlayerVolume { get; set; } = 70; // 0 … 100, direct playing
    public string PlayerRepeat { get; set; } = "off"; // off | all | one (Music\PlayQueue.cs Repeat), direct playing
    public bool PlayerShuffle { get; set; } // 무작위, direct playing
    public string JriverAddress { get; set; } = ""; // JRiver's web service (MCWS); empty: http://localhost:52199. Its password is in ApiKeys (MusicPlayer.JriverKey)
    public string JriverUser { get; set; } = "";
    public string FoobarPath { get; set; } = ""; // foobar2000.exe; empty: found by itself (PlayerExe)
    public string MusicBeePath { get; set; } = ""; // MusicBee.exe; empty: found by itself
    public int Width { get; set; } = DefaultWidth; // the launcher as last sized by hand: dip at 100 % (Scale multiplies it)
    public int Height { get; set; } = DefaultHeight;
    public const int DefaultWidth = 760, DefaultHeight = 476; // 설정 > 일반 > 런처 창 크기 puts it back to these

    [JsonIgnore] private static Saver? saver;
    [JsonIgnore] public static Settings Current { get; private set; } = new();

    public static void Load()
    {
        saver?.Dispose();
        bool existed = File.Exists(Paths.In("settings.json"));
        Current = Json.Load<Settings>(Paths.In("settings.json"));
        Current.Onboarded ??= existed; // someone who already used Bloom doesn't get the first-run screens
        // the clipboard history was kept by days before it was kept by minutes
        if (Current.ClipKeepDays is { } days)
        {
            Current.ClipKeepMinutes = Math.Max(0, days) * 24 * 60;
            Current.ClipKeepDays = null;
        }
        Current.MoveApiFileTools();
        // 0.9.2 saved its default of 5 idle minutes: a restarted Claude CLI rewrites its whole prompt cache, so 0.9.3 is
        // back at 10 (the first start after 0.9.2 only; SeenVersion moves on at that start)
        if (Current.SeenVersion == "0.9.2" && Current.AiIdleMinutes == 5) Current.AiIdleMinutes = 10;
        Lang.Init(Current.Language);
        // before Claude and ChatGPT had a model each, "chatgpt" was one of the models
        if (Current.AiModel.StartsWith("chatgpt", StringComparison.Ordinal))
        {
            Current.AiProvider = "chatgpt";
            Current.AiModel = "sonnet";
        }
        saver = Saver.ForJson(Paths.In("settings.json"), () => Current);
    }

    /// The API chats' file and command tools had one switch before each action had its 권한: off is the four of them
    /// 사용 안 함 (read once; the switch is gone after).
    internal void MoveApiFileTools()
    {
        if (ApiFileTools is not { } files) return;
        if (!files)
            foreach (string action in ToolPermissions.FileActions) Permissions[action] = ToolPermissions.Off;
        ApiFileTools = null;
    }

    private static int version;

    /// How many times the settings were saved this run: a cache built from them (Ui\Settings\SettingsCatalog.cs) is
    /// built again once it moves.
    public static int Version => Volatile.Read(ref version);

    public static void Save()
    {
        Interlocked.Increment(ref version);
        saver?.Touch();
    }

    public static void Flush() => saver?.Flush();
}
