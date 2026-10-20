<p align="center"><img src="promo/bloom-github.png" alt="Bloom launcher with AI usage, this week, a Claude Code approval card, search, AI chat, PC actions, work notifications, Bloom Lens, Bloom Space Cleaner and docked tools"></p>

<p align="center">Bloom is a native Windows launcher for opening apps, calculations, file search, AI chat and screen translation.</p>

<p align="center">
  <a href="#download">Download</a> · <a href="#features">Features</a> · <a href="#shortcuts">Shortcuts</a> · <a href="#about-the-ai-features">AI &amp; privacy</a> · <a href="README.ko.md">한국어</a> · <a href="README.ja.md">日本語</a> · <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <img src="docs/media/en/loop.webp" alt="Bloom demo: finding Chrome by its initials, maths, a unit, a reminder, emoji, clipboard history, the color picker, asking AI, a PC task approved on its card, notes attached beside the launcher, a Claude Code task-done card, screen translation and Space Cleaner">
</p>

<p align="center"><sub>WPF · .NET 10 · no WebView · about 80 MB with everything off · English / 한국어 / 日本語 / 简体中文 · MIT</sub></p>

- **Search**: Find apps, files, calculations, reminders, emoji and web searches from a few letters in the search line.
- **AI**: use Claude Code, Codex or omp signed in on this PC, or your own API key (OpenAI, Anthropic, Gemini, OpenRouter, DeepSeek, xAI, OpenCode Go), for questions, file actions, notes, timers and calendar events.
- **Features**: A native app with no WebView. Disabling a feature removes its menu entries and shortcuts and reduces memory use.

## Requirements

Use Windows 10/11, 64-bit. Setup installs the .NET 10 Desktop Runtime if needed.
AI features need an installed, signed-in Claude Code, Codex or omp CLI, or an API key from one of the supported providers. Everything enables full-PC file search.
Search words, time expressions and units accept Korean or English in every interface language.

## Download

