<p align="center"><img src="promo/bloom-github.png" alt="Bloom with its cherry-blossom mark, the line Search and AI on one line, the home with AI usage and token cost, and the pill taskbar"></p>

<p align="center">Bloom is a native Windows launcher that opens apps, does sums, finds files, chats with AI and translates the screen from one search line.</p>

<p align="center">
  <a href="#download">Download</a> | <a href="#features">Features</a> | <a href="#add-ons">Add-ons</a> | <a href="#shortcuts">Shortcuts</a> | <a href="#about-the-ai-features">AI and privacy</a> | <a href="README.ko.md">한국어</a> | <a href="README.ja.md">日本語</a> | <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <img src="docs/media/en/loop.webp" alt="Bloom demo showing finding an app, maths, a unit, a reminder, emoji, clipboard history, the color picker, asking AI, a PC task approved on its card, notes attached beside the launcher, a work notification, screen translation and Space Cleaner">
</p>

<p align="center"><sub>Native WPF on .NET 10, no WebView, in English, 한국어, 日本語 and 简体中文, MIT license</sub></p>

## Why Bloom

- One key opens it, and one search line answers many things.
- AI runs on your own Claude Code or Codex sign-in, or on API keys you add.
- A feature you switch off leaves the menus, frees its shortcut and takes no memory.

## Download

