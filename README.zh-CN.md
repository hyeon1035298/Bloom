<p align="center"><img src="promo/bloom-github-zh.png" alt="显示 AI 用量、本周日程和 Claude Code 等待批准卡片的 Bloom 启动器，以及搜索、AI 聊天、电脑操作、工作通知、Bloom Lens、Bloom Space Cleaner、旁边的工具"></p>

<p align="center">Bloom 是一款 Windows 原生启动器。在搜索栏中即可启动应用、计算、搜索文件、AI 聊天和翻译屏幕。</p>

<p align="center">
  <a href="#下载">下载</a> · <a href="#功能">功能</a> · <a href="#快捷键">快捷键</a> · <a href="#关于-ai-功能">AI 与隐私</a> · <a href="README.md">English</a> · <a href="README.ko.md">한국어</a> · <a href="README.ja.md">日本語</a>
</p>

<p align="center">
  <img src="docs/media/en/loop.webp" alt="Bloom 演示（英文界面）：按首字母找到 Chrome、计算、单位、提醒、表情符号、剪贴板记录、取色器、向 AI 提问、在卡片上批准电脑操作、启动器旁的笔记、Claude Code 任务完成卡片、屏幕翻译、Space Cleaner">
</p>

<p align="center"><sub>WPF · .NET 10 · 无 WebView · 全部关闭时约 80 MB · 한국어 / English / 日本語 / 简体中文 · MIT</sub></p>

- **搜索**：在搜索栏输入几个字母，即可使用应用、文件、计算、提醒、表情符号和网页搜索。
- **AI**：使用本机已登录的 Claude Code、Codex 或 omp，或你自己的 API 密钥（OpenAI、Anthropic、Gemini、OpenRouter、DeepSeek、xAI、OpenCode Go）提问、操作文件、记笔记、设计时器和日历日程。
- **功能**：不使用 WebView 的原生应用。关闭某项功能后，其菜单和快捷键随之移除，内存占用也会减少。

## 运行环境

需要 Windows 10/11（64 位）。如果没有 .NET 10 桌面运行时，安装程序会自动安装。
AI 功能需要已安装并登录的 Claude Code CLI、Codex CLI 或 omp CLI，或受支持服务的 API 密钥。全盘文件搜索需要 Everything。
无论界面语言是什么，搜索词、时间表达和单位都需用韩语或英语输入。

## 下载