**[Get Bloom for Windows](https://github.com/hyeon1035298/Bloom/releases/latest)** — `BloomLauncher-win-Setup.exe` on the latest release.

Or from PowerShell, which downloads that same setup and runs it ([install.ps1](install.ps1)):

```powershell
irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex
```

- Windows 10/11, 64-bit. If the PC doesn't have the .NET 10 Desktop Runtime, the setup installs it (Windows asks for permission for that part).
- Bloom installs for your account only, into `%LOCALAPPDATA%\BloomLauncher`, with a Start-menu shortcut. To remove it, use Settings › About › **Uninstall Bloom** (it asks whether your chats, settings and favorites in `%APPDATA%\Bloom` go too), or Windows Settings › Apps, which keeps them.
- Bloom is not code-signed, so SmartScreen may say "Windows protected your PC". Choose **More info › Run anyway**.
- **Updates**: Bloom checks this repository's latest release once after launch. New versions download in the background and apply at the next start. Settings › About has **Check for updates**, **Restart to update** when a version is ready, and a switch for checking at startup. Changes appear once after an update and remain available in Settings › About.

## Features

### One search line

<img src="docs/media/en/search.webp" alt="The search line reading laundry in 30 min as a reminder, and a list of what one line takes: maths, units, emoji, apps, site searches, Korean initials">

Search by part of a name. Calculations update as you type; timers accept time expressions.

- **Apps · settings · commands**: Start-menu apps, Windows settings pages and Bloom commands.
  - A name's initials work (`gc` → Google Chrome).
  - For Korean users it also understands initial consonants (`ㅁㅁㅈ` → 메모장) and words typed with the keyboard left in Korean (`초개ㅡㄷ` → Chrome).
  - An app can be renamed inside Bloom only.
- **Favorites**: keep apps, files, sites and commands in the home's icon row and open them with Ctrl+1…9.
  - Group them in folders.
  - Give them picture icons that match their names, or icons drawn by AI.
- **Maths · units · currencies**: `45000*1.1`, `(1+2)*3`, `5!`, `5.9 ft to cm`, `5km to mi`, `100 usd`
  - Enter on the result copies it.
- **Timers · reminders · calendar**: `timer 5m`, `laundry in 30 min`, `remind me at 3pm to call`
  - Reminders also show on the monthly calendar and in the home's "This week".
  - When one is due, a card comes up with a sound (again in 5 min · dismiss).
- **Web search · site shortcuts**: what you typed goes to your browser as a search (Google · Naver · Bing · DuckDuckGo).
  - A keyword sends it to a site: `yt cats`, `gh bloom`, `map central park`.
  - Make and edit your own site shortcuts, and pick the browser searches and links open in.
- **Files · recent files**
  - With [Everything](https://www.voidtools.com/) installed, find files by name at once, narrow by kind (videos, pictures, documents, folders …) and preview them.
  - Without it, Bloom finds them in Windows' recent files.
- **Window switching**: find an open window by name and switch to it; Ctrl+W closes it.
- **Emoji · symbols**: by English or Korean name, then paste: `:heart`, `arrow`, `celsius`.
- **System**
  - Dark mode, mute sound or microphone, `volume 30`, switch the output device, Wi-Fi · Bluetooth, show hidden files · extensions
  - Sleep, screen off, empty the Recycle Bin, end processes, lock · restart · shut down
- **Ctrl+K** on any result lists everything you can do with it. On a file: rename, move or copy to another folder, Recycle Bin — the way Explorer does it, with the same progress window and name-clash questions.
- Recent selections take priority when you repeat a search. Clear the learning history in settings.
- **What you can type**: type `?` alone to list the inputs Bloom understands, by kind; Enter puts an example in the search line. The empty search line shows one example at a time.
- **Bloom settings**: type a setting's name (`glass`, `shortcut`) to find it; Enter opens it in the settings window.

### Paths, windows, dates and installs

<img src="docs/media/en/more.webp" alt="The window layout list with drawn icons for halves and quarters, and a column of more things one line takes: a path, left half, in 100 days, days until christmas, new york 3pm, install vlc, pin to the screen">

- **Paths**: type `C:\Users`, `D:/work`, `~\Downloads` or `%APPDATA%` and that folder comes first with what's in it. Enter opens it in Explorer; Tab goes into the folder you picked.
- **Window layout**: `left half`, `right third`, `top-left quarter`, `maximize`, `center`, `next screen` move the window in front. Halves meet edge to edge and the taskbar stays clear.
- **Dates · world clock**: `in 100 days`, `days until christmas`, `days since 2026-03-01`, `2026-01-01 ~ 2026-12-25`, `new york time`, `tokyo 3pm`, `3pm in london`
- **Install apps**: `install vlc` searches winget and the Microsoft Store. Enter opens a confirmation, then installation runs in the background with the result shown on a card. Ctrl+K opens installation in a terminal to show progress.
- **Shortcuts for anything**: Ctrl+K › Set a shortcut gives an app, folder, file, site, command or window layout a global shortcut of its own: just press the keys you want (say Ctrl+Alt+M). The assigned shortcuts are listed in Settings › Tools.
- **Uninstall apps**: right-click an app (or Ctrl+K) › Uninstall…. After confirmation, the program's uninstaller opens. Store apps are removed for your account.
- **Pin to the screen · capture**: Bloom Lens' fifth and sixth choices (see below).
- Each of these can be switched off in Settings › Features.

### AI with your own Claude Code · Codex login

<img src="docs/media/en/ai.webp" alt="A chat asking Allow, Always or Decline before it moves a PDF into Documents, and Claude Code and Codex work notifications">

Answers come from the Claude Code or Codex CLI you have installed and signed in to on this PC, or from a provider you add a key for in Settings › AI accounts › API keys. Keys are encrypted by Windows and stay on this PC; each goes only to its own provider. With an API model, files, PowerShell commands, Bloom's PC actions and your other MCP servers are offered as tools, and each asks first unless you change it in Settings › PC actions · MCP › Permissions.

- **Quick AI**: type in the search line and press **Tab** to open AI chat there; Shift+Tab starts a new chat. Choose the model and thinking level.
  - Esc goes back home and the answer keeps going; the search line shows it and Tab takes you back. ■ Stop (or Ctrl+.) stops it.
  - A click outside closes the launcher, as in the other views; the Win key opens it again at the same chat. 📌 keeps it above other windows instead.
- **Ready before you ask**: when a chat is open or you start typing, Bloom starts the AI for the model that chat uses (Claude, ChatGPT or omp, only one at a time), so your first message doesn't wait for it. It ends after a few minutes unused or when the chat closes.
- **ChatGPT answers as they're written**: ChatGPT chats (the chat window and the launcher's quick chat) show the answer as it is written, not all at once at the end, and keep one Codex process per chat.
  - Bloom's Codex runs with its own settings, so your own Codex `config.toml` doesn't change Bloom's chats; your Codex sign-in and earlier chats carry over.
  - The first ChatGPT chat after updating can take longer to start while Codex indexes your history once.
- **omp chats** (optional): when [omp](https://github.com/can1357/oh-my-pi) is installed and signed in (`omp login`), it is one more side to pick (Settings › AI chat › omp, Ctrl+Shift+M); nothing changes until you pick it.
  - Each chat keeps one `omp --mode rpc` process and its own omp session in Bloom's folder, with Bloom's instructions in place of omp's own prompt and none of your omp skills, rules or extensions. omp's own file and command tools are off; web search is on by default.
  - The chat's bar shows how much of the model's memory it uses (`14.2k / 200k`) and a **Tidy** button that has omp summarise the earlier part. omp also does it by itself when the chat gets long, and a line under the answer says so.
  - `/session`, `/context`, `/usage` and a few other read-only omp commands work in the chat; commands that would change your omp settings are answered with a note instead.
  - Pick the models in Settings › AI chat › omp › My models (from `omp models`, or typed as `provider/model`); with none picked, omp answers with its own default model.
- **AI chat window** (Ctrl+Alt+C)
  - Continue past chats and keep them in groups (folders).
  - Attach files and pictures (drag and drop · Ctrl+O · Ctrl+V).
  - Open or save the files the AI makes.
  - Chat titles use the model, field and task, like `[◆ Opus • Code ] Fix the login`; the title changes only when a new task starts.
  - Pick several chats and groups and delete them at once: ✓ by the chat search, Ctrl+click, or ⋯ › Select.
  - Chat memory is released after 10 minutes of inactivity. Set minutes or hours (or 0 = never) in Settings › AI chat.
- **Copy any part**: drag over an answer, your message or a code block (in the quick chat too) and press Ctrl+C or right-click › Copy.
- **Hand off PC tasks**: "move the quote PDF I got yesterday into Documents", "volume 30", "find last week's videos in Downloads".
  - Each action that changes data requires approval on a card in the chat (Allow · Always in this chat · Decline).
  - What was done stays under the answer as ✓ / ✕.
- **Notes, timers and events stay in Bloom**
  - "Write my shopping list in a note" attaches Bloom's notes beside the chat and writes it there.
  - "A 25 minute timer" and "put a meeting on my calendar tomorrow at 3pm" go into Bloom's timers and calendar, and the timers tool comes up beside the chat to show them.
  - Notes, timers and events use Bloom storage and tools, without creating files or opening other apps.
- **AI actions** (Ctrl+Alt+A): in any app, turn the selected text (or the clipboard when nothing is selected) into something else and paste it back.
  - Summarize · translate · fix spelling · polish · shorter · longer · formal · casual · key points as a list · explain simply · draft a reply
  - Or write your own instruction.
- **Usage**: the home shows Claude's and ChatGPT's 5-hour and weekly limits, as used or as left, refreshed every 5 or 10 minutes. Several accounts of each are supported (Settings › AI accounts): each one signs in with the CLI's own login, and AI chat uses the account you pick. Nothing switches accounts on its own. An added account can be signed out with its × (or its menu): Bloom runs the CLI's own sign-out and moves the account's folder to the Recycle Bin.
- **Work notifications**: a card in a screen corner and a sound when a Claude Code or Codex task starts, finishes, waits for approval, or nears or regains its usage limit. Click a task's card to go to the terminal or editor window it runs in.
  - Pick one of six positions, the size, the sound and which events.
  - Notifications are hidden during full-screen games and presentations.
  - **Approve on the card** (Settings › Notifications, off by default): when Claude Code asks for permission, its card offers Allow, Decline and Answer in terminal. Turning it on adds Bloom's hook to Claude Code's `settings.json`; turning it off takes it out. While the session's window is in front, the terminal asks as before.

### On the screen — Bloom Lens · color picker

<img src="docs/media/en/lens.webp" alt="A Japanese message on the screen picked with Bloom Lens and translated into English, and the color picker on #FF7A9E">

- **Bloom Lens** (Ctrl+Alt+L): drag over an area of the screen, then pick what to do by number.
  - `1` Ask AI: Enter with nothing typed explains the screen.
  - `2` Translate
  - `3` Copy its text (Windows OCR)
  - `4` Translate live: Bloom keeps reading the area and translates it whenever it changes.
  - `5` Pin to the screen: the area stays on top as a picture. Drag it, size it with the wheel, Esc closes it.
  - `6` Capture: the area goes to the clipboard as a picture, ready for Ctrl+V. Captures are also saved in your Screenshots folder, where Windows' Snipping Tool saves them, as "Bloom <date> <time>.png"; Bloom never deletes anything there.
  - `7` Color palette: the main colors of the area (see below).
  - Translate (Ctrl+Alt+T) and copy text (Ctrl+Alt+O) have shortcuts of their own.
- **Color picker**: take a color from the screen with the magnifier, or paste a color code.
  - See HEX · RGB · HSL and its contrast with white and black text.
  - Get gradients made from the color (copied as CSS).
  - Recent colors are kept.
- **Color palette** (Lens `7`, or the palette button on the capture's drawing bar): picks the main colors of the area, 5 by default (− + for 3–10).

<img src="docs/media/en/palette.webp" alt="The color palette of a drawn poster: five swatches with HEX, RGB and share, Copy all, and suggested gradients for the picked color">

  - Each swatch shows HEX, RGB and its share; click to copy the HEX. ← → picks a swatch.
  - Copy all gives a HEX list, CSS variables or a palette image; a picked color shows gradient suggestions.
  - Worked out on this PC, with no AI usage. Palettes are kept with the recent colors.

### Docked tools

<img src="docs/media/en/tools.webp" alt="The pinned launcher with notes attached on its right and the calculator under it">

- **Ctrl+J** (or the tools button at the top) opens notes, timers, calculator, clipboard history and shortcuts as small windows. Press it again to close the menu.
- The **Shortcuts** tool lists every shortcut you gave an app, folder or command (Ctrl+K › Set a shortcut) with its keys; a click runs it.
- **Tool placement**
  - Move a tool by its head and size it by its edges.
  - Near the launcher, another tool or a screen edge, it snaps in place.
  - Dropped beside the launcher it stays attached right there, at that size: it moves with the launcher and comes and goes with it.
  - Pulled away, it floats on its own. The pin on its head keeps it always on top (off at first).
- **Notes**: several notes, kept as you type. Pin them; deleted ones go to a trash.
  - Type `note buy milk` in the search line to keep a note at once.
  - Notes are searched too.
  - Notes are Markdown: switch between view and edit, tick checklist boxes in the view, and a note's `# heading` names it in the list. Ctrl+wheel changes the text size (Ctrl+0 resets it).
- **Markdown documents (.md)**: open a .md file in Bloom's document window with "View in Bloom" on a file you find, or with Explorer's "Open with". In the setup's install, Settings › Features › "Make it the default app" lets a double-click in Explorer open them in Bloom.
  - It shows in the taskbar and snaps like other windows. View draws the Markdown, Edit shows the text. Long documents open quickly; drag to select across paragraphs and copy with Ctrl+C.
  - It saves as you type and keeps the file's encoding and line endings. When another program changes the file, Bloom asks whether to reload it or save yours.
  - Links open only web pages and e-mail; a link to a file on this PC shows it in its folder. Pictures in network folders load only when the document is in that same folder.
- **Timers**: set days, hours, minutes and seconds (▲ ▼, the mouse wheel, the arrow keys or digits), write what it is for underneath and press Start or Enter. Left at zero, the line is read as words instead: `10 min`, `3pm meeting`. The time left shows, with 5 more minutes and cancel.
- **Calculator**: the same sums as the search line. Enter copies the answer and keeps it in the list; the bin in its head clears the list.
- **Clipboard history** (Ctrl+Alt+V): the text, images and files you copied. Click one to copy it again; pin the ones you keep.
- **Pin · move · size the launcher**
  - Ctrl+P keeps the launcher above other windows when you click elsewhere (📌 shows while it does; in the quick AI, 📌 sits where ⚙ is).
  - Drag the search line (anywhere past the text) or the bottom bar to move it, to another monitor too; double-click either to put it back in the middle.
  - ⚙ at the end of the search line opens the settings (Ctrl+,); in the quick AI, use Ctrl+,.
  - Drag the launcher's edges and corners to resize it. A bigger launcher shows more, not bigger: text and icons keep their size. The size is kept and fitted to the screen on open; Settings › General resets it.
  - Ctrl+= / Ctrl+- / Ctrl+0 scale Bloom from 80 to 150 % — the launcher, tools, AI chat and settings together.

### Bloom Space Cleaner

<img src="docs/media/en/space.webp" alt="A map of a folder where videos, downloads, photos and projects are blocks as big as they are, with a list of the biggest files">

Shows drive and folder space as a map. Each file and folder occupies a block proportional to its size.

- Click a block to go inside; pick big files from the list beside it and send them to the Recycle Bin.
- Switch drives or pick a folder to look at.
- Choose files and folders below drive roots, outside the Windows folder. Restore recycled items from the Recycle Bin.

### Edit the home

<img src="docs/media/en/home.webp" alt="The home of a wide launcher in two columns while it is edited: AI usage, timers and this week on the left, favorites and commands on the right, each block with its own buttons and an Add a block line">

The empty search line shows the home: favorites, AI usage, this week, timers, recently used, commands and more. Ctrl+E (or "Edit home") edits it.

- Drag a block by its title bar to move it; a gap shows where it will land. ▲ ▼ ‹ › move it with the buttons, ✕ hides it.
- Each list block has its own row count (− +), and clicking a title folds the block.
- A wide launcher lays the home out in two columns; ‹ › moves a block to the other column. Resize the launcher from its edges (see above).
- "+ Add block" at the end of each column lists the hidden blocks as cards. Click one to add it at the end of that column, or drag it to where it should go.
- The AI usage cards can be reordered there too, and each account can show a name of your own instead of its e-mail.

### Settings and custom commands

<img src="docs/media/en/yours.webp" alt="The Features page of the settings with a switch per feature, and the Performance page with animation, glass and memory">

- **Custom commands**: run what you do often by a keyword.
  - Scripts: .ps1 · .bat · programs, or one PowerShell line. They run without a window; the result comes as a notice card.
  - Snippets: saved text pasted into the window in front (`{date}` `{time}` `{clipboard}`).
  - Chains: lines for the search line, run in turn (`notepad ; volume 30 ; wait 2 ; yt lofi`).
  - Words after the keyword fill the `{input}` spot (`bk D:\photos`).
  - Script files are added from Ctrl+K › Custom commands.
- **Features on and off**: disabling a feature (AI chat · Bloom Lens · calculator · timers · color picker · emoji · window switching …) removes it from search results, commands, home and favorites, and releases its shortcut. Saved data is retained.
- **Look · performance**
  - Dark, light or system theme, and accent color presets
  - Animations on · reduced · off, how many per second (30 · 60 · max), glass off or how see-through it is (a slider, 15–75 %)
  - Bloom draws with the CPU by default. GPU drawing is experimental: on some PCs the screen may stutter or freeze for a few seconds. Pick it at first start or in Settings › Performance
  - Features that use memory say in the settings how much they add.
- **Language · first start · moving**
  - 한국어 / English / 日本語 / 简体中文 (auto follows Windows); Bloom restarts after a change. Search words, times and units are typed in English or Korean in every language.
  - First-run setup covers how to open Bloom, the home and AI features.
  - Export the settings, favorites, app names and browser shortcuts to one file, and import them on another PC.
- **Problem report**: Settings › About › Problem report copies the version, Windows and the recent log (user name and e-mail addresses hidden) and opens a GitHub issue page. Bloom sends nothing itself.
- **Stall log**: when Bloom stops responding for half a second or more, its log says so.

## Shortcuts

| Keys | What it does |
| --- | --- |
| `Win` · `Alt+Space` | Open Bloom (`Win` over apps run as administrator: Settings › General › Win key over admin apps) |
| `Tab` / `Shift+Tab` | Ask AI what's in the search line / in a new chat |
| `Ctrl+K` | Everything you can do with the picked result |
| `Ctrl+1` … `Ctrl+9` | Favorites |
| `Ctrl+J` | Tools (notes · timers · calculator · clipboard · shortcuts) |
| `Ctrl+P` | Keep the launcher on top |
| `?` | In an empty search line: what Bloom understands, by kind |
| `Ctrl+E` | Edit the home |
| `Ctrl+=` · `Ctrl+-` · `Ctrl+0` | Bigger · smaller · 100 % |
| `Esc` | Back → clear the text → close (the quick AI's answer keeps going) |
| `Ctrl+.` | Stop the quick AI's answer |
| `Ctrl+Alt+C` | AI chat window |
| `Ctrl+Alt+A` | AI actions |
| `Ctrl+Alt+L` | Bloom Lens |
| `1` … `7` | In Bloom Lens: ask AI · translate · copy text · live translation · pin · capture · color palette |
| `← →` · `−` `+` · `Enter` | In the color palette: pick a color · fewer or more colors · copy the HEX |
| `Ctrl+Alt+T` · `Ctrl+Alt+O` | Translate the screen · copy text from the screen |
| `Ctrl+Alt+V` | Clipboard history |

The global shortcuts can be changed or switched off per feature in the settings: click one and press the keys, Backspace turns it off. Any key works with Ctrl, Alt, Shift or Win; keys that type nothing (F1–F24, PrintScreen, Pause, media keys) work alone too. Window switching, emoji, the color picker, asking AI about the screen, live translation and capture can have one too (off at first).

## About the AI features

AI features use the official CLIs installed and signed in on this PC. Sign-in credentials stay with those CLIs.

- Claude: [Claude Code](https://docs.anthropic.com/en/docs/claude-code) CLI (`claude`)
- ChatGPT: [Codex CLI](https://github.com/openai/codex) (`codex`)
- omp (optional): [oh-my-pi](https://github.com/can1357/oh-my-pi) CLI (`omp`)

Usage and terms are those of your own accounts. Without the CLIs only the AI features are off; everything else works as usual.

**AI acting on the PC** (Claude · ChatGPT · omp, Settings › PC actions · MCP)

- Bloom offers one tool (`pc`) through an MCP server that only opens on this PC (`Bloom.exe --mcp`, a named pipe only this Windows user can open).
- What it may do comes in five groups, each switched on or off in the settings.
  - **Look up**: apps · files (Everything) · recent files · windows · timers and events · notes · custom commands. Runs at once, without asking.
  - **Open · run**: open apps, files and sites, run a search-line line (`volume 30`, `yt lofi`), run custom commands.
  - **Notes · timers · calendar**: write in Bloom's notes (they show beside the chat), start timers, put events on the calendar.
  - **Tidy files**: rename · move · copy · Recycle Bin.
  - **Clipboard**: read · write.
- Every action except look-ups requires approval with the **Allow / Always in this chat / Decline buttons in the chat** (Claude Code's `--permission-prompt-tool stdio`). Results remain under the answer as ✓ / ✕.
- Other MCP servers already set up on this PC show on the same page, one switch per server (off by default).
  - They are found in the settings of Claude Code · Claude Desktop · Cursor · VS Code · Windsurf · Codex, and in the `.mcp.json` of projects opened with Claude Code.
  - Each tool action requires approval on the same card.
  - For ChatGPT, a Bloom relay (`Bloom.exe --mcp-proxy`) connects to the server to handle approval.
  - Server settings are read from their original files each time.
- There is one tool definition, adding about 500 input tokens per request compared with no tools. The measured increase was 400 before notes, timers and calendar were added; the current estimate includes their description length. Disabling groups reduces tokens. Only selected MCP servers are loaded (`--strict-mcp-config`).
- ChatGPT (Codex) chats use the same tool.
  - Codex can't ask by itself in exec mode, so Bloom's MCP server shows the same card right before acting and waits for the answer.
  - The server is attached per chat run with `-c mcp_servers.bloom…` only.
  - Codex loads MCP tool descriptions only when needed, so a one-line note of the tool's name and actions goes with each request (about +250 tokens, worked out the same way).
- omp chats use the same tool.
  - omp can't ask either (it runs with `--no-ui`), so Bloom's MCP server shows the same card right before acting. The tool and the other servers switched on are written to the chat's own `.omp/mcp.json`, in a folder per chat under `%LOCALAPPDATA%\Bloom\ai\omp-work`; nothing is written to omp's own settings.

AI usage comes from `claude -p /usage` (no model call) and the limit lookup of `codex app-server`, one account at a time (`CLAUDE_CONFIG_DIR` / `CODEX_HOME` per added account, folders under `%LOCALAPPDATA%\Bloom\accounts`). The optional lightweight ChatGPT mode instead sends one HTTPS request to the endpoint Codex's own status reads, with that account's Codex sign-in; it is not an official API, and Bloom refreshes tokens only in folders it made, never in `~/.codex`. Bloom never reads Claude sign-ins. Work notifications read local Claude Code and Codex logs (`~/.claude/sessions`, `~/.codex/sessions`); the logs stay on this PC.
Update checks use GitHub's API (`api.github.com`) to find this repository's latest release and download new versions from GitHub. The request contains repository information only; settings, files and chats stay on this PC. Switch checking off in Settings › About.
Only what you send yourself reaches the AI — your question, attached files, the screen area picked with Bloom Lens, the text to translate — and what the PC tool returns.
Claude can read and write files only inside that chat's folder (`%LOCALAPPDATA%\Bloom\ai\files\<chat>`). ChatGPT (Codex) and omp don't handle files themselves, so text attachments go in the message and files it makes are taken out of the answer and saved.

Bloom is an unofficial personal project, not affiliated with Anthropic, OpenAI, voidtools or Raycast.

## Install · build

To use Bloom, take the setup file from [Download](#download). To build it yourself you need Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download); `tools\package.ps1` also needs the C++ build tools (Visual Studio's "Desktop development with C++") for the Win-key helper.

```powershell
dotnet build src\Bloom\Bloom.csproj -c Release
# or make an installed copy
powershell -File tools\package.ps1   # → dist\app
powershell -File tools\install.ps1   # → %LOCALAPPDATA%\Programs\Bloom, Start-menu shortcut
```

Data lives in `%APPDATA%\Bloom` and `%LOCALAPPDATA%\Bloom`.
For development, `Bloom.exe --data <folder>` uses another data folder (`--lang ko|en` fixes the language).
Automatic updates require the setup version. Update copies made with `tools\install.ps1` manually.

**A release**: raise `<Version>` in `src/Bloom/Bloom.csproj`, write what changed under `## <Version>` in `CHANGELOG.md` (that becomes the release's text; without it the commit list is used), push, then run **Actions › Release › Run workflow** on GitHub. It runs the tests, builds, packs the setup file and the update packages with [Velopack](https://velopack.io) and publishes them as `v<Version>`; installed copies find it at their next start.

## Code structure

<details>
<summary>Folders · conventions · tests and lint</summary>

Under `src/Bloom`, one folder is one feature. Big classes are split into `partial` files by topic.

| Folder | What's in it |
| --- | --- |
| `App/` | `Host` (wires the whole app: start · shortcuts · commands · Lens · timers/colors · running custom commands · file actions), work notifications (`Notices`), updates from GitHub Releases (`Updates`, Velopack), the MCP server (`Mcp/` — relay · server · the `pc` tool), run options, tray, the Win key hook (and its elevated helper for apps run as administrator: `src/WinKeyHelper`, NativeAOT) |
| `App/Engine/` | Making what the search line shows — root search, home blocks, item conversion, per view (files · clipboard · calendar · timers · colors …) |
| `App/Qa/` | Off-screen capture QA (`--capture <folder> --qa-set v15 --data <folder> --lang en`) |
| `Core/` | What is saved (settings · favorites · usage · learning per typed text · timers · notes · colors) and JSON storage, Korean search, matching, the interface language (`Lang` — `Tr("한국어", "English")`) |
| `Search/` | App list, calculator (sums · units · currencies), Everything, open windows, recent files, emoji · symbols (`Assets/symbols.tsv`), system items, custom commands (`UserCommands`), result models |
| `Ai/` | Claude Code / Codex / omp CLI sessions with chats · groups (`Chats`), one-shot asks (`OneShot`), translation, AI actions, AI icons, usage (`AiUsage`), reading work logs (`ClaudeWatch` · `CodexWatch`) |
| `Ocr/` | Picking a screen area, Windows OCR, the translation window, picking colors |
| `Clip/`, `Space/`, `Browser/` | Clipboard history, the disk space map, handing off to the browser |
| `Native/` | Win32 calls (System32 DLLs only), WinRT calls (`WinRt`), system toggles (`SystemControl` — sound · wireless · dark mode …), Explorer-style file operations (`ShellFiles`) |
| `Ui/Tools/` | Tool windows: free placement · attaching · magnetic snapping · sizing (`ToolDock`), the window frame (`ToolWindow`), notes · timers · calculator · clipboard |
| `Ui/` | The launcher window (`Launcher/` — every view's chips · hints · results in one place, `Launcher.ViewSpecs.cs`), the AI chat window (`Chat/`), the settings window (`Settings/`), notice cards, shared controls (`Controls/` — continuous-curvature corners `SmoothBorder`, `Dropdown`, `PopupMenu`), theme (`Theme/` — animation settings in `Motion`) |
| `tests/Bloom.Tests` | Automated tests (calculator · time reading · dates and world clock · typed paths · winget's table · shortcuts · matching · learning · emoji · language · AI prompts · the MCP tool · usage reading · work logs · custom commands) — a console run without a test framework |

Conventions: a new feature splits into `Engine.<feature>.cs` (results) + one view line in `Launcher.ViewSpecs.cs` + `Core/<feature>.cs` (storage) when needed.
Every piece of text on screen is written as `Tr("한국어", "English")`. Japanese and Simplified Chinese come from `src/Bloom/Assets/Lang/ja.tsv` and `zh.tsv`, keyed by the English text (`{0}` for each hole of an interpolated one); `python tools/lang.py check` lists what they miss, `todo ja` the texts still to translate. Search keywords and parser data keep Korean and English.
Screen text follows [docs/COPY.md](docs/COPY.md).
QA and development runs always use `--data <temporary folder>`, apart from the real data.

```powershell
# tests (exit code = failures)
dotnet run --project tests\Bloom.Tests
# lint (analyzers + formatting) — zero warnings
dotnet build src\Bloom\Bloom.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet build tests\Bloom.Tests\Bloom.Tests.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet format whitespace src\Bloom\Bloom.csproj --verify-no-changes
dotnet format whitespace tests\Bloom.Tests\Bloom.Tests.csproj --verify-no-changes
```

For a bounded responsiveness probe, run the Release executable with
`--data <isolated-folder> --no-hook --no-tray --qa-responsive 3`.
It writes `responsiveness.json` and `responsiveness.txt` using synthetic search results,
an Input-priority heartbeat, and one half-duty CPU worker. The ink samples measure
completed strokes and export, not live mouse or display latency. See
[the responsiveness notes](docs/wiki/responsiveness.md) for comparison results and limits.

</details>

## Optional

Install these programs to enable their related features, blocks and settings.

- **Everything**: finding files.
- **Claude Code CLI · Codex CLI · omp**: AI chat · Lens · translation · quick actions · usage · work notifications.

## License

MIT — [LICENSE](LICENSE).
Exchange rates: [Rates By Exchange Rate API](https://www.exchangerate-api.com).