**[Get Bloom for Windows](https://github.com/hyeon1035298/Bloom/releases/latest)** and run `BloomLauncher-win-Setup.exe` from the latest release.

Or run this in PowerShell, which downloads the same setup and starts it ([install.ps1](install.ps1)).

```powershell
irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex
```

- Bloom needs Windows 10 or 11, 64-bit. The setup installs the .NET 10 Desktop Runtime when the PC lacks it, and Windows asks for permission for that part.
- Bloom installs for your account only, into `%LOCALAPPDATA%\BloomLauncher`, with a Start menu shortcut.
- To remove it, use Settings › Data › Uninstall Bloom, which asks whether your data in `%APPDATA%\Bloom` goes too. Windows Settings › Apps also removes it and keeps the data.
- Bloom is not code-signed, so SmartScreen may say "Windows protected your PC". Choose More info, then Run anyway.
- Bloom checks this repository's latest release once after it starts. A new version downloads in the background and goes in at the next start, or at once with Restart to update. Settings › About has the check and the switch for it.

## Features

### One search line

<img src="docs/media/en/search.webp" alt="The launcher converting 5.9 ft to cm, with cards on initials, maths and units, reminders and emoji">

- Apps, Windows settings pages and Bloom commands open from part of a name or its initials (`gc` for Google Chrome). Korean initial consonants work too (`ㅁㅁㅈ` for 메모장), and so does a word typed with the keyboard left in Korean.
- Sums, units and currencies update as you type, and Enter copies the answer. Try `45000*1.1`, `5!`, `5.9 ft to cm` or `100 usd`.
- Timers and reminders take plain time words, as in `timer 5m`, `laundry in 30 min` or `remind me at 3pm to call`. They show on the calendar and in the home's This week.
- Emoji and symbols come by English or Korean name (`:heart`, `arrow`, `celsius`).
- Anything else goes to your browser as a web search, and a keyword sends it to a site (`yt cats`, `gh bloom`). You can make your own site shortcuts.
- With [Everything](https://www.voidtools.com/) installed, files are found by name at once and can be previewed. Without it, Bloom searches Windows' recent files.
- Open windows are found by title, and Enter switches to one.
- Ctrl+K on any result lists what you can do with it, such as renaming, moving or copying a file the way Explorer does. Type `?` alone to see every kind of input Bloom understands.

### Paths, windows, dates and installs

<img src="docs/media/en/more.webp" alt="The window layout list with drawn icons for halves and quarters, with cards on paths, window layout, dates and world clock, and installing apps">

- Type a path such as `C:\Users`, `~\Downloads` or `%APPDATA%` to see that folder and what is in it. Enter opens it in Explorer.
- `left half`, `right third`, `top-left quarter`, `maximize`, `center` and `next screen` move the window in front, with the taskbar kept clear.
- Dates and the world clock answer `in 100 days`, `days until christmas`, `2026-01-01 ~ 2026-12-25` and `tokyo 3pm`.
- `install vlc` searches winget and the Microsoft Store, asks first, then installs in the background and shows the result on a card.
- Ctrl+K › Set a shortcut gives an app, folder, file, site, command or window layout a global shortcut of its own.
- Right-click an app, or press Ctrl+K, to uninstall it.

### AI chat

<img src="docs/media/en/ai.webp" alt="A chat asking Allow or Decline before it moves a PDF into Documents, and work notification cards for Claude Code and Codex">

- Type a question and press Tab for the quick AI right in the launcher. Shift+Tab starts a new chat, and Esc goes back while the answer keeps going.
- The AI chat window (Ctrl+Alt+C) keeps past chats in groups and takes attached files and pictures.
- Answers come from the Claude Code or Codex CLI signed in on this PC, from [omp](https://github.com/can1357/oh-my-pi) when it is installed, or from an API key for OpenAI, Anthropic, Gemini, OpenRouter, DeepSeek, xAI or OpenCode Go. Keys are encrypted by Windows and go only to their own provider.
- Hand off PC tasks such as "move the quote PDF I got yesterday into Documents". Each action that changes something waits for Allow or Decline on a card in the chat.
- "Write my shopping list in a note", "a 25 minute timer" and "a meeting tomorrow at 3pm" go into Bloom's own notes, timers and calendar.
- AI actions (Ctrl+Alt+A) summarize, translate, polish or rewrite the selected text in any app and paste it back.
- Work notifications show a card and play a sound when a Claude Code or Codex task starts, finishes, waits for approval or nears its limit. A click on the card goes to the window the task runs in, and with Approve on the card turned on you can allow or decline Claude Code's requests there.

### The home

<img src="docs/media/en/home.webp" alt="A wide home in two columns with usage cards for three Claude accounts and one ChatGPT account, token cost and its chart, the player, favorites and this week">

The empty search line shows the home, with favorites, AI usage, this week, timers, recently used items, notes, the player and commands.

- AI usage cards show Claude's and ChatGPT's 5-hour and weekly limits, plus Claude's Fable weekly line. Each card has its company's logo and a plan badge, and offers Sign in again when a sign-in has run out.
- Several accounts of each kind can be added in Settings › AI accounts. Each signs in with the CLI's own login, and nothing switches accounts on its own.
- Four token blocks count the tokens in the Claude Code and Codex logs on this PC, with what they would cost at API prices, a chart, and the most used models and sessions. They are off at first.
- Favorites open with Ctrl+1 to Ctrl+9 and can be grouped in folders.
- Ctrl+E edits the home. Drag blocks, hide them, add them back from + Add block, and use two columns in a wide launcher.

### Bloom Lens and the color picker

<img src="docs/media/en/lens.webp" alt="A Japanese message on the screen picked with Bloom Lens and translated into English on a card">

- Bloom Lens (Ctrl+Alt+L) lets you drag over part of the screen and pick what to do with keys 1 to 7.
  - `1` ask AI, `2` translate, `3` copy the text with Windows OCR, `4` translate live as the area changes
  - `5` pin the area on top as a picture, `6` capture it to the clipboard and your Screenshots folder, `7` make a color palette
- Translate (Ctrl+Alt+T) and copy text (Ctrl+Alt+O) have shortcuts of their own.
- The color picker takes a color from the screen with a magnifier and shows HEX, RGB, HSL and its contrast with white and black text.
- The palette picks the main colors of an area, 5 by default and 3 to 10 with − and +. It is worked out on this PC with no AI usage.

### Docked tools

<img src="docs/media/en/tools.webp" alt="The pinned launcher with notes attached on its right and the calculator under it">

- Ctrl+J opens notes, timers, the calculator, clipboard history and shortcuts as small windows.
- Drop a tool beside the launcher and it stays attached, moving and hiding with it. Pull it away and it floats on its own.
- Notes are Markdown with checklists, and `note buy milk` in the search line keeps one at once.
- Clipboard history (Ctrl+Alt+V) keeps the text, pictures and files you copied, and you can pin the ones to keep.
- Ctrl+P keeps the launcher on top. Drag its edges to size it, and Ctrl+= or Ctrl+- scales all of Bloom from 80 to 150 %.
- Bloom opens .md files in its own document window, which saves as you type.

### Bloom Space Cleaner

<img src="docs/media/en/space.webp" alt="A map of a folder where videos, downloads, photos and projects are blocks as big as they are, with a list of the biggest files">

- Bloom Space Cleaner shows a drive or folder as a map where each block is as big as what it holds.
- Click a block to go inside, and send big files from the list beside it to the Recycle Bin.
- It works below drive roots and outside the Windows folder, and everything it removes can be restored from the Recycle Bin.

### Settings

<img src="docs/media/en/settings.webp" alt="General settings with the theme and accent color, and the Data page listing what Bloom keeps by size">

- Settings has 12 pages, from General and Launcher to Add-ons, Data and About. Type a setting's name in the search line to go to it.
- The accent color can be any color. The picker shows its contrast in the dark and light themes, and the accent appears only on what is on.
- The Data page lists what Bloom keeps on this PC with its size, and lets you delete each part or reset Bloom.
- Features lets you switch off what you don't use, and its saved data stays.
- Custom commands run scripts, paste snippets (`{date}`, `{clipboard}`) or run chains of search lines (`notepad ; volume 30 ; yt lofi`) by a keyword.
- The look follows dark, light or the system theme, with animation and glass settings. GPU drawing is experimental and off at first.
- The interface comes in English, 한국어, 日本語 and 简体中文, and following Windows is the default. Search words, times and units are typed in English or Korean in every language.
- Export the settings to one file to move them to another PC. Settings › About › Problem report copies the version and a recent log with names and e-mail addresses hidden, then opens a GitHub issue page. Bloom sends nothing itself.

## Add-ons

<img src="docs/media/en/addons.webp" alt="Settings › Add-ons with the taskbar folders editor and a preview of the pill taskbar">

An add-on is a separate program that Bloom downloads, checks, starts and talks to through files. None of its code is in Bloom, which stays MIT. Both add-ons below are GPL-3.0 with their own source repositories.

### Taskbar (PillTaskbar)

PillTaskbar turns the Windows 11 taskbar into a centered glass pill. While it is on, Settings › Add-ons holds its settings.

- Taskbar folders let you make a folder, rename it, give it an icon, add and take out apps, order them and ungroup it. Dragging one app onto another and holding it there makes a folder too.
- An automatic Running folder gathers the apps you run without pinning.
- The look covers the size, the background and a Start button that is Bloom's flower and opens Bloom.
- The tray, the clock in 24-hour or 12-hour time and the song playing now sit at the end of the pill.
- AI usage shows one limit per account, the 5-hour, weekly or Fable weekly one, with its label and icon color.
- The auto-hide island hides the taskbar and shows a black or white island at the top center of the screen.

Its first start downloads the taskbar's symbol files (about 70 MB) from Microsoft's symbol server. If Explorer keeps crashing, Bloom turns the add-on off and says so.

### Alt+Tab per monitor

With more than one monitor, Alt+Tab and Win+Alt+Tab open on the monitor under the cursor and list only its windows.

### How add-ons are installed

- Settings › Add-ons downloads each one with one Download button and shows the progress.
- Bloom installs it only when the zip has the SHA-256 this Bloom pins and every file matches its `addon.json`. A failed download leaves nothing behind and says why.
- When a newer Bloom pins a newer version, the same line offers Update.
- Add-ons are off at first, and the first switch-on explains what the add-on does.
- If the same mod is running in Windhawk, the status line says so and what to do.
- Hold Shift while Bloom starts to skip add-ons for that run. While an add-on and Start with Windows are both on, a sign-in task of your own starts Bloom earlier.

### Your own add-ons

An add-on put in `%LOCALAPPDATA%\Bloom\user-addons` shows on Settings › Add-ons after a restart. It is off at first, and the first switch-on warns that Bloom did not make or check it. Its files are still checked against the SHA-256 in its `addon.json`.

## Shortcuts

| Keys | What it does |
| --- | --- |
| `Win` or `Alt+Space` | Open Bloom (`Win` over apps run as administrator needs Settings › General › Win key over admin apps) |
| `Tab` / `Shift+Tab` | Ask AI what is in the search line, in the same chat or a new one |
| `Ctrl+K` | Everything you can do with the picked result |
| `Ctrl+1` … `Ctrl+9` | Favorites |
| `Ctrl+J` | Tools (notes, timers, calculator, clipboard history, shortcuts) |
| `Ctrl+P` | Keep the launcher on top |
| `Ctrl+E` | Edit the home |
| `Ctrl+,` | Settings |
| `?` | In an empty search line, what Bloom understands by kind |
| `Ctrl+=` / `Ctrl+-` / `Ctrl+0` | Bigger, smaller, 100 % |
| `Esc` | Back, then clear the text, then close (the quick AI's answer keeps going) |
| `Ctrl+.` | Stop the quick AI's answer |
| `Ctrl+Alt+C` | AI chat window |
| `Ctrl+Alt+A` | AI actions |
| `Ctrl+Alt+L` | Bloom Lens |
| `1` … `7` | In Bloom Lens, ask AI, translate, copy text, live translation, pin, capture, color palette |
| `←` `→` / `−` `+` / `Enter` | In the color palette, pick a color, fewer or more colors, copy the HEX |
| `Ctrl+Alt+T` / `Ctrl+Alt+O` | Translate the screen, copy text from the screen |
| `Ctrl+Alt+V` | Clipboard history |

Change or switch off the global shortcuts on Settings › Shortcuts. Click one and press the keys, or press Backspace to turn it off. Window switching, emoji, the color picker, asking AI about the screen, live translation and capture can have one too, and they are off at first.

## About the AI features

Bloom's AI uses the official CLIs installed and signed in on this PC, or API keys you add. Sign-ins stay with those CLIs, and usage and terms are those of your own accounts. Without them only the AI features are off.

- Claude uses the [Claude Code](https://docs.anthropic.com/en/docs/claude-code) CLI (`claude`).
- ChatGPT uses the [Codex CLI](https://github.com/openai/codex) (`codex`).
- omp is optional and uses the [oh-my-pi](https://github.com/can1357/oh-my-pi) CLI (`omp`).
- API keys are encrypted by Windows, stay on this PC and go only to their own provider.

Only what you send reaches the AI, which means your question, attached files, the screen area picked with Bloom Lens, the text to translate and what the PC tool returns. Claude reads and writes files only inside that chat's folder (`%LOCALAPPDATA%\Bloom\ai\files\<chat>`). Codex and omp don't handle files themselves, so text attachments go in the message and files they make are saved from the answer.

<details>
<summary>PC actions, MCP servers, usage lookups, token logs and update checks</summary>

#### The pc tool

Bloom offers one tool, `pc`, through an MCP server that only this Windows user can open on this PC (`Bloom.exe --mcp`, a named pipe). Its five groups are switched on or off in Settings › PC actions · MCP.

- Look up apps, files (Everything), recent files, windows, timers and events, notes and custom commands. Look-ups run without asking.
- Open apps, files and sites, run a search line such as `volume 30`, and run custom commands.
- Write in Bloom's notes, start timers and put events on the calendar.
- Rename, move, copy and send files to the Recycle Bin.
- Read and write the clipboard.

Everything except look-ups waits for Allow or Decline on a card in the chat, and the result stays under the answer as ✓ or ✕. Claude Code asks through `--permission-prompt-tool stdio`. Codex and omp can't ask by themselves, so Bloom's MCP server shows the same card right before acting.

The tool definition adds about 250 to 500 input tokens per request, and switching groups off lowers that. Codex gets the server per chat run through `-c mcp_servers.bloom…` only. omp gets it in the chat's own `.omp/mcp.json` under `%LOCALAPPDATA%\Bloom\ai\omp-work`, and nothing is written to omp's own settings.

#### Other MCP servers

MCP servers already set up for Claude Code, Claude Desktop, Cursor, VS Code, Windsurf or Codex, and in the `.mcp.json` of projects opened with Claude Code, show on the same page with one switch each. They are off at first, and only the ones switched on are loaded (`--strict-mcp-config`). Each of their actions asks on the same card, and for ChatGPT a Bloom relay (`Bloom.exe --mcp-proxy`) handles that. Their settings are read from the original files each time.

#### Usage lookups

AI usage comes from `claude -p /usage`, which makes no model call, and from the limit lookup of `codex app-server`, one account at a time. Added accounts keep their own folders under `%LOCALAPPDATA%\Bloom\accounts` (`CLAUDE_CONFIG_DIR` or `CODEX_HOME`). The optional lightweight ChatGPT mode sends one HTTPS request to the endpoint Codex's own status reads, with that account's Codex sign-in. It is not an official API, and Bloom refreshes tokens only in folders it made, never in `~/.codex`. Bloom never reads Claude sign-ins.

Settings › AI accounts › Write the usage to a file (off at first) writes the percentages used, the reset times, the account names you gave and each account's taskbar choices to `%LOCALAPPDATA%\Bloom\export\ai-usage.json` for other programs on this PC, such as the taskbar add-on. No e-mail or sign-in is in it.

#### Logs and token counts

Work notifications read the local Claude Code and Codex logs (`~/.claude/sessions`, `~/.codex/sessions`), and the logs stay on this PC.

The token blocks read the Claude Code and Codex logs of every account on this PC. Bloom keeps only hourly sums per model, effort and session in `%LOCALAPPDATA%\Bloom\tokens`, with each session's title and the name of its last folder, never the messages or full paths. The first count reads all the logs once in the background, and later counts read only what was added. Claude Code deletes its logs after 30 days, so longer spans fill up from the day Bloom started counting. The cost is what the same tokens would cost at the providers' API prices, not what you paid, and Settings › AI accounts › Edit prices opens `prices.json` to change them.

#### Update checks

Update checks ask GitHub's API (`api.github.com`) for this repository's latest release and download new versions from GitHub. The request carries repository information only. Switch checking off in Settings › About.

</details>

## Install and build

<details>
<summary>Build from source, data folders and releases</summary>

To use Bloom, take the setup from [Download](#download). To build it you need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download). `tools\package.ps1` also needs the C++ build tools (Visual Studio's "Desktop development with C++") for the Win key helper.

```powershell
dotnet build src\Bloom\Bloom.csproj -c Release
# or make an installed copy
powershell -File tools\package.ps1   # → dist\app
powershell -File tools\install.ps1   # → %LOCALAPPDATA%\Programs\Bloom, Start menu shortcut
```

Data lives in `%APPDATA%\Bloom` and `%LOCALAPPDATA%\Bloom`. For development, `Bloom.exe --data <folder>` uses another data folder, and `--lang ko|en` fixes the language. Automatic updates need the setup install, so update a copy made with `tools\install.ps1` by hand.

To make a release, raise `<Version>` in `src/Bloom/Bloom.csproj`, write what changed under `## <Version>` in `CHANGELOG.md`, push, then run Actions › Release › Run workflow on GitHub. It downloads and checks every add-on in `addons/catalog.json`, runs the tests, builds, packs the setup and update packages with [Velopack](https://velopack.io) and publishes them as `v<Version>`.

</details>

## Code structure

<details>
<summary>Folders, conventions, tests and lint</summary>

Under `src/Bloom`, one folder is one feature. Big classes are split into `partial` files by topic.

| Folder | What's in it |
| --- | --- |
| `App/` | `Host` (start, shortcuts, commands, Lens, timers, custom commands, file actions), work notifications (`Notices`), updates (`Updates`, Velopack), run options, tray, the Win key hook and its helper for admin apps (`src/WinKeyHelper`, NativeAOT) |
| `App/Engine/` | What the search line shows, from the root search and home blocks to each view (files, clipboard, calendar, timers, colors) |
| `App/Mcp/` | The MCP server, the relay and the `pc` tool |
| `App/Qa/` | Off-screen capture QA (`--capture <folder> --qa-set <set> --data <folder> --lang en`) |
| `Addons/` | Add-ons as separate programs. The pinned catalog and downloads (`AddonCatalog`, `AddonDownload`), `addon.json` checks (`AddonManifest`), the hash-checked run copy (`AddonStore`), starting and stopping the host process (`AddonRunner`) and the shared names (`AddonProtocol`). Bloom never loads add-on code |
| `Core/` | Saved data (settings, favorites, usage, learning, timers, notes, colors), JSON storage, Korean search, matching, the interface language (`Lang`) |
| `Search/` | App list, calculator (sums, units, currencies), Everything, open windows, recent files, emoji and symbols, system items, custom commands, result models |
| `Ai/` | Claude Code, Codex and omp CLI sessions and chats, one-shot asks, translation, AI actions, usage (`AiUsage`), work logs (`ClaudeWatch`, `CodexWatch`) |
| `Ai/Api/` | API key chats, their providers and the MCP client for other servers |
| `Ocr/` | Picking a screen area, Windows OCR, the translation window, picking colors |
| `Music/` | The mini player, playlists and songs playing in other apps |
| `Clip/`, `Space/`, `Browser/` | Clipboard history, the disk space map, handing off to the browser |
| `Native/` | Win32 calls (System32 DLLs only), WinRT calls, system toggles (`SystemControl`), Explorer-style file operations (`ShellFiles`) |
| `Ui/Launcher/` | The launcher window, with every view's chips, hints and results in `Launcher.ViewSpecs.cs` |
| `Ui/Chat/`, `Ui/Player/` | The AI chat window and the mini player window |
| `Ui/Settings/` | The settings window, including the add-on pages and the taskbar folders editor |
| `Ui/Tools/` | Tool windows, their placement, attaching and snapping (`ToolDock`), and notes, timers, calculator and clipboard |
| `Ui/Controls/`, `Ui/Theme/` | Shared controls (`SmoothBorder` for G2 corners, `Dropdown`, `PopupMenu`, `AccentPicker`) and the theme (`Motion` for animation) |
| `tests/Bloom.Tests` | Automated tests as a console run without a test framework |

A new feature splits into `Engine.<feature>.cs` for results, one view line in `Launcher.ViewSpecs.cs` and `Core/<feature>.cs` for storage when needed.
Every piece of text on screen is written as `Tr("한국어", "English")`. Japanese and Simplified Chinese come from `src/Bloom/Assets/Lang/ja.tsv` and `zh.tsv`, keyed by the English text. `python tools/lang.py check` lists what they miss, and `todo ja` lists the texts still to translate. Search keywords and parser data keep Korean and English.
Screen text follows [docs/COPY.md](docs/COPY.md).
QA and development runs always use `--data <temporary folder>`, apart from the real data.

```powershell
# tests (exit code = failures)
dotnet run --project tests\Bloom.Tests
# lint (analyzers and formatting), zero warnings
dotnet build src\Bloom\Bloom.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet build tests\Bloom.Tests\Bloom.Tests.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet format whitespace src\Bloom\Bloom.csproj --verify-no-changes
dotnet format whitespace tests\Bloom.Tests\Bloom.Tests.csproj --verify-no-changes
```

For a bounded responsiveness probe, run the Release build with `--data <isolated-folder> --no-hook --no-tray --qa-responsive 3`. It writes `responsiveness.json` and `responsiveness.txt`.

</details>

## License

Bloom is under the MIT license. See [LICENSE](LICENSE).

Exchange rates come from [Rates By Exchange Rate API](https://www.exchangerate-api.com).

The taskbar add-on PillTaskbar ([source](https://github.com/hyeon1035298/PillTaskbar_BloomAddon)) and the Alt+Tab per monitor add-on ([source](https://github.com/hyeon1035298/AltTabPerMonitor_BloomAddon)) are separate programs under GPL-3.0, and Bloom itself stays MIT.

The OpenAI and Anthropic logos in `src/Bloom/Assets/Logos` are trademarks of their owners and are not covered by the MIT license. Bloom shows them only to mark which service an AI usage account belongs to.

Bloom is an unofficial personal project, not affiliated with Anthropic, OpenAI, voidtools or Raycast.
