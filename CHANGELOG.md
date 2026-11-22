# Changelog

Each release on GitHub shows its section from this file (the workflow looks for `## <version>`).

## 1.4.3

[![With Auto, PC actions ask only when running a newly downloaded or created file](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.4.3/auto-en.webp)](bloom://settings/mcp)

- AI chat permissions now have an 'Auto' option, which is the new default. Claude chats run in Claude Code's auto mode, and PC actions ask only when running a newly downloaded or created file or reading the clipboard.
- Actions set to 'Always allow' no longer ask again when opening a program or script.
- In AI chat, long answers stream smoothly all the way to the end.
- Bloom Space Cleaner no longer freezes the window while counting a large file list.
- Floating cards such as notification cards, alarms and the screen recording bar draw more lightly. Fixed the screen recording bar's shadow being clipped into a rectangle.
- Add-ons: the quick settings window includes only taskbar add-ons that have controls there, using less memory.
- Add-ons: Memory Clean asks every time before ending a program running as administrator. The admin helper needs to be reinstalled once.
- Fixed: Store app windows could show another app's name and icon, and clipboard history could show the wrong source app.
- Fixed: sound in screen recordings could briefly go out of sync.

### 한국어

[![자동에서는 PC 조작이 새로 받거나 만든 파일을 실행할 때만 묻습니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.4.3/auto-ko.webp)](bloom://settings/mcp)

- AI 채팅 권한에 '자동'이 생겨 기본값이 됩니다. Claude 대화는 Claude Code의 자동 모드로 동작하고, PC 조작은 새로 받거나 만든 파일을 실행할 때와 클립보드를 읽을 때만 묻습니다.
- '항상 허용'으로 둔 작업은 프로그램이나 스크립트를 열 때도 다시 묻지 않습니다.
- AI 채팅에서 긴 답도 끝까지 부드럽게 나옵니다.
- Bloom Space Cleaner가 큰 파일 목록을 세는 동안에도 창이 멈추지 않습니다.
- 알림 카드, 알람, 화면 녹화 막대처럼 떠 있는 카드가 더 가볍게 그려집니다. 화면 녹화 막대의 그림자가 네모나게 잘리던 문제도 고쳤습니다.
- 애드온: 빠른 설정 창에는 그곳에서 쓸 기능이 있는 작업표시줄 애드온만 들어가 메모리를 덜 씁니다.
- 애드온: 메모리 정리는 관리자 권한으로 실행 중인 프로그램을 끝낼 때마다 먼저 묻습니다. 관리자 도우미를 한 번 다시 설치해야 합니다.
- 고침: Store 앱 창의 이름과 아이콘이 다른 앱 것으로 보이던 문제, 클립보드 기록의 출처 앱이 잘못 나오던 문제.
- 고침: 화면 녹화에서 소리가 잠깐 어긋나던 문제.

## 1.4.2

- AI chats are more careful with your PC. Claude asks before it fetches a web page, and each chat works in its own folder. Codex no longer uses its own shell, so every action on the PC shows a card in the chat. Opening a program, script or shortcut is asked every time, even with Always allow.
- Links in AI answers open only web addresses. A file the AI made that would run is shown in its folder, never run.
- Other programs on the PC can no longer use Bloom's PC tool.
- Importing a settings file keeps this PC's mini player programs and JRiver address, and scheduled AI commands come in switched off until you turn them on.
- Custom commands refuse a typed or copied value that could change what the command runs and say which character to remove. A full path copied with its quotes works as it is.
- Playlists play songs from another computer only when the list is on that same share. Settings warns when the JRiver password would go over plain http.
- Problem reports hide more, such as paths, web addresses and keys.
- Add-ons can open Bloom's settings on their own page.
- Fixed: with omp 18, omp chats didn't start, stayed busy after the answer, or didn't show errors such as a model the account can't use.

### 한국어

- AI 채팅이 PC를 더 조심해서 다룹니다. Claude는 웹 페이지를 가져오기 전에 묻고, 대화마다 자기 폴더에서 작업합니다. Codex는 자체 셸을 쓰지 않아 PC 작업마다 채팅에 카드가 뜹니다. 프로그램, 스크립트, 바로 가기를 여는 일은 항상 허용이어도 매번 묻습니다.
- AI 답변의 링크는 웹 주소만 엽니다. AI가 만든 파일 중 실행되는 파일은 실행하지 않고 폴더에서 보여 줍니다.
- PC의 다른 프로그램은 이제 Bloom의 PC 도구를 쓸 수 없습니다.
- 설정 파일을 가져와도 이 PC의 미니 플레이어 프로그램과 JRiver 주소는 그대로이고, 예약된 AI 명령은 꺼진 채로 들어와 직접 켜야 합니다.
- 사용자 명령은 실행 내용을 바꿀 수 있는 입력값이나 복사한 값을 거절하고 어떤 글자를 빼야 하는지 알려 줍니다. 따옴표째 복사한 전체 경로는 그대로 쓸 수 있습니다.
- 재생 목록은 다른 컴퓨터의 곡을 목록이 같은 공유 폴더에 있을 때만 재생합니다. JRiver 비밀번호가 암호화 없는 http로 오가면 설정에 경고가 나옵니다.
- 문제 보고에서 경로, 웹 주소, 키 같은 정보를 더 많이 가립니다.
- 애드온이 Bloom 설정을 자기 페이지로 바로 열 수 있습니다.
- 고침: omp 18에서 omp 채팅이 시작되지 않거나, 답이 와도 계속 작업 중으로 남거나, 계정에서 쓸 수 없는 모델 같은 오류가 보이지 않던 문제를 고쳤습니다.

## 1.4.1

[![After a Windows update the taskbar add-on turns on and runs on symbols borrowed from an earlier build](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.4.1/borrowed-en.webp)](bloom://settings/addons)

- After a Windows update, add-ons can run before Microsoft publishes the symbol files for the new build, by borrowing the symbols of an earlier build. This comes with PillTaskbar 0.2.15, Alt+Tab per monitor 1.2.1-9, Monitor Brightness 0.2.2, Coffee 0.1.7 and Memory Clean 0.3.2.
- Settings › Add-ons shows when an add-on runs on borrowed symbols, when it waits for Microsoft's symbol files, and when it was turned off because Explorer crashed while it ran on them.
- Fixed: after a Windows update, add-ons could stop with "Could not download the symbol files" even though the internet worked. Microsoft hadn't published the symbol files for the new Windows build yet. Bloom now tries again every 30 minutes and turns the add-on on once the files are there, and the message explains this.

### 한국어

[![Windows 업데이트 뒤 작업표시줄 애드온이 켜지고 이전 빌드에서 빌린 기호로 작동합니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.4.1/borrowed-ko.webp)](bloom://settings/addons)

- Windows 업데이트 뒤 Microsoft가 새 빌드의 기호 파일을 올리기 전에도 애드온이 이전 빌드의 기호를 빌려 작동할 수 있습니다. PillTaskbar 0.2.15, 모니터별 Alt+Tab 1.2.1-9, 모니터 밝기 조정 0.2.2, 커피 0.1.7, 메모리 정리 0.3.2부터 들어 있습니다.
- 설정 › 애드온에 애드온이 빌린 기호로 작동하는지, Microsoft의 기호 파일을 기다리는지, 빌린 기호로 작동하다 탐색기가 멈춰 꺼졌는지 나옵니다.
- 고침: Windows 업데이트 뒤 인터넷이 정상인데도 애드온이 "기호 파일을 받지 못했습니다"로 멈출 수 있었습니다. Microsoft가 새 Windows 빌드의 기호 파일을 아직 올리지 않았기 때문입니다. 이제 Bloom이 30분마다 다시 시도해 파일이 올라오면 애드온을 켜고, 안내 문구도 이 내용을 설명합니다.

## 1.4.0

[![Claude Code finishes a task, a click on Bloom's card opens the taskbar's usage window with that session's chat](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.4.0/chat-en.webp)](bloom://settings/notices)

- A click on a Claude Code or Codex card can open that session's chat on the taskbar instead of its app. Pick it under Clicking a card in Settings › Work notifications. It needs PillTaskbar 0.2.14 or later.
- Checking for updates in Settings › About checks the add-ons too, and the ones turned on update right away.
- Add-ons now bring their own words and pictures for Settings › Add-ons, so an add-on update alone can change its page. An add-on not yet updated shows its settings by their plain schema names until it is.

### 한국어

[![Claude Code가 작업을 끝내고, Bloom 카드를 누르면 작업표시줄 사용량 창에서 그 세션의 대화가 열립니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.4.0/chat-ko.webp)](bloom://settings/notices)

- Claude Code와 Codex 카드를 누르면 앱 대신 작업표시줄에서 그 세션의 대화를 열 수 있습니다. 설정 › 작업 알림의 카드를 누르면에서 고르며, PillTaskbar 0.2.14 이상이 필요합니다.
- 설정 › 정보에서 업데이트를 확인하면 애드온도 함께 확인하고, 켜 둔 애드온은 바로 업데이트합니다.
- 설정 › 애드온의 이름, 설명, 그림을 이제 애드온이 직접 싣습니다. 애드온만 업데이트해도 그 페이지가 바뀌고, 아직 업데이트하지 않은 애드온은 그때까지 기본 이름으로 보입니다.

## 1.3.0

[![Claude Code asks two questions in the terminal, they are answered on Bloom's card with a click each, and the answers reach the terminal](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.3.0/question-en.webp)](bloom://settings/notices)

- When Claude Code asks you a question, its approval card shows the question and the choices. Pick one or several, or type your own answer, and it goes back to Claude Code.
- Windows notifications: other apps' banners can show as Bloom's cards instead. Turn it on in Settings › Work notifications. It needs PillTaskbar 0.2.13 or later, and a click on the card opens Windows' notification center.
- Settings › Add-ons is easier to read. Choices show as small pictures, every line has a plain name and a short note, and programs to leave out can be picked from the ones running now.
- Fixed: number fields in the add-on settings show their range on one line.

### 한국어

[![터미널의 Claude Code가 질문 두 개를 하고, Bloom 카드에서 클릭으로 답하면 그 답이 터미널로 갑니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.3.0/question-ko.webp)](bloom://settings/notices)

- Claude Code가 질문하면 승인 카드에 질문과 선택지가 나옵니다. 하나나 여러 개를 고르거나 직접 답을 적으면 Claude Code로 전해집니다.
- Windows 알림: 다른 앱의 알림 배너를 Bloom 카드로 대신 띄울 수 있습니다. 설정 › 작업 알림에서 켜며, PillTaskbar 0.2.13 이상이 필요합니다. 카드를 누르면 Windows 알림 센터가 열립니다.
- 설정 › 애드온이 읽기 쉬워졌습니다. 선택지는 작은 그림으로 고르고, 줄마다 쉬운 이름과 짧은 설명이 붙고, 제외할 프로그램은 지금 실행 중인 것에서 고릅니다.
- 고침: 애드온 설정의 숫자 칸 범위가 한 줄로 나옵니다.

## 1.2.1

- Fixed: an add-on's setting slider can be dragged all the way to either end without stopping or jumping back to its start.
- Fixed: Settings › Taskbar shows the settings a newer add-on adds.
- Fixed: reinstalling an add-on of the same version no longer fails while Windows still holds its old files. The add-on starts right away.

### 한국어

- 고침: 애드온 설정 슬라이더를 끝까지 끌어도 멈추거나 처음 값으로 돌아가지 않습니다.
- 고침: 설정 › 작업표시줄에 새 버전 애드온이 더한 설정이 나옵니다.
- 고침: 같은 버전 애드온을 다시 설치할 때 Windows가 옛 파일을 붙잡고 있어도 실패하지 않고 바로 시작합니다.

## 1.2.0

[![In the clipboard's Snippets tab, the last snippet is dragged to the top and the others make room](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.2.0/reorder-en.webp)](bloom://settings/shortcuts)

- Snippets: drag one up or down in the Snippets tab to change the order.
- Update history: Settings › About lists every version since 1.0.0. Pick one to see what changed. The notes come from GitHub when you open them, so Bloom no longer carries them.
- In Settings, the mouse's back button goes from a sub-page, such as an add-on's page, to the page above it.
- Fixed: at a larger size, a snippet's right-click menu stays inside the clipboard view instead of running past its left edge.

### 한국어

[![클립보드의 상용구 탭에서 마지막 상용구를 맨 위로 끌어 옮기면 나머지가 자리를 내줍니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.2.0/reorder-ko.webp)](bloom://settings/shortcuts)

- 상용구: 상용구 탭에서 항목을 위아래로 끌어 순서를 바꿉니다.
- 업데이트 기록: 설정 › 정보에서 1.0.0부터의 버전을 골라 바뀐 점을 봅니다. 기록은 열 때 GitHub에서 불러오므로 Bloom 안에 더는 담지 않습니다.
- 설정의 하위 페이지(애드온 페이지 등)에서 마우스 뒤로 버튼을 누르면 위 페이지로 돌아갑니다.
- 고침: 크기를 키워도 상용구 우클릭 메뉴가 클립보드 화면 왼쪽 밖으로 잘리지 않습니다.

## 1.1.0

[![Ctrl+Alt+V opens the clipboard, the Snippets tab lists the saved texts, and a click pastes one into the mail in front](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.1.0/snippets-en.webp)](bloom://settings/shortcuts)

- Snippets: the clipboard view (Ctrl+Alt+V) has a Snippets tab next to History. Click a saved text or press Enter to paste it into the window in front.
- Keep a copied text with Ctrl+K › Save as a snippet. Settings › Shortcuts can give the Snippets tab a key of its own.
- Menus open and move without lag when GPU drawing is off.
- Icons sit in the middle of their tiles.

### 한국어

[![Ctrl+Alt+V로 클립보드를 열고, 상용구 탭에서 저장한 글을 눌러 앞의 메일에 붙여넣습니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.1.0/snippets-ko.webp)](bloom://settings/shortcuts)

- 상용구: 클립보드 화면(Ctrl+Alt+V)의 기록 옆에 상용구 탭이 생겼습니다. 저장한 글을 누르거나 Enter를 누르면 앞의 창에 붙여넣습니다.
- 복사한 글은 Ctrl+K › 상용구로 저장으로 남깁니다. 설정 › 단축키에서 상용구 탭을 바로 여는 키를 정할 수 있습니다.
- GPU로 그리기를 꺼도 메뉴가 끊김 없이 열리고 움직입니다.
- 아이콘이 칸 가운데에 놓입니다.

## 1.0.0

[![The island at the top opens into the pill taskbar, a folder opens and the app in it starts](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/taskbar.webp)](bloom://settings/addon:pill-taskbar)

- Taskbar add-on: point at the island to open the pill taskbar. Folders get fourteen icons that follow the taskbar's light or dark look.

[![The RAM module next to the clock opens its window, and Clean now frees memory](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/memory-en.webp)](bloom://settings/addon:memory-clean)

- Memory Clean add-on: a RAM module next to the clock shows the use. Click it and Clean now frees memory.

[![The monitor next to the clock opens its window, one slider moves both monitors, and the wheel over it turns them down](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/brightness-en.webp)](bloom://settings/addon:brightness)

- Monitor Brightness add-on: a monitor next to the clock shows the brightness. One slider moves every monitor, and the wheel over the icon changes it too.

[![With the cup off, ten minutes without input turn the screen off; with the cup on for an hour, the same ten minutes leave it on](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/coffee-en.webp)](bloom://settings/addon:keep-awake)

- Coffee add-on: a coffee cup next to the clock keeps the computer and the screen awake, until turned off or for a set time. Right-click the cup to turn it on or off.

[![Alt+Tab opens on the monitor with the cursor and lists only its windows](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/alttab.webp)](bloom://settings/addon:alt-tab-per-monitor)

- Alt+Tab per monitor add-on: Alt+Tab opens on the monitor under the cursor, with only that monitor's windows.
- In Bloom, click a picture to open that add-on in Settings › Add-ons.
- Screen recording: pick an area with Bloom Lens and record it to MP4, with system sound and the microphone.
- Settings › Add-ons: an overview with a moving picture of each add-on, and a page for each.
- Add-ons run as programs of their own and follow Bloom's theme and glass setting.
- Add-ons update themselves from their latest release, without waiting for a new Bloom.
- Hide a result from search and home with Ctrl+K › Hide from search. Settings › Hidden items brings it back.
- My AI commands: saved prompts with steps and schedules. The notification center keeps finished tasks, limits, approvals and command results in one place.
- Bloom uses less memory.
- Fixed: Bloom Lens draws the picked area and its bar on every monitor, clear of the island.

### 한국어

[![위 끝의 아일랜드가 알약 작업표시줄로 펼쳐지고, 폴더를 열어 안의 앱을 켭니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/taskbar.webp)](bloom://settings/addon:pill-taskbar)

- 작업표시줄 애드온: 아일랜드에 포인터를 올리면 알약 작업표시줄이 펼쳐집니다. 폴더 아이콘 14종이 생겼고, 작업표시줄의 라이트·다크에 맞춰 바뀝니다.

[![시계 옆 램 모듈을 누르면 창이 열리고, 지금 정리로 메모리를 비웁니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/memory-ko.webp)](bloom://settings/addon:memory-clean)

- 메모리 정리 애드온: 시계 옆 램 모듈에 사용량이 나옵니다. 누르고 지금 정리를 누르면 메모리를 비웁니다.

[![시계 옆 모니터를 누르면 창이 열리고, 슬라이더 하나로 두 모니터가 함께 밝아지며, 휠로 다시 어둡게 합니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/brightness-ko.webp)](bloom://settings/addon:brightness)

- 모니터 밝기 애드온: 시계 옆 모니터 모양에 밝기가 나옵니다. 슬라이더 하나로 모든 모니터를 함께 바꾸고, 아이콘 위에서 휠을 돌려도 바뀝니다.

[![커피가 꺼져 있으면 입력 없이 10분 뒤 화면이 꺼지고, 커피를 1시간 켜 두면 같은 10분이 지나도 화면이 켜져 있습니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/coffee-ko.webp)](bloom://settings/addon:keep-awake)

- 커피 애드온: 시계 옆 커피잔으로 PC와 화면이 잠들지 않게 합니다. 끌 때까지나 정한 시간 동안 깨어 있고, 컵을 오른쪽 클릭하면 켜고 끕니다.

[![커서가 있는 모니터에 Alt+Tab이 뜨고 그 모니터의 창만 보입니다](https://raw.githubusercontent.com/hyeon1035298/Bloom/main/docs/media/whatsnew/1.0.0/alttab.webp)](bloom://settings/addon:alt-tab-per-monitor)

- 모니터별 Alt+Tab 애드온: Alt+Tab이 커서가 있는 모니터에 뜨고 그 모니터의 창만 보입니다.
- Bloom에서 그림을 누르면 설정 › 애드온의 그 애드온으로 갑니다.
- 화면 녹화: Bloom Lens로 범위를 골라 시스템 소리, 마이크와 함께 MP4로 녹화합니다.
- 설정 › 애드온: 애드온마다 움직이는 그림이 있는 개요와 애드온별 페이지가 생겼습니다.
- 애드온이 따로 된 프로그램으로 돌고, Bloom의 테마와 유리 효과를 따릅니다.
- 애드온은 새 Bloom을 기다리지 않고 자기 최신 릴리스로 스스로 업데이트됩니다.
- Ctrl+K › 검색에서 숨기기로 결과를 검색과 홈에서 숨깁니다. 설정 › 숨긴 항목에서 되돌립니다.
- 내 AI 명령: 자주 쓰는 프롬프트를 단계와 일정과 함께 저장합니다. 알림 센터가 작업 완료, 한도, 승인 대기, 명령 결과를 한곳에 모읍니다.
- Bloom이 메모리를 덜 씁니다.
- 고침: Bloom Lens가 고른 범위와 막대를 모든 모니터에, 아일랜드를 피해 그립니다.