**[获取 Windows 版 Bloom](https://github.com/hyeon1035298/Bloom/releases/latest)** — 最新版本中的 `BloomLauncher-win-Setup.exe`。

也可以在 PowerShell 中下载并运行同一个安装程序（[install.ps1](install.ps1)）：

```powershell
irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex
```

- 适用于 Windows 10/11（64 位）。如果没有 .NET 10 桌面运行时，安装程序会安装它（这一步需要 Windows 授权）。
- Bloom 只为当前用户安装，位置是 `%LOCALAPPDATA%\BloomLauncher`，并在开始菜单创建快捷方式。卸载请使用 设置 › 关于 › **卸载 Bloom**（会询问是否一并删除 `%APPDATA%\Bloom` 中的对话、设置和收藏）；通过 Windows 设置 › 应用 卸载时，这些数据会保留。
- Bloom 没有代码签名，SmartScreen 可能显示“Windows 已保护你的电脑”。请选择 **更多信息 › 仍要运行**。
- **更新**：启动后检查一次本仓库的最新版本。新版本在后台下载，下次启动时生效。设置 › 关于 中有 **检查更新**、准备就绪时的 **重启以更新**，以及启动时检查的开关。更新后会显示一次更新内容，之后也可在 设置 › 关于 中查看。

## 功能

### 一个搜索栏

<img src="docs/media/zh/search.webp" alt="把 laundry in 30 min 读作 30 分钟后提醒的搜索栏，以及单行输入示例：计算、单位、表情符号、首字母、网站搜索、笔记">

输入名称的一部分即可搜索。计算结果随输入实时更新，计时器能读懂时间表达。

- **应用 · 设置 · 命令**：开始菜单中的应用、Windows 设置页面和 Bloom 命令。
  - 可以用名称的首字母查找（`gc` → Google Chrome）。
  - 也能识别韩语初声（`ㅁㅁㅈ` → 메모장）和在韩语输入法下敲出的英文单词（`초개ㅡㄷ` → Chrome）。
  - 应用名称可以只在 Bloom 内修改。
- **收藏**：把应用、文件、网站和命令放在主页的图标行，用 Ctrl+1…9 打开。
  - 可以用文件夹分组。
  - 可以配上与名称相符的图片图标，或由 AI 绘制的图标。
- **计算 · 单位 · 货币**：`45000*1.1`、`(1+2)*3`、`5!`、`5.9 ft to cm`、`5km to mi`、`100 usd`
  - 在结果上按 Enter 即复制。
- **计时器 · 提醒 · 日历**：`timer 5m`、`laundry in 30 min`、`remind me at 3pm to call`
  - 提醒也显示在月历和主页的“本周”中。
  - 到时会弹出卡片并播放提示音（5 分钟后再提醒 · 关闭）。
- **网页搜索 · 网站快捷方式**：输入的内容会在浏览器中搜索（Google · Naver · Bing · DuckDuckGo）。
  - 加上关键词即可在网站内搜索：`yt cats`、`gh bloom`、`map central park`。
  - 可以创建和编辑自己的网站快捷方式，并选择搜索和打开链接所用的浏览器。
- **文件 · 最近的文件**
  - 安装了 [Everything](https://www.voidtools.com/) 时，可按文件名即时查找，按类型（视频、图片、文档、文件夹等）筛选并预览。
  - 没有 Everything 时，从 Windows 的最近文件中查找。
- **切换窗口**：按名称找到已打开的窗口并切换；Ctrl+W 关闭。
- **表情符号 · 符号**：用英文或韩文名称查找后粘贴：`:heart`、`arrow`、`celsius`。
- **系统**
  - 深色模式、静音声音或麦克风、`volume 30`、切换输出设备、Wi-Fi · 蓝牙、显示隐藏文件 · 扩展名
  - 睡眠、关闭屏幕、清空回收站、结束进程、锁定 · 重启 · 关机
- 在任意结果上按 **Ctrl+K**，会列出可对它执行的所有操作。对文件可以重命名、移动或复制到其他文件夹、移到回收站，与资源管理器使用相同的进度窗口和重名确认。
- 重复同一搜索时，最近选过的项目排在前面。学习记录可在设置中清除。
- **可输入的内容**：在搜索栏只输入 `?`，按类别列出 Bloom 能识别的输入；按 Enter 把示例填入搜索栏。空搜索栏也会逐个显示示例。
- **查找 Bloom 设置**：输入设置名称（`玻璃效果`、`快捷键`）即可找到该设置，按 Enter 在设置窗口中打开。

### 路径、窗口、日期和安装

<img src="docs/media/zh/more.webp" alt="用图标表示半屏和四分之一屏的窗口布局列表，以及更多单行输入示例：路径、left half、in 100 days、days until christmas、new york 3pm、install vlc、pin to the screen">

- **路径**：输入 `C:\Users`、`D:/work`、`~\Downloads` 或 `%APPDATA%`，该文件夹及其中的项目会排在最前。Enter 在资源管理器中打开；Tab 进入所选文件夹。
- **窗口布局**：`left half`、`right third`、`top-left quarter`、`maximize`、`center`、`next screen` 移动最前面的窗口。两个半屏边缘紧贴，不遮挡任务栏。
- **日期 · 世界时钟**：`in 100 days`、`days until christmas`、`days since 2026-03-01`、`2026-01-01 ~ 2026-12-25`、`new york time`、`tokyo 3pm`、`3pm in london`
- **安装应用**：`install vlc` 在 winget 和 Microsoft Store 中搜索。按 Enter 打开确认窗口，安装在后台进行，结果以卡片显示。Ctrl+K 可在终端中安装并查看进度。
- **任何项目都能设快捷键**：Ctrl+K › 设置快捷键… 可为应用、文件夹、文件、网站、命令或窗口布局指定专属的全局快捷键，直接按下想用的组合键（如 Ctrl+Alt+M）即可。已设置的快捷键列在 设置 › 工具 中。
- **卸载应用**：右键单击应用（或按 Ctrl+K）› 卸载…。确认后打开该程序的卸载程序。应用商店应用会从当前账户中移除。
- **固定到屏幕 · 截图**：Bloom Lens 的第 5 和第 6 项（见下文）。
- 以上每项都可在 设置 › 功能 中关闭。

### 用你自己的 Claude Code · Codex 登录使用 AI

<img src="docs/media/zh/ai.webp" alt="在把报价单 PDF 移到“文档”之前，在聊天中询问 允许 · 始终允许 · 拒绝 的卡片，以及 Claude Code 和 Codex 的工作通知">

回答来自本机已安装并登录的 Claude Code CLI 或 Codex CLI，或你在 设置 › AI 账户 › API 密钥 中添加的服务。密钥由 Windows 加密，只保存在本机，每个密钥只发送给对应的服务。使用 API 模型时，文件、PowerShell 命令、Bloom 的电脑操作和其他 MCP 服务器都可作为工具使用；除非在 设置 › 电脑操作 · MCP › 权限 中更改，每次执行前都会先询问。

- **快速 AI**：在搜索栏输入后按 **Tab**，就地打开 AI 聊天；Shift+Tab 开始新对话。可以选择模型和思考程度。
  - 按 Esc 回到主页，回答仍会继续，搜索栏会显示进度，按 Tab 返回。■ 停止（或 Ctrl+.）可停止回答。
  - 与其他界面一样，点击外部会关闭启动器；按 Win 键会重新打开同一个对话。如需一直显示，请打开 📌。
- **提前启动**：打开聊天或开始输入时，Bloom 会先启动该聊天所用模型（Claude、ChatGPT 或 omp，只启动其中一个）的 AI，这样第一条消息不必等它启动。几分钟不用或关闭聊天后会结束。
- **ChatGPT 边写边显示**：ChatGPT 聊天（聊天窗口和启动器的快速聊天）会在回答写出时随即显示，而不是最后一次性出现；每个聊天持续使用一个 Codex 进程，而不是每条消息启动一个。
  - Bloom 的 Codex 使用 Bloom 自己的设置运行，因此你自己的 Codex `config.toml` 不会影响 Bloom 的聊天；Codex 登录和之前的聊天会保留。
  - 更新后的第一次 ChatGPT 聊天，因为 Codex 要整理一次历史记录，启动可能会慢一些。
- **omp 聊天**（可选）：如果已安装并登录 [omp](https://github.com/can1357/oh-my-pi)（`omp login`），可选的 AI 就多了一个（设置 › AI 聊天 › omp，Ctrl+Shift+M）；你不选就不会有任何变化。
  - 每个聊天使用一个 `omp --mode rpc` 进程，以及 Bloom 文件夹里独立的 omp 会话。使用 Bloom 的指令代替 omp 自带的提示词，不会加载你自己 omp 的技能、规则和扩展。omp 自带的文件和命令工具是关闭的，网页搜索默认开启。
  - 聊天顶部显示模型记住的对话量（`14.2k / 200k`）和让 omp 总结前半部分的 **整理** 按钮。对话变长时 omp 也会自行整理，并在回答下面用一行说明。
  - `/session`、`/context`、`/usage` 等只读的 omp 命令可在聊天中使用；会更改你 omp 设置的命令不会执行，只给出提示。
  - 在 设置 › AI 聊天 › omp › 我的模型 中选择模型（从 `omp models` 里选，或按 `提供方/模型` 输入）；不选时使用 omp 自己的默认模型。
- **AI 聊天窗口**（Ctrl+Alt+C）
  - 继续以前的对话，并用分组（文件夹）整理。
  - 附加文件和图片（拖放 · Ctrl+O · Ctrl+V）。
  - 打开或保存 AI 生成的文件。
  - 对话标题包含模型、领域和任务，例如 `[◆ Opus • Code ] 修复登录`；只有开始新任务时才会改变。
  - 可以选中多个对话和分组一次删除：对话搜索旁的 ✓、Ctrl+单击，或 ⋯ › 选择。
  - 对话闲置 10 分钟后释放内存。可在 设置 › AI 聊天 中设定分钟或小时（0 = 不释放）。
- **部分复制**：在回答、你发送的消息或代码块上拖选，再按 Ctrl+C 或 右键 › 复制。快速 AI 中也可用。
- **把电脑操作交给 AI**：“把昨天收到的报价单 PDF 移到文档”“volume 30”“找出下载里上周的视频”。
  - 每个会更改数据的操作，都需要在聊天中的卡片上批准（允许 · 在此对话中始终允许 · 拒绝）。
  - 执行结果以 ✓ / ✕ 留在回答下方。
- **笔记、计时器和日程都在 Bloom 里**
  - “把购物清单写进笔记”会把 Bloom 的笔记附在聊天旁并写入其中。
  - “25 分钟计时器”“明天下午 3 点加一个会议”会进入 Bloom 的计时器和日历，计时器工具会出现在聊天旁以便查看。
  - 笔记、计时器和日程使用 Bloom 自己的存储和工具，不会创建文件或打开其他应用。
- **AI 操作**（Ctrl+Alt+A）：在任何应用中，把选中的文字（未选中时为剪贴板内容）转换后再粘贴回去。
  - 总结 · 翻译 · 修正拼写 · 润色 · 缩短 · 扩写 · 正式 · 口语 · 要点列表 · 通俗解释 · 起草回复
  - 也可以写自己的指令。
- **用量**：主页显示 Claude 和 ChatGPT 的 5 小时与每周额度（已用或剩余），每 5 或 10 分钟刷新。支持多个账户（设置 › AI 账户）：每个账户使用 CLI 的官方登录，AI 聊天使用你选择的账户。账户不会自动切换。添加的账户可以用 ×（或菜单）退出登录：Bloom 先运行 CLI 的官方退出登录，再把该账户的文件夹移到回收站。
- **工作通知**：Claude Code 或 Codex 的任务开始、完成、等待批准，或额度将尽、恢复时，在屏幕一角弹出卡片并播放提示音。点击任务卡片可转到该任务所在的终端或编辑器窗口。
  - 可选择 6 个位置之一、大小、声音和通知的事件。
  - 全屏游戏和演示时不显示通知。
  - **在卡片上批准**（设置 › 通知，默认关闭）：Claude Code 请求权限时，卡片上提供“允许”“拒绝”“在终端中回答”。开启后会把 Bloom 的钩子加入 Claude Code 的 `settings.json`，关闭后移除。正在查看该会话窗口时，仍像以前一样在终端中询问。

### 屏幕上的工具 — Bloom Lens · 取色器

<img src="docs/media/zh/lens.webp" alt="用 Bloom Lens 选中屏幕上的英文消息，并在翻译卡片中显示中文译文；旁边是打开 #FF7A9E 的取色器">

- **Bloom Lens**（Ctrl+Alt+L）：拖选屏幕上的区域，然后按编号选择操作。
  - `1` 问 AI：不输入内容直接按 Enter，会解释屏幕内容。
  - `2` 翻译
  - `3` 复制文字（Windows OCR）
  - `4` 实时翻译：Bloom 持续读取该区域，内容变化时重新翻译。
  - `5` 固定到屏幕：把该区域作为图片置顶显示。拖动可移动，滚轮可缩放，Esc 关闭。
  - `6` 截图：把该区域作为图片复制到剪贴板，可直接 Ctrl+V 粘贴。截图也会保存到 Windows 截图工具所用的屏幕截图文件夹，文件名为“Bloom 日期 时间.png”；Bloom 不会删除该文件夹里的任何内容。
  - `7` 调色板：提取该区域的主要颜色（见下）。
  - 翻译（Ctrl+Alt+T）和复制文字（Ctrl+Alt+O）有各自的快捷键。
- **取色器**：用放大镜从屏幕取色，或粘贴颜色代码。
  - 显示 HEX · RGB · HSL，以及与白色、黑色文字的对比度。
  - 显示由该颜色生成的渐变（复制为 CSS）。
  - 保留最近使用的颜色。
- **调色板**（Lens 的 `7`，或截图绘图栏上的调色板按钮）：提取该区域的主要颜色，默认 5 种（用 − + 可调为 3–10 种）。

<img src="docs/media/zh/palette.webp" alt="一张绘制的海报的调色板：五个色块，标有 HEX、RGB 和占比，另有全部复制和所选颜色的推荐渐变">

  - 每个色块显示 HEX、RGB 和占比，点击即可复制 HEX。← → 选择色块。
  - 全部复制可得到 HEX 列表、CSS 变量或调色板图片；选中某种颜色后会给出渐变建议。
  - 在这台电脑上计算，不使用 AI 用量。调色板与最近使用的颜色一起保存。

### 旁边的工具

<img src="docs/media/zh/tools.webp" alt="固定的启动器右侧附着笔记、下方附着计算器">

- **Ctrl+J**（或顶部的工具按钮）以小窗口打开笔记、计时器、计算器、剪贴板记录和快捷键。再按一次关闭菜单。
- **快捷键**工具列出你为应用、文件夹或命令设置的所有快捷键（Ctrl+K › 设置快捷键…）及其按键；单击即可执行。
- **工具的摆放**
  - 拖动顶部移动工具，拖动边缘调整大小。
  - 靠近启动器、其他工具或屏幕边缘时会自动吸附。
  - 放在启动器旁就会以该位置和大小附着：随启动器移动，并随它一起显示或隐藏。
  - 拉开后单独浮动。顶部的图钉可让它始终置顶（默认关闭）。
- **笔记**：多条笔记，边输入边保存。可以置顶，删除的笔记进入回收站。
  - 在搜索栏输入 `note buy milk` 即可立即存为笔记。
  - 笔记也会被搜索。
  - 笔记用 Markdown 书写：可在查看和编辑之间切换，在查看界面直接勾选清单，笔记的 `# 标题` 会成为列表中的名称。Ctrl+滚轮调整字号（Ctrl+0 恢复）。
- **Markdown 文档 (.md)**：在搜索到的 .md 文件上选择“在 Bloom 中查看”，或通过资源管理器的“打开方式”，在 Bloom 的文档窗口中打开。用安装程序安装时，可在 设置 › 功能 › “设为默认应用” 中设置，让资源管理器中双击也用 Bloom 打开。
  - 显示在任务栏中，并可像其他窗口一样贴靠。查看时绘制 Markdown，编辑时显示原文。长文档也能很快打开，可跨段落拖动选择并用 Ctrl+C 复制。
  - 边输入边保存，并保留文件的编码和换行方式。其他程序修改了文件时，会询问是重新读取还是保存你的版本。
  - 链接只打开网页和邮件地址；指向本机文件的链接会在文件夹中显示该文件。网络文件夹中的图片只在文档位于同一文件夹时才加载。
- **计时器**：设置天、时、分、秒（▲ ▼、鼠标滚轮、方向键或数字），在下方写上用途，按开始或 Enter。全为零时会把这一行当作文字读取：`10 min`、`3pm meeting`。显示剩余时间，并可延长 5 分钟或取消。
- **计算器**：与搜索栏相同的计算。Enter 复制答案并留在列表中；顶部的垃圾桶清空列表。
- **剪贴板记录**（Ctrl+Alt+V）：复制过的文字、图片和文件。单击即可再次复制；需要保留的可以置顶。
- **固定 · 移动 · 缩放启动器**
  - Ctrl+P 把启动器固定在最上层：点击其他窗口时也不会被遮住（固定时显示 📌；在快速 AI 中，📌 位于 ⚙ 的位置）。
  - 拖动搜索栏（文字右侧的空白处）或底栏即可移动，也能移到其他显示器；双击其中任一处会回到中央。
  - 搜索栏末端的 ⚙（Ctrl+,）打开设置；在快速 AI 中请用 Ctrl+,。
  - 拖动启动器的边缘和角可调整大小。放大后文字和图标大小不变，显示的内容变多。大小会被记住，打开时按屏幕调整；可在 设置 › 常规 中重置。
  - Ctrl+= / Ctrl+- / Ctrl+0 把整个 Bloom（启动器、工具、AI 聊天和设置）缩放到 80–150 %。

### Bloom Space Cleaner

<img src="docs/media/zh/space.webp" alt="按大小把文件夹中的视频、下载、照片、项目显示为方块的地图，以及大文件列表">

以地图形式显示驱动器和文件夹的占用空间。每个文件和文件夹占据与其大小成比例的方块。

- 单击方块进入其中；从旁边的列表中选出大文件并移到回收站。
- 可以切换驱动器或选择要查看的文件夹。
- 只能删除驱动器根目录以下、Windows 文件夹以外的文件和文件夹。移到回收站的项目可从回收站恢复。

### 编辑主页

<img src="docs/media/zh/home.webp" alt="在较宽的启动器中分为两栏的主页编辑界面：左侧是 AI 用量、计时器和本周，右侧是收藏和命令，每个区块带有按钮，并有“添加区块”一行">

空搜索栏会显示主页：收藏、AI 用量、本周、计时器、最近使用、命令等区块。按 Ctrl+E（或“编辑主页”）进行编辑。

- 拖动区块的标题行即可移动，落点处会显示空位。也可用 ▲ ▼ ‹ › 按钮移动，用 ✕ 隐藏。
- 列表区块可用 − + 设置显示条数，点击标题可折叠区块。
- 启动器较宽时主页分为两栏，用 ‹ › 把区块移到另一栏。启动器可拖动边缘调整大小（见上）。
- 每栏末尾的“+ 添加区块”会以卡片列出已隐藏的区块。点击卡片会添加到该栏末尾，拖动则添加到放下的位置。
- AI 用量卡片也可以在这里调整顺序，并可为每个账户显示自定义名称以代替邮箱。

### 设置与自定义命令

<img src="docs/media/zh/yours.webp" alt="设置中的功能页面（每项功能一个开关），以及包含动画、玻璃效果和内存的性能页面">

- **自定义命令**：用关键词执行常用操作。
  - 脚本：.ps1 · .bat · 程序，或一行 PowerShell。无窗口运行，结果以通知卡片显示。
  - 片段：把保存的文字粘贴到最前面的窗口（`{date}` `{time}` `{clipboard}`）。
  - 连续执行：依次运行搜索栏中的多行（`notepad ; volume 30 ; wait 2 ; yt lofi`）。
  - 关键词后面的内容会填入 `{input}`（`bk D:\photos`）。
  - 脚本文件从 Ctrl+K › 自定义命令 添加。
- **功能开关**：关闭某项功能（AI 聊天 · Bloom Lens · 计算器 · 计时器 · 取色器 · 表情符号 · 切换窗口等）后，它会从搜索结果、命令、主页和收藏中移除，并释放其快捷键。已保存的数据会保留。
- **外观 · 性能**
  - 深色、浅色或跟随系统的主题，以及强调色预设
  - 动画开启 · 减少 · 关闭，每秒帧数（30 · 60 · 最大），关闭玻璃效果或用滑块调整透明度（15–75%）
  - 默认用 CPU 绘制。GPU 绘制为实验功能，在部分电脑上画面可能卡顿或停住几秒，在首次启动时或 设置 › 性能 中选择
  - 占用内存的功能会在设置中注明增加的用量。
- **语言 · 首次设置 · 迁移**
  - 한국어 / English / 日本語 / 简体中文（自动时跟随 Windows）；更改后 Bloom 会重启。在任何语言下，搜索词、时间和单位都用英语或韩语输入。
  - 首次设置包括打开 Bloom 的方式、主页和 AI 功能。
  - 可以把设置、收藏、应用名称和浏览器快捷方式导出为一个文件，在另一台电脑上导入。
- **问题报告**：在 设置 › 关于 › 问题报告 中，复制版本、Windows 和最近日志（隐藏用户名和电子邮件地址），并打开 GitHub 问题页面。Bloom 本身不发送任何内容。
- **卡顿记录**：Bloom 停止响应半秒以上时，日志会记录。

## 快捷键

| 按键 | 作用 |
| --- | --- |
| `Win` · `Alt+Space` | 打开 Bloom（在以管理员身份运行的应用上按 `Win`：设置 › 常规 › 在管理员应用上也使用 Win 键） |
| `Tab` / `Shift+Tab` | 把搜索栏中的内容问 AI / 在新对话中问 |
| `Ctrl+K` | 所选结果可执行的所有操作 |
| `Ctrl+1` … `Ctrl+9` | 收藏 |
| `Ctrl+J` | 工具（笔记 · 计时器 · 计算器 · 剪贴板 · 快捷键） |
| `Ctrl+P` | 把启动器固定在最上层 |
| `?` | 在空搜索栏中：按类别列出 Bloom 能理解的输入 |
| `Ctrl+E` | 编辑主页 |
| `Ctrl+=` · `Ctrl+-` · `Ctrl+0` | 放大 · 缩小 · 100 % |
| `Esc` | 返回 → 清除输入 → 关闭（快速 AI 的回答会继续） |
| `Ctrl+.` | 停止快速 AI 的回答 |
| `Ctrl+Alt+C` | AI 聊天窗口 |
| `Ctrl+Alt+A` | AI 操作 |
| `Ctrl+Alt+L` | Bloom Lens |
| `1` … `7` | 在 Bloom Lens 中：询问 AI · 翻译 · 复制文字 · 实时翻译 · 固定到屏幕 · 截图 · 调色板 |
| `← →` · `−` `+` · `Enter` | 在调色板中：选择颜色 · 减少或增加颜色数 · 复制 HEX |
| `Ctrl+Alt+T` · `Ctrl+Alt+O` | 翻译屏幕 · 复制屏幕上的文字 |
| `Ctrl+Alt+V` | 剪贴板记录 |

全局快捷键可在设置中按功能更改或关闭：单击后按下按键，Backspace 表示关闭。任何键都可以与 Ctrl、Alt、Shift 或 Win 组合；不输入字符的键（F1–F24、PrintScreen、Pause、媒体键）也可以单独使用。切换窗口、表情符号、取色器、就屏幕问 AI、实时翻译和截图也可以设置快捷键（默认关闭）。

## 关于 AI 功能

AI 功能使用本机已安装并登录的官方 CLI。登录凭据保存在这些 CLI 中。

- Claude：[Claude Code](https://docs.anthropic.com/en/docs/claude-code) CLI（`claude`）
- ChatGPT：[Codex CLI](https://github.com/openai/codex)（`codex`）
- omp（可选）：[oh-my-pi](https://github.com/can1357/oh-my-pi) CLI（`omp`）

用量和条款以你自己的账户为准。没有这些 CLI 时只有 AI 功能不可用，其他功能照常使用。

**AI 在电脑上的操作**（Claude · ChatGPT，设置 › 电脑操作 · MCP）

- Bloom 通过只在本机开放的 MCP 服务器（`Bloom.exe --mcp`，只有当前 Windows 用户能打开的命名管道）提供一个工具（`pc`）。
- 可执行的操作分为五组，每组都可在设置中开关。
  - **查询**：应用 · 文件（Everything）· 最近的文件 · 窗口 · 计时器和日程 · 笔记 · 自定义命令。无需确认，立即执行。
  - **打开 · 运行**：打开应用、文件和网站，运行搜索栏中的一行（`volume 30`、`yt lofi`），运行自定义命令。
  - **笔记 · 计时器 · 日历**：写入 Bloom 的笔记（显示在聊天旁），启动计时器，在日历中添加日程。
  - **整理文件**：重命名 · 移动 · 复制 · 移到回收站。
  - **剪贴板**：读取 · 写入。
- 除查询外的所有操作，都需要在**聊天中的 允许 / 在此对话中始终允许 / 拒绝 按钮**上批准（Claude Code 的 `--permission-prompt-tool stdio`）。结果以 ✓ / ✕ 留在回答下方。
- 本机已配置的其他 MCP 服务器也显示在同一页面，每个服务器一个开关（默认关闭）。
  - 从 Claude Code · Claude Desktop · Cursor · VS Code · Windsurf · Codex 的设置，以及用 Claude Code 打开的项目的 `.mcp.json` 中查找。
  - 每个工具操作都需要在同一种卡片上批准。
  - 对于 ChatGPT，由 Bloom 的中继（`Bloom.exe --mcp-proxy`）连接服务器并处理批准。
  - 每次都从原始文件读取服务器设置。
- 工具定义只有一个，与不带工具相比，每次请求约多 500 个输入 token。加入笔记、计时器和日历之前实测为 400，当前数值是包含其描述长度的估算。关闭分组可减少 token。只加载所选的 MCP 服务器（`--strict-mcp-config`）。
- ChatGPT（Codex）对话使用同一个工具。
  - Codex 在 exec 模式下无法自行询问，因此由 Bloom 的 MCP 服务器在执行前显示同样的卡片并等待回答。
  - 服务器只通过每次对话运行时的 `-c mcp_servers.bloom…` 连接。
  - Codex 只在需要时加载 MCP 工具说明，因此每次请求都附上一行工具名称和操作（约 +250 token，按同样方法估算）。
- omp 对话使用同一个工具。
  - omp 同样以 `--no-ui` 运行、无法自行询问，因此由 Bloom 的 MCP 服务器在执行前显示同样的卡片。Bloom 的工具和已开启的其他服务器只写入 `%LOCALAPPDATA%\Bloom\ai\omp-work` 下每个对话各自文件夹里的 `.omp/mcp.json`，不会改动 omp 自身的设置。

AI 用量来自 `claude -p /usage`（不调用模型）和 `codex app-server` 的额度查询，按账户依次获取（添加的账户使用 `%LOCALAPPDATA%\Bloom\accounts` 下的文件夹作为 `CLAUDE_CONFIG_DIR` / `CODEX_HOME`）。选择 ChatGPT 轻量方式时，会用该账户的 Codex 登录向 Codex 状态页读取的地址发送一次 HTTPS 请求。这不是官方 API；Bloom 只在自己创建的文件夹中刷新令牌，不会在 `~/.codex` 中刷新。Bloom 不读取 Claude 登录信息。工作通知读取本机的 Claude Code 和 Codex 日志（`~/.claude/sessions`、`~/.codex/sessions`），日志不会离开本机。
检查更新时通过 GitHub API（`api.github.com`）查询本仓库的最新版本，并从 GitHub 下载新版本。请求中只包含仓库信息；设置、文件和对话都留在本机。可在 设置 › 关于 中关闭检查。
发送给 AI 的只有你自己发送的内容（问题、附件、用 Bloom Lens 选中的屏幕区域、要翻译的文字），以及电脑工具返回的结果。
Claude 只能读写该对话的文件夹（`%LOCALAPPDATA%\Bloom\ai\files\<chat>`）。ChatGPT（Codex）和 omp 不直接处理文件，因此文本附件会放进消息中，生成的文件会从回答中取出并保存。

Bloom 是非官方的个人项目，与 Anthropic、OpenAI、voidtools 和 Raycast 无关。

## 安装 · 构建

只想使用的话，请从[下载](#下载)获取安装程序。自行构建需要 Windows 10/11 和 [.NET 10 SDK](https://dotnet.microsoft.com/download)。`tools\package.ps1` 还需要 C++ 生成工具（Visual Studio 的“使用 C++ 的桌面开发”），用于 Win 键助手。

```powershell
dotnet build src\Bloom\Bloom.csproj -c Release
# 或者生成安装用副本
powershell -File tools\package.ps1   # → dist\app
powershell -File tools\install.ps1   # → %LOCALAPPDATA%\Programs\Bloom，开始菜单快捷方式
```

数据保存在 `%APPDATA%\Bloom` 和 `%LOCALAPPDATA%\Bloom`。
开发时可用 `Bloom.exe --data <文件夹>` 使用另一个数据文件夹（`--lang ko|en` 固定语言）。
只有安装程序版本支持自动更新；用 `tools\install.ps1` 生成的副本需手动更新。

**发布**：提高 `src/Bloom/Bloom.csproj` 中的 `<Version>`，在 `CHANGELOG.md` 的 `## <Version>` 下写明变更（这会成为发布说明；没有时使用提交列表），推送后在 GitHub 运行 **Actions › Release › Run workflow**。它会运行测试、构建，用 [Velopack](https://velopack.io) 打包安装程序和更新包，并以 `v<Version>` 发布；已安装的副本会在下次启动时找到它。

## 代码结构

<details>
<summary>文件夹 · 约定 · 测试与 lint</summary>

`src/Bloom` 下一个文件夹对应一个功能。较大的类按主题拆分为 `partial` 文件。

| 文件夹 | 内容 |
| --- | --- |
| `App/` | `Host`（整个应用的连接：启动 · 快捷键 · 命令 · Lens · 计时器和颜色 · 运行自定义命令 · 文件操作）、工作通知（`Notices`）、从 GitHub Releases 更新（`Updates`，Velopack）、MCP 服务器（`Mcp/` — 中继 · 服务器 · `pc` 工具）、启动选项、托盘、Win 键钩子（管理员应用的助手：`src/WinKeyHelper`，NativeAOT） |
| `App/Engine/` | 生成搜索栏显示的内容 — 根搜索、主页区块、项目转换，以及各视图（文件 · 剪贴板 · 日历 · 计时器 · 颜色等） |
| `App/Qa/` | 屏幕外截图 QA（`--capture <文件夹> --qa-set v15 --data <文件夹> --lang en`） |
| `Core/` | 保存的数据（设置 · 收藏 · 使用记录 · 按输入的学习 · 计时器 · 笔记 · 颜色）和 JSON 存储、韩语搜索、匹配、界面语言（`Lang` — `Tr("한국어", "English")`） |
| `Search/` | 应用列表、计算器（计算 · 单位 · 货币）、Everything、已打开的窗口、最近的文件、表情符号 · 符号（`Assets/symbols.tsv`）、系统项目、自定义命令（`UserCommands`）、结果模型 |
| `Ai/` | Claude Code / Codex / omp CLI 会话与对话 · 分组（`Chats`）、一次性提问（`OneShot`）、翻译、AI 操作、AI 图标、用量（`AiUsage`）、读取工作日志（`ClaudeWatch` · `CodexWatch`） |
| `Ocr/` | 选择屏幕区域、Windows OCR、翻译窗口、取色 |
| `Clip/`、`Space/`、`Browser/` | 剪贴板记录、磁盘空间地图、交给浏览器 |
| `Native/` | Win32 调用（仅 System32 的 DLL）、WinRT 调用（`WinRt`）、系统开关（`SystemControl` — 声音 · 无线 · 深色模式等）、与资源管理器相同的文件操作（`ShellFiles`） |
| `Ui/Tools/` | 工具窗口：自由摆放 · 附着 · 磁吸 · 调整大小（`ToolDock`）、窗口框架（`ToolWindow`）、笔记 · 计时器 · 计算器 · 剪贴板 |
| `Ui/` | 启动器窗口（`Launcher/` — 所有视图的标签 · 提示 · 结果集中在 `Launcher.ViewSpecs.cs`）、AI 聊天窗口（`Chat/`）、设置窗口（`Settings/`）、通知卡片、共用控件（`Controls/` — 连续曲率圆角 `SmoothBorder`、`Dropdown`、`PopupMenu`）、主题（`Theme/` — 动画设置在 `Motion`） |
| `tests/Bloom.Tests` | 自动测试（计算器 · 时间解析 · 日期与世界时钟 · 输入的路径 · winget 表格 · 快捷键 · 匹配 · 学习 · 表情符号 · 语言 · AI 提示词 · MCP 工具 · 用量解析 · 工作日志 · 自定义命令）— 不使用测试框架的控制台程序 |

约定：新功能拆分为 `Engine.<功能>.cs`（结果）+ `Launcher.ViewSpecs.cs` 中的一行视图 + 需要时的 `Core/<功能>.cs`（存储）。
屏幕上的所有文字都写成 `Tr("한국어", "English")`。日文和简体中文来自 `src/Bloom/Assets/Lang/ja.tsv` 和 `zh.tsv`，以英文文本为键（含插值的文本每个占位用 `{0}`）；`python tools/lang.py check` 列出缺失项，`todo ja` 列出待翻译的文本。搜索关键词和解析数据保持韩语和英语。
界面文字遵循 [docs/COPY.md](docs/COPY.md)。
QA 和开发运行始终使用 `--data <临时文件夹>`，与真实数据分开。

```powershell
# 测试（退出码 = 失败数）
dotnet run --project tests\Bloom.Tests
# lint（分析器 + 格式）— 零警告
dotnet build src\Bloom\Bloom.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet build tests\Bloom.Tests\Bloom.Tests.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet format whitespace src\Bloom\Bloom.csproj --verify-no-changes
dotnet format whitespace tests\Bloom.Tests\Bloom.Tests.csproj --verify-no-changes
```

</details>

## 可选

安装以下程序后，相关的功能、区块和设置即可使用。

- **Everything**：查找文件。
- **Claude Code CLI · Codex CLI · omp**：AI 聊天 · Lens · 翻译 · 快捷操作 · 用量 · 工作通知。

## 许可证

MIT — [LICENSE](LICENSE)。
汇率：[Rates By Exchange Rate API](https://www.exchangerate-api.com)。
