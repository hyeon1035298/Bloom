# Changelog

Each release on GitHub shows its section from this file (the workflow looks for `## <version>`).

## 0.17.0

- Mini player: the song's cover shows next to its title — the picture inside the file, or the album picture in its folder (cover.jpg, folder.jpg and the like). Songs show their real title, artist and album instead of the file name.
- Mini player: repeat (off, all, one) and shuffle buttons around play. Shuffle plays every song once before any comes back; with repeat all, each new round is shuffled again. Both are remembered.
- Mini player: playlists. Type a name under Playlists and press Enter to save the queue; click a playlist to play it, ＋ to add it after the queue. Playlists made by other players (.m3u, .m3u8) open from the search (Ctrl+K) or from Explorer with Bloom. A playlist that names a program or a web address plays only its songs.

### 한국어

- 미니 플레이어: 곡 제목 옆에 표지가 나옵니다. 파일에 든 그림, 없으면 같은 폴더의 앨범 그림(cover.jpg, folder.jpg 같은 이름)을 씁니다. 파일 이름 대신 실제 제목·아티스트·앨범이 나옵니다.
- 미니 플레이어: 재생 버튼 양옆에 반복(끔·전체·한 곡)과 무작위 버튼이 생겼습니다. 무작위는 모든 곡을 한 번씩 튼 다음에 다시 돌고, 전체 반복과 같이 켜면 한 바퀴마다 새로 섞습니다. 두 설정은 다음에 켜도 그대로입니다.
- 미니 플레이어: 재생목록. 재생목록 탭에 이름을 넣고 Enter를 누르면 지금 대기열을 저장합니다. 목록을 누르면 재생하고, ＋는 대기열 뒤에 붙입니다. 다른 플레이어가 만든 재생목록(.m3u, .m3u8)도 검색(Ctrl+K)이나 탐색기에서 Bloom으로 열 수 있습니다. 재생목록에 프로그램이나 웹 주소가 들어 있으면 노래만 재생합니다.

## 0.16.1

- Fixed: a song opened with Bloom from Explorer (Open with › Bloom) now plays in the mini player. Before, Bloom tried to show the song as a text document, stopped responding for minutes and said "The running Bloom didn't answer". Open several songs at once and the first plays while the rest join the queue.
- Fixed: other files that aren't text (videos, pictures, programs) opened with Bloom from Explorer no longer freeze it; Bloom says it can't open them.
- Fixed: a song whose file name has a percent code in it, like "%41", plays instead of being skipped as "The file cannot be opened".
- Fixed: a mini player or other tool window attached to the launcher, opened while the launcher is hidden, now comes with the launcher. Before, it stood alone and stayed on top, or, right after Bloom started, opened off the screen.

### 한국어

- 수정: 탐색기에서 노래 파일을 Bloom으로 열면(연결 프로그램 › Bloom) 이제 미니 플레이어에서 재생합니다. 전에는 노래를 텍스트 문서로 열려다 몇 분 동안 멈췄고 '실행 중인 Bloom이 응답하지 않았습니다'가 떴습니다. 여러 곡을 한꺼번에 열면 첫 곡을 재생하고 나머지는 대기열에 넣습니다.
- 수정: 탐색기에서 Bloom으로 연 다른 텍스트가 아닌 파일(동영상, 그림, 프로그램)에 Bloom이 멈추지 않습니다. 열 수 없다고 알립니다.
- 수정: 파일 이름에 '%41' 같은 퍼센트 코드가 든 노래를 '열 수 없는 파일이에요'로 건너뛰지 않고 재생합니다.
- 수정: 런처에 붙여 둔 미니 플레이어나 도구 창을 런처가 숨은 상태에서 열면 이제 런처와 함께 나타납니다. 전에는 혼자 떠서 맨 위에 남았고, Bloom을 막 시작했을 때는 화면 밖에 열렸습니다.

## 0.16.0

- Your own accent colour: Settings › General › Colour has a "Custom" dot. Type a code like `#3A7BD5` and press Enter to use it. The window and tray icons and the launcher's logo now follow the accent colour; rose, the default, looks as before. Thanks to @horseman777777-sketch (#7).
- Search providers: a `providers.json` in Bloom's data folder ties a keyword to your own script, and the script's results show in the search line as you type (keyword + space + words). How to write one, with an example script: `docs/PROVIDERS.md`. Thanks to @horseman777777-sketch (#8).
- omp (oh-my-pi) as an AI provider: with omp installed, Bloom can chat with it, send it one-off asks, show how full the conversation is, and list its models in My models. Thanks to @horseman777777-sketch (#9).
- Mini player: Ctrl+K on an audio file in the search results › "Play in mini player". A small window beside the launcher shows the song and a queue. Bloom plays the file itself (flac, mp3, wav, m4a, aac, wma) or through JRiver, foobar2000 or MusicBee (Settings › Mini player). Thanks to @horseman777777-sketch (#10).
- Bloom Lens keeps one less copy of the whole screen in memory, so Bloom grows less after you use it.

### 한국어

- 직접 고른 강조색: 설정 › 일반 › 색에 '직접' 점이 생겼습니다. `#3A7BD5` 같은 색 코드를 적고 Enter를 누르면 그 색을 씁니다. 창·트레이 아이콘과 런처 로고도 강조색을 따릅니다. 기본인 장미는 예전 모습 그대로입니다. @horseman777777-sketch 님 감사합니다(#7).
- 검색 제공자: Bloom 데이터 폴더의 `providers.json`에 키워드와 내 스크립트를 적어 두면, 키워드 + 스페이스 + 검색어를 입력하는 동안 스크립트 결과가 검색줄에 나옵니다. 만드는 법과 예시 스크립트는 `docs/PROVIDERS.md`에 있습니다. @horseman777777-sketch 님 감사합니다(#8).
- omp(oh-my-pi)를 AI 제공자로 쓸 수 있습니다. omp가 설치돼 있으면 채팅, 한 번 묻기, 대화 분량 게이지, 내 모델 목록에서 씁니다. @horseman777777-sketch 님 감사합니다(#9).
- 미니 플레이어: 검색 결과의 오디오 파일에서 Ctrl+K › '미니 플레이어로 재생'. 런처 옆 작은 창에 지금 곡과 대기열이 나옵니다. Bloom이 직접 재생하거나(flac·mp3·wav·m4a·aac·wma) JRiver·foobar2000·MusicBee로 재생합니다(설정 › 미니 플레이어). @horseman777777-sketch 님 감사합니다(#10).
- Bloom Lens가 화면 전체 사본을 하나 덜 들고 있어서, 쓰고 난 뒤 Bloom의 메모리가 덜 늘어납니다.

## 0.15.2

- Markdown documents and notes no longer stall, even with many tables: Bloom now lays out and draws the text itself, only what is on the screen. On the test PC a 22 KB document with 24 tables opens in 0.05 s instead of 0.7 s, and scrolling, resizing the window and changing the text size no longer pause (the longest pause fell from 4.7 s to 0.05 s). Shift+arrow selection now carries on across paragraphs, and a shortened link copies as its whole address.
- Glass transparency is a slider (Settings › Performance › Display, 15–75 %); the windows change as it moves.
- The "update downloaded" card has "Restart to update", which puts the update in right away (#2).
- Win key over admin apps (Settings › General, shown on administrator accounts): with an app run as administrator in front, a lone Win press now opens Bloom instead of the Start menu. Turning it on asks for admin approval once and installs a small helper, "Bloom Win-key helper", in Program Files that watches only the Win key and only while Bloom runs. Turning it off or uninstalling Bloom removes it (with admin approval); Windows Settings › Apps can remove it too. Reported by @nuv47 (#3).
- Fixed: in 0.15.1, turning on Win key over admin apps said the helper couldn't be installed; Windows refused to start it before asking for approval.
- Security: in custom commands, what `{input}` and `{clipboard}` put into a script is always taken as text and never run as a command, whatever quotes the script puts around it; the winget "install in a terminal" line quotes the package's Id and source too. Thanks to @horseman777777-sketch (#5).
- Fixed: Bloom Lens and the color picker covered only part of a monitor whose scale differs from the main one (a 100 % monitor beside a 150 % one). Thanks to @horseman777777-sketch (#4).

### 한국어

- 표가 많은 마크다운 문서와 메모도 멈추지 않습니다. 이제 Bloom이 글자 배치를 직접 계산해 화면에 보이는 부분만 그립니다. 시험 PC에서 표 24개가 든 22KB 문서가 0.7초 대신 0.05초 만에 열리고, 스크롤·창 크기·글자 크기를 바꿀 때도 멈추지 않습니다(가장 긴 멈춤 4.7초 → 0.05초). Shift+화살표 선택이 문단을 넘어 이어지고, 줄여 표시한 링크는 전체 주소로 복사됩니다.
- 유리 투명도를 슬라이더로 정합니다(설정 › 성능 › 화면, 15~75%). 움직이는 동안 창에 바로 반영됩니다.
- '다운로드 완료' 업데이트 카드에 '다시 시작해서 업데이트' 버튼이 생겼습니다. 누르면 바로 업데이트를 적용합니다(#2).
- 관리자 앱 위에서도 Win 키 사용(설정 › 일반, 관리자 계정에서만 표시): 관리자 권한으로 실행한 앱이 앞에 있어도 Win 키 한 번으로 시작 메뉴 대신 Bloom이 열립니다. 켤 때 관리자 승인을 한 번 받고, Bloom이 실행 중일 때 Win 키만 살피는 작은 도우미 'Bloom Win-key helper'를 Program Files에 설치합니다. 끄거나 Bloom을 삭제하면 지우며(관리자 승인 필요), Windows 설정 › 앱에서도 지울 수 있습니다. @nuv47 님 제보(#3).
- 수정: 0.15.1에서 '관리자 앱 위에서도 Win 키 사용'을 켜면 '도우미를 설치하지 못했습니다'가 뜨던 문제를 고쳤습니다. Windows가 승인 창을 띄우기 전에 도우미 실행을 막고 있었습니다.
- 보안: 사용자 명령에서 `{input}`과 `{clipboard}`로 스크립트에 들어가는 값은 스크립트가 어떤 따옴표를 두르든 항상 글자로만 다루고 명령으로 실행하지 않습니다. winget '터미널에서 설치' 줄의 패키지 Id와 소스도 따옴표로 감쌉니다. @horseman777777-sketch 님 감사합니다(#5).
- 수정: 배율이 주 모니터와 다른 모니터에서 Bloom Lens와 색 고르기가 화면 일부만 덮던 문제(150% 옆의 100% 모니터 등)를 고쳤습니다. @horseman777777-sketch 님 감사합니다(#4).

## 0.15.1

- Markdown documents and notes no longer stall, even with many tables: Bloom now lays out and draws the text itself, only what is on the screen. On the test PC a 22 KB document with 24 tables opens in 0.05 s instead of 0.7 s, and scrolling, resizing the window and changing the text size no longer pause (the longest pause fell from 4.7 s to 0.05 s). Shift+arrow selection now carries on across paragraphs, and a shortened link copies as its whole address.
- Glass transparency is a slider (Settings › Performance › Display, 15–75 %); the windows change as it moves.
- The "update downloaded" card has "Restart to update", which puts the update in right away (#2).
- Win key over admin apps (Settings › General, shown on administrator accounts): with an app run as administrator in front, a lone Win press now opens Bloom instead of the Start menu. Turning it on asks for admin approval once and installs a small helper, "Bloom Win-key helper", in Program Files that watches only the Win key and only while Bloom runs. Turning it off or uninstalling Bloom removes it (with admin approval); Windows Settings › Apps can remove it too. Reported by @nuv47 (#3).
- Security: in custom commands, what `{input}` and `{clipboard}` put into a script is always taken as text and never run as a command, whatever quotes the script puts around it; the winget "install in a terminal" line quotes the package's Id and source too. Thanks to @horseman777777-sketch (#5).
- Fixed: Bloom Lens and the color picker covered only part of a monitor whose scale differs from the main one (a 100 % monitor beside a 150 % one). Thanks to @horseman777777-sketch (#4).

### 한국어

- 표가 많은 마크다운 문서와 메모도 멈추지 않습니다. 이제 Bloom이 글자 배치를 직접 계산해 화면에 보이는 부분만 그립니다. 시험 PC에서 표 24개가 든 22KB 문서가 0.7초 대신 0.05초 만에 열리고, 스크롤·창 크기·글자 크기를 바꿀 때도 멈추지 않습니다(가장 긴 멈춤 4.7초 → 0.05초). Shift+화살표 선택이 문단을 넘어 이어지고, 줄여 표시한 링크는 전체 주소로 복사됩니다.
- 유리 투명도를 슬라이더로 정합니다(설정 › 성능 › 화면, 15~75%). 움직이는 동안 창에 바로 반영됩니다.
- '다운로드 완료' 업데이트 카드에 '다시 시작해서 업데이트' 버튼이 생겼습니다. 누르면 바로 업데이트를 적용합니다(#2).
- 관리자 앱 위에서도 Win 키 사용(설정 › 일반, 관리자 계정에서만 표시): 관리자 권한으로 실행한 앱이 앞에 있어도 Win 키 한 번으로 시작 메뉴 대신 Bloom이 열립니다. 켤 때 관리자 승인을 한 번 받고, Bloom이 실행 중일 때 Win 키만 살피는 작은 도우미 'Bloom Win-key helper'를 Program Files에 설치합니다. 끄거나 Bloom을 삭제하면 지우며(관리자 승인 필요), Windows 설정 › 앱에서도 지울 수 있습니다. @nuv47 님 제보(#3).
- 보안: 사용자 명령에서 `{input}`과 `{clipboard}`로 스크립트에 들어가는 값은 스크립트가 어떤 따옴표를 두르든 항상 글자로만 다루고 명령으로 실행하지 않습니다. winget '터미널에서 설치' 줄의 패키지 Id와 소스도 따옴표로 감쌉니다. @horseman777777-sketch 님 감사합니다(#5).
- 수정: 배율이 주 모니터와 다른 모니터에서 Bloom Lens와 색 고르기가 화면 일부만 덮던 문제(150% 옆의 100% 모니터 등)를 고쳤습니다. @horseman777777-sketch 님 감사합니다(#4).

## 0.15.0

- Markdown documents (.md) open in Bloom's own document window: it shows in the taskbar like other apps and snaps like other windows. Open one with "View in Bloom" on a .md file you find or with Explorer's "Open with"; in the setup's install, Settings › Features › "Make it the default app" lets a double-click in Explorer open them in Bloom. Settings › Features › Markdown documents turns it off.
- The document window switches between view (the Markdown drawn) and edit (the text). It saves as you type and keeps the file's encoding and line endings. When another program changes the file, it asks whether to reload it or save yours. Edits not in the file yet are asked about before Bloom quits, restarts or updates; if Bloom is ended before it can ask, they are kept beside the file as "<name> (Bloom recovery <date> <time>).md".
- Long documents open quickly: only the paragraphs on the screen are drawn (on the test PC a long document opened in 1.4 s instead of 6.5 s, and resizing the window took under 0.1 s instead of up to 20 s). Drag to select across paragraphs (it scrolls at the window's edge), Ctrl+A selects all and Ctrl+C copies; in a wide window the text keeps a comfortable reading width. Files over 10 MB show their start only, as plain text.
- Documents read better: headings sized by level with space around them, lines wrapped in the file read as one paragraph, tables sized to their content with a tinted header row, and long code wraps after `.` and `/`. Tables and code in AI chat answers and notes are drawn the same way.
- Notes in Markdown: a note switches between view and edit, checklist boxes can be ticked in the view, a note's `# heading` names it in the list, what the AI adds shows at once, and Ctrl+wheel sets the text size (10–28, Ctrl+0 back to 14).
- Links in notes and documents open only web pages (http, https) and e-mail (mailto); a link to a file on this PC shows it in its folder. Pictures in network folders are not loaded, so Windows doesn't hand your sign-in to them, unless the document is in that network folder itself.
- AI accounts: an added account can be signed out with its × or "Sign out" in its menu, after a confirmation. Bloom runs the CLI's own sign-out (for ChatGPT, after stopping Bloom's Codex for it), then moves the account's folder to the Recycle Bin; AI chat goes back to the default account.
- Glass transparency: Settings › Performance › Display sets how much of the screen behind shows through the glass (15–75 %, 30 % by default).
- Fixed: a black console window could pop up for a moment when Bloom stalled, for example while opening a long .md file. Bloom no longer writes diagnostic files for stalls; its log still notes each one in a line, and the old files in `%LOCALAPPDATA%\Bloom\stalls` are deleted.

### 한국어

- 마크다운 문서(.md)를 Bloom 전용 문서 창에서 엽니다. 다른 앱처럼 작업 표시줄에 표시되고, 다른 창처럼 화면 가장자리에 붙일 수 있습니다. 검색한 .md 파일의 'Bloom으로 보기'나 탐색기의 '연결 프로그램'으로 엽니다. 설치판에서는 설정 › 기능 › '기본 앱으로 정하기'로 탐색기에서 두 번 클릭할 때 Bloom으로 열리게 할 수 있습니다. 설정 › 기능 › 마크다운 문서에서 끌 수 있습니다.
- 문서 창은 보기(마크다운을 그린 화면)와 편집(원문)을 오갑니다. 입력하는 대로 저장하고, 파일의 인코딩과 줄바꿈 방식은 그대로 둡니다. 다른 프로그램이 파일을 바꾸면 다시 읽을지, 고친 내용을 저장할지 묻습니다. 아직 파일에 저장되지 않은 내용은 Bloom을 끄거나 다시 시작하거나 업데이트하기 전에 묻습니다. 물을 틈 없이 Bloom이 종료되면 파일 옆에 '<이름> (Bloom 복구 <날짜> <시각>).md' 사본으로 남깁니다.
- 긴 문서도 빨리 열립니다. 화면에 보이는 문단만 그립니다(시험 PC에서 긴 문서 열기 6.5초 → 1.4초, 창 크기 바꾸기 최대 20초 → 0.1초 미만). 여러 문단에 걸쳐 드래그해 선택하고(창 끝에서는 자동으로 스크롤), Ctrl+A로 전체를 선택하고, Ctrl+C로 복사합니다. 넓은 창에서도 글은 읽기 좋은 폭을 유지합니다. 10MB가 넘는 파일은 앞부분만 텍스트로 표시합니다.
- 문서가 더 읽기 좋아졌습니다. 제목은 단계별 크기와 여백으로 나뉘고, 파일 안에서 줄을 바꾼 문장은 한 문단으로 이어집니다. 표는 내용에 맞춘 칸 폭과 색을 입힌 머리줄로, 긴 코드는 `.`과 `/` 뒤에서 줄을 바꿉니다. AI 채팅 답변과 메모의 표·코드도 같은 방식으로 그립니다.
- 메모도 마크다운: 보기와 편집을 오가고, 보기 화면에서 체크리스트를 바로 체크하며, `# 제목`이 목록에 메모 이름으로 표시됩니다. AI가 덧붙인 내용도 바로 반영되고, Ctrl+휠로 글자 크기를 바꿉니다(10~28, Ctrl+0은 14).
- 메모와 문서의 링크는 웹 페이지(http, https)와 메일(mailto)만 열고, PC 안 파일 링크는 폴더에서 해당 파일을 표시합니다. 네트워크 폴더의 그림은 Windows 로그인 정보가 그쪽으로 전달되지 않도록 불러오지 않습니다. 문서가 그 네트워크 폴더 안에 있을 때만 불러옵니다.
- AI 계정: 추가한 계정은 ×나 메뉴의 '로그아웃'으로, 확인을 거쳐 로그아웃할 수 있습니다. CLI의 공식 로그아웃을 실행한 뒤(ChatGPT는 그 계정의 Bloom Codex를 먼저 멈춤) 계정 폴더를 휴지통으로 옮기고, AI 채팅은 기본 계정으로 돌아갑니다.
- 유리 투명도: 설정 › 성능 › 화면에서 유리 너머로 뒤 화면이 비치는 정도를 고릅니다(15~75%, 기본 30%).
- 수정: Bloom이 잠깐 멈출 때(예: 긴 .md 파일을 열 때) 검은 콘솔 창이 잠깐 뜨던 문제를 고쳤습니다. 멈춤 진단 파일은 더 이상 만들지 않고 로그에 한 줄로만 남기며, `%LOCALAPPDATA%\Bloom\stalls`의 예전 파일은 지웁니다.

## 0.14.1

- Color palette from a capture: Bloom Lens › 7 (or the palette button on the capture drawing bar) picks the main colors of the area, 5 by default (− + for 3–10). Each swatch shows HEX, RGB and its share; click to copy the HEX. "Copy all" gives a HEX list, CSS variables or a palette image, and a picked color shows gradient suggestions. Worked out on this PC, no AI usage; palettes are kept with the recent colors.
- AI chat answers start sooner: when a chat is open or you start typing, Bloom starts the AI for the model that chat uses (Claude or ChatGPT, never both) so your first message doesn't wait for it. It ends after a few minutes unused or when the chat closes.
- ChatGPT chats (the chat window and the launcher's quick chat) now show the answer as it is written instead of all at once at the end, and keep one Codex process per chat instead of one per message. Bloom's Codex runs with its own settings, so your own Codex config.toml doesn't change Bloom's chats; your Codex sign-in and earlier chats carry over. The first ChatGPT chat after updating can take longer to start while Codex indexes your history once.
- Fixed: a big capture could pause the launcher while making a palette; copies that fail (another app holding the clipboard) now say so.

### 한국어

- 캡처로 컬러 팔레트 만들기: Bloom Lens › 7(또는 캡처 그리기 도구줄의 팔레트 버튼)을 누르면 영역의 대표 색을 뽑습니다. 기본 5색이고 − +로 3~10색까지 바꿀 수 있습니다. 색마다 HEX, RGB, 차지 비율이 표시되고, 누르면 HEX가 복사됩니다. '전체 복사'는 HEX 목록 · CSS 변수 · 팔레트 이미지 중에서 고르고, 고른 색의 그라데이션 추천도 나옵니다. 이 PC 안에서 계산하며 AI 사용량은 없고, 팔레트는 최근 색과 함께 기록됩니다.
- AI 채팅 첫 답이 더 빨라졌습니다: 채팅을 열거나 입력을 시작하면 그 대화가 쓰는 모델(Claude 또는 ChatGPT, 둘 중 하나만)을 미리 켜 둡니다. 몇 분 동안 쓰지 않거나 대화를 닫으면 꺼집니다.
- ChatGPT 채팅(채팅 창, 런처 빠른 채팅)은 답이 다 만들어진 뒤 한꺼번에 뜨지 않고 쓰이는 대로 바로 표시됩니다. 메시지마다 Codex를 새로 켜지 않고 대화마다 하나를 계속 씁니다. Bloom의 Codex는 Bloom 전용 설정으로 실행되어 내 Codex config.toml이 Bloom 채팅에 영향을 주지 않으며, Codex 로그인과 이전 대화는 그대로 이어집니다. 업데이트 후 첫 ChatGPT 채팅은 Codex가 기록을 한 번 정리하느라 시작이 조금 오래 걸릴 수 있습니다.
- 수정: 큰 화면을 팔레트로 만들 때 런처가 잠깐 멈추던 문제, 다른 앱이 클립보드를 쓰고 있어 복사에 실패해도 완료로 표시되던 문제를 고쳤습니다.

## 0.14.0

- The launcher resizes from its edges and corners. A bigger launcher shows more, not bigger: text and icons keep their size. The size is kept, fitted to the screen on open, and Settings › General resets it.
- Home editing (Ctrl+E or "Edit home"): drag a block by its title bar to move it, and a gap shows where it will land. Each list block has its own row count (− +), and clicking a title folds the block. A wide launcher lays the home out in two columns, and ‹ › moves a block to the other column.
- "+ Add block" at the end of each column lists the hidden blocks as cards. Click one to add it at the end of that column, or drag it to where it should go.
- Claude Code and Codex task cards (working, done, needs approval) now take you to the session: a click brings the terminal or editor window the task runs in to the front.
- Type `?` alone in the search line to see what Bloom understands, with examples by kind; Enter puts one in the search line. The empty search line shows one example at a time.
- Bloom's own settings show up in search: type a setting's name (glass, shortcut) and Enter opens it in the settings window.
- Settings › About › Problem report copies the version, Windows and the recent log (user name and e-mail addresses hidden) and opens a GitHub issue page. Bloom sends nothing itself.
- The Ctrl+K menu is shorter. The icon choices are one "Change icon…" line (AI drawing and the original icon are inside it); Move left/right, Customize the home and Copy name are gone (Ctrl+Shift+←/→, dragging and Ctrl+E still do those).
- Fixed: a long Ctrl+K menu could run past the bottom of the launcher and get cut off. It now fits inside and scrolls.
- Fixed: typing in the search line could freeze Bloom for a moment, sometimes for seconds, while it counted what was in the Recycle Bin for the "Empty recycle bin" row. The row no longer shows the count, and emptying runs in the background.
- AI with your own API key: add a key for OpenAI, Anthropic, Gemini, OpenRouter, DeepSeek, xAI or OpenCode Go in Settings › AI accounts › API keys, then pick its models in AI chat, the launcher's quick chat, Lens and translation. Keys are encrypted by Windows, stay on this PC and go only to their own provider.
- API models can use tools: reading and writing files, PowerShell commands, Bloom's PC actions and your other MCP servers. Writes, commands and anything outside the chat's folder ask first on a card that shows the whole command or content.
- Settings › PC actions · MCP › Permissions: for each kind of action (reading outside the chat's folder, writing, editing, commands, PC actions, other MCP tools) choose Ask every time, Always allow or Off. Imported settings never change these.
- OpenRouter and DeepSeek keys get a balance card on the home, and every API answer shows how many tokens it used.
- File searches that use Everything's functions (anything with a colon, such as ext: or content:) now ask first in every chat, because some of them read inside files.
- Captures (the capture shortcut, Capture in the Lens menu) are also saved in your Screenshots folder, where Windows' Snipping Tool saves them, as "Bloom <date> <time>.png". Bloom never deletes anything there.
- Bloom draws with the CPU by default, and GPU drawing is marked experimental: on some PCs it made the screen stutter or freeze for seconds.
- Approve on the card (Settings › Notifications, off by default): when Claude Code asks for permission, its card offers Allow, Decline and Answer in terminal. Turning it on adds Bloom's hook to Claude Code's settings.json; turning it off takes it out. While the session's window is in front, the terminal asks as before.
- When Bloom stops responding for half a second or more, its log says so. A freeze past 0.8 s also leaves a small diagnostic file (`%LOCALAPPDATA%\Bloom\stalls`, the newest five, never sent) that shows where Bloom was stuck.

### 한국어

- 런처 테두리와 모서리를 끌어 크기를 바꿀 수 있습니다. 창을 키우면 글자와 아이콘은 그대로이고 보이는 내용이 늘어납니다. 크기는 기억했다가 열 때 화면에 맞추고, 설정 › 일반에서 초기화할 수 있습니다.
- 홈 편집(Ctrl+E 또는 '홈 편집'): 블록 제목 줄을 끌어 옮기면 놓일 자리에 빈칸이 표시됩니다. 목록 블록은 − +로 보일 개수를 정하고, 제목을 누르면 접힙니다. 런처가 넓으면 홈이 두 열로 나뉘고, ‹ ›로 블록을 다른 열로 옮깁니다.
- 각 열 끝의 '+ 블록 추가'를 누르면 숨긴 블록이 카드로 표시됩니다. 카드를 누르면 그 열 끝에 추가되고, 끌면 원하는 자리에 놓입니다.
- Claude Code · Codex 작업 알림 카드(진행 중 · 작업 완료 · 승인 대기)를 누르면 그 작업이 실행 중인 터미널이나 편집기 창으로 이동합니다.
- 검색창에 `?`만 입력하면 쓸 수 있는 표현을 종류별 예시로 표시합니다. Enter를 누르면 예시가 검색창에 들어갑니다. 빈 검색창에도 예시가 하나씩 표시됩니다.
- Bloom 설정을 검색할 수 있습니다. `유리 효과`, `단축키`처럼 설정 이름을 입력하고 Enter를 누르면 설정 창에서 열립니다.
- 설정 › 정보 › 문제 보고: 버전, Windows, 최근 로그(사용자 이름과 이메일 주소는 가림)를 복사하고 GitHub 이슈 작성 페이지를 엽니다. Bloom이 직접 보내는 것은 없습니다.
- Ctrl+K 메뉴를 줄였습니다. 아이콘 항목은 '아이콘 바꾸기…' 하나로 합쳤고(AI로 그리기와 원래 아이콘은 그 안에 있습니다), 앞으로 · 뒤로 옮기기, 홈 화면 꾸미기, 이름 복사는 뺐습니다(Ctrl+Shift+←/→, 끌어서 옮기기, Ctrl+E는 그대로입니다).
- 수정: Ctrl+K 메뉴가 길면 런처 아래로 넘쳐 끝이 잘리던 문제를 고쳤습니다. 이제 런처 안에 맞춰지고 메뉴 안에서 스크롤됩니다.
- 수정: 검색창에 입력할 때 '휴지통 비우기' 줄에 표시할 항목 수를 세느라 Bloom이 잠깐, 때로는 몇 초 동안 멈추던 문제를 고쳤습니다. 이제 이 줄에는 개수를 표시하지 않고, 휴지통 비우기는 백그라운드에서 처리합니다.
- 내 API 키로 AI 사용: 설정 › AI 계정 › API 키에 OpenAI, Anthropic, Gemini, OpenRouter, DeepSeek, xAI, OpenCode Go 키를 넣으면 AI 채팅, 런처 빠른 채팅, 렌즈, 번역에서 해당 모델을 고를 수 있습니다. 키는 Windows가 암호화해 이 PC에만 저장하고, 해당 서비스로만 보냅니다.
- API 모델도 도구를 씁니다. 파일 읽기·쓰기, PowerShell 명령, Bloom의 PC 작업, 다른 MCP 서버를 쓸 수 있고, 쓰기·명령·대화 폴더 밖의 작업은 명령이나 내용 전체를 보여주는 카드로 먼저 묻습니다.
- 설정 › PC 작업 · MCP › 권한: 작업 종류(대화 폴더 밖 읽기, 쓰기, 고치기, 명령, PC 작업, 다른 MCP 도구)마다 매번 묻기 · 항상 허용 · 사용 안 함을 고릅니다. 설정을 가져와도 이 값은 바뀌지 않습니다.
- OpenRouter·DeepSeek 키는 홈에 잔액 카드가 표시되고, API 답변마다 사용한 토큰 수가 표시됩니다.
- Everything 기능을 쓰는 파일 검색(ext:, content:처럼 콜론이 들어간 검색)은 일부가 파일 내용을 읽기 때문에, 이제 모든 대화에서 먼저 묻습니다.
- 캡처(캡처 단축키, 렌즈 메뉴의 캡처)는 Windows 캡처 도구와 같은 스크린샷 폴더에도 'Bloom 날짜 시각.png'로 저장합니다. 이 폴더의 파일은 Bloom이 지우지 않습니다.
- 기본으로 CPU로 그리고, GPU로 그리기는 실험적 기능으로 표시합니다. 일부 PC에서 GPU로 그리면 화면이 끊기거나 몇 초 동안 멈췄습니다.
- 알림 카드에서 승인(설정 › 알림, 기본은 꺼짐): Claude Code가 승인을 요청하면 카드에 허용 · 거절 · 터미널에서 답하기가 표시됩니다. 켜면 Claude Code 설정(settings.json)에 Bloom 훅을 추가하고, 끄면 뺍니다. 세션 창을 보고 있을 때는 지금처럼 터미널에서 묻습니다.
- Bloom이 0.5초 이상 응답하지 않으면 로그에 기록합니다. 0.8초 넘게 멈추면 멈춘 위치를 담은 작은 진단 파일도 남깁니다(`%LOCALAPPDATA%\Bloom\stalls`, 최근 5개, 외부로 보내지 않음).

## 0.13.3

- Fixed: a favorites folder's tile on the home could keep its old small icons after one of its items got a new icon.
- Fixed: copying the same text again could leave "Pin to the top" (Ctrl+D) on the home's recent clip doing nothing.

### 한국어

- 수정: 즐겨찾기 폴더 안 항목의 아이콘을 바꿔도 홈의 폴더 타일에 예전 작은 아이콘이 남을 수 있던 문제를 고쳤습니다.
- 수정: 같은 글을 다시 복사하면 홈의 '최근 복사' 줄에서 '위에 고정'(Ctrl+D)이 먹지 않을 수 있던 문제를 고쳤습니다.

## 0.13.2

- The launcher opens in about a third of the time: from the key to the screen took about 100 ms and now 30–40 ms (measured). The home is kept between openings (blocks that would look the same stay as they were, a block that changed is drawn again), the launcher shows as soon as its first screen is drawn, and the rest of a long list comes right after.
- On a busy PC Bloom stalls less. While the launcher is open it runs ahead of other programs; file search (Everything), pictures copied to the clipboard and the memory clean-up no longer hold up the launcher or the Win key.
- Pictures in the clipboard history's folder that no entry uses any more (left by a crash) are removed at start.
- A slow opening of the launcher (over 150 ms) is written to the log with its steps.

### 한국어

- 런처가 약 3분의 1의 시간에 열립니다. 키를 누른 뒤 화면에 뜨기까지 약 100 ms에서 30–40 ms로 줄었습니다(실측). 홈을 열 때마다 새로 만들지 않고, 그대로인 블록은 남겨 두고 바뀐 블록만 다시 그립니다. 첫 화면을 다 그리면 바로 표시하고, 긴 목록의 나머지는 바로 뒤에 채웁니다.
- PC가 바쁠 때 Bloom이 덜 멈춥니다. 런처가 열려 있는 동안 다른 프로그램보다 먼저 실행되고, 파일 검색(Everything) · 클립보드에 복사한 그림 · 메모리 정리가 더 이상 런처와 Win 키를 붙잡지 않습니다.
- 클립보드 기록 폴더에서 어떤 항목도 쓰지 않는 그림(비정상 종료로 남은 것)을 시작할 때 지웁니다.
- 런처가 느리게 열리면(150 ms 초과) 단계별 시간을 로그에 남깁니다.

## 0.13.1

- Edit home (Ctrl+E) now reorders the AI usage cards (‹ › on each card) and lets each account show a name of your own instead of its e-mail (✎). A card with its own name shows it even when it is the only one of its kind; limit notices use the same name.
- ChatGPT Pro plans show their tier: Pro 100, Pro 200 or Pro 500.
- Fixed: Bloom could stop responding for a moment while an app started (Store apps take a few seconds to activate, and run as administrator waited for the UAC prompt). Apps, files and links now start in the background.

### 한국어

- 홈 편집(Ctrl+E)에서 AI 사용량 카드의 순서를 바꾸고(카드마다 ‹ ›), 계정마다 이메일 대신 원하는 이름을 붙일 수 있습니다(✎). 이름을 붙인 카드는 같은 종류 계정이 하나뿐이어도 그 이름을 표시하고, 한도 알림도 같은 이름을 씁니다.
- ChatGPT Pro 요금제를 Pro 100 · Pro 200 · Pro 500으로 구분해 표시합니다.
- 수정: 앱을 실행할 때 Bloom이 잠깐 응답하지 않던 문제를 고쳤습니다(Store 앱은 활성화에 몇 초가 걸리고, 관리자 권한 실행은 UAC 창을 기다렸습니다). 이제 앱 · 파일 · 링크를 백그라운드에서 실행합니다.

## 0.13.0

- Several Claude and ChatGPT accounts. Settings › AI accounts adds an account by running the CLI's own sign-in in a new terminal (you sign in in the browser); each account keeps its sign-in in a folder of its own. The home shows a card per account, up to four in a row; with two accounts of one kind each card shows its e-mail.
- AI chat, Bloom Lens and translation use the account you pick for each of Claude and ChatGPT. A chat started under another account continues as a new session with the conversation so far. Nothing switches accounts on its own, even at a limit.
- ChatGPT usage can be checked two ways, chosen in the first-run screens or in Settings › AI accounts: the official CLI (Codex runs briefly per account) or a lightweight mode (one HTTPS request with the Codex sign-in, no CLI; not an official API).
- Added accounts are checked every 30 minutes; the default ones as before. The home's ↻ checks all of them. Limit notices name the account.
- Fixed: on a fresh install the first usage check could fail because the Claude and Codex checks started at the same moment.

### 한국어

- Claude · ChatGPT 계정을 여러 개 쓸 수 있습니다. 설정 › AI 계정에서 계정을 추가하면 새 터미널에서 CLI의 공식 로그인이 실행되고, 브라우저에서 로그인합니다. 계정마다 로그인 정보가 따로 저장됩니다. 홈에는 계정마다 카드가 한 줄에 4개까지 표시되고, 같은 종류 계정이 2개 이상이면 카드에 이메일이 표시됩니다.
- AI 채팅 · Bloom Lens · 번역은 Claude와 ChatGPT 각각 직접 고른 계정을 씁니다. 다른 계정에서 시작한 대화는 지금까지의 대화를 넘겨 새 세션으로 이어집니다. 한도에 닿아도 계정은 자동으로 바뀌지 않습니다.
- ChatGPT 사용량 조회 방식을 처음 설정 화면이나 설정 › AI 계정에서 고릅니다. 공식 CLI(계정마다 Codex를 잠깐 실행) 또는 가벼운 방식(Codex 로그인으로 HTTPS 요청 1번, CLI 실행 없음, 공식 API 아님)입니다.
- 추가한 계정은 30분마다, 기본 계정은 이전처럼 조회합니다. 홈의 ↻는 모든 계정을 조회합니다. 한도 알림에 계정이 표시됩니다.
- 수정: 새로 설치한 뒤 첫 사용량 조회가 Claude와 Codex 조회가 동시에 시작되면서 실패할 수 있던 문제를 고쳤습니다.

## 0.12.3

- Fixed: opening the launcher could flash a light background for a moment, more often with Notes or Clipboard attached beside it. The launcher and its attached tools now stay hidden until their new picture is on screen, and appear together.
- The Bloom Lens translation window and tool windows opened on their own also appear only once drawn.

### 한국어

- 수정: 런처를 열 때 밝은 배경이 잠깐 깜빡이던 문제를 고쳤습니다. 메모나 클립보드를 옆에 붙여 쓸 때 더 자주 보였습니다. 이제 런처와 붙은 도구가 새 화면을 다 그린 뒤 함께 표시됩니다.
- Bloom Lens 번역 창과 따로 연 도구 창도 다 그린 뒤 표시됩니다.

## 0.12.2

- Updates could get stuck: an update downloaded earlier that could not be installed kept being offered (for example 0.11.4), and newer versions were never looked for. Bloom now checks GitHub even then and takes the newest version.
- An app opened from Bloom no longer keeps Bloom's install folder in use. Such an app (a VR streamer, for example) blocked every update until it was closed.

### 한국어

- 업데이트가 멈추던 문제를 고쳤습니다. 예전에 받았지만 설치하지 못한 업데이트(예: 0.11.4)만 계속 표시되고 새 버전은 확인하지 않았습니다. 이제 그런 경우에도 GitHub를 확인해 가장 새 버전을 받습니다.
- Bloom에서 실행한 앱이 Bloom 설치 폴더를 붙잡지 않습니다. 이런 앱(예: VR 스트리머)이 켜져 있으면 닫을 때까지 모든 업데이트가 실패했습니다.

## 0.12.1

- Screen capture now lets you mark up the area before it goes anywhere. After you drag over the area, a bar opens beside it: pen (P), highlighter (H), eraser (E), four colours (1–4), three line sizes, undo (Ctrl+Z) and redo (Ctrl+Y). Enter copies the picture with your marks; the bar can also pin it, ask AI about it or save it. Esc cancels.
- Bloom remembers the tool, colour and size you used last.
- Settings › Bloom Lens has a new Saved captures section: open the folder where Bloom keeps the last 30 captures on this PC, or delete them all. Pinned pictures stay on screen after deleting.
- Fixed: with two monitors, Bloom Lens could close the moment it opened (most often right after the launcher hid because you clicked another window), so its shortcut seemed to do nothing.

### 한국어

- 화면 캡처에서 영역을 고른 뒤 바로 표시할 수 있습니다. 영역 옆에 펜(P) · 형광펜(H) · 지우개(E) · 색 4가지(1~4) · 굵기 3단계 · 되돌리기(Ctrl+Z) · 다시 하기(Ctrl+Y) 막대가 표시됩니다. Enter를 누르면 표시한 내용까지 함께 복사됩니다. 막대에서 화면에 고정 · AI에게 묻기 · 그림 저장도 할 수 있고, Esc로 취소합니다.
- 마지막에 쓴 도구 · 색 · 굵기를 기억합니다.
- 설정 › Bloom Lens에 캡처 저장본 항목이 생겼습니다. 이 PC에 보관하는 최근 캡처 30장의 폴더를 열거나 모두 삭제할 수 있습니다. 삭제해도 화면에 고정한 그림은 그대로 표시됩니다.
- 수정: 모니터가 2개일 때 Bloom Lens가 열리자마자 닫혀 단축키가 반응하지 않는 것처럼 보이던 문제를 고쳤습니다. 다른 창을 눌러 런처가 숨겨진 직후에 주로 발생했습니다.

## 0.12.0

- Every screen's wording is rewritten to be short and factual: statuses and results are short labels ("Capture copied"), and an error says what happened and what to do next instead of showing the raw error text. Japanese and Chinese follow.
- Where a message used to send you to another screen, it now has the button: a failed text recognition opens the recognition language, an update notice opens the update settings, a waiting approval opens its chat, a finished quick AI answer opens it in the AI chat.
- Reset, uninstall and restart questions name their buttons by what they do (Reset · Export settings · Keep data · Delete data · Restart · Later) instead of Yes / No.
- 'My commands' is now 'Custom commands'; the old words still find it.
- A custom command's failure notice shows its exit code and last line, with a button to its actions (copy the last output, edit the command).
- The home search line reads "Search, or ask AI".
- The quick AI closes when you click elsewhere, like the other views. The Win key opens it again at the same chat.
- 📌 keeps the launcher above other windows. In the quick AI, 📌 takes the place of the settings button.

### 한국어

- 모든 화면의 문구를 짧고 사실 위주로 바꿨습니다. 상태와 결과는 짧은 이름(‘캡처 복사 완료’)으로 표시하고, 오류는 원본 오류 대신 무슨 일이 있었고 무엇을 하면 되는지 표시합니다. 일본어 · 중국어도 함께 바뀌었습니다.
- 다른 화면으로 가라고 안내하던 곳에 바로 가는 버튼이 생겼습니다. 글자 인식 실패는 인식 언어 설정, 업데이트 알림은 업데이트 설정, 승인 대기 알림은 해당 대화, 빠른 AI 답변 완료 알림은 AI 채팅을 엽니다.
- 초기화 · 삭제 · 다시 시작 확인 창의 버튼이 예 / 아니요 대신 하는 일을 이름으로 표시합니다(초기화 · 설정 내보내기 · 데이터 유지 · 데이터 삭제 · 다시 시작 · 나중에).
- ‘내 명령’의 이름이 ‘사용자 명령’으로 바뀌었습니다. 예전 이름으로도 검색할 수 있습니다.
- 사용자 명령이 실패하면 알림에 종료 코드와 마지막 출력 줄이 표시되고, 명령 동작(마지막 출력 복사, 명령 편집)으로 가는 버튼이 함께 표시됩니다.
- 홈 검색줄의 안내 문구가 ‘검색하거나 AI에게 묻기’로 바뀌었습니다.
- 빠른 AI도 다른 화면처럼 바깥을 누르면 닫힙니다. Win 키를 누르면 보던 대화로 다시 열립니다.
- 📌을 켜면 다른 창을 눌러도 런처가 맨 위에 유지됩니다. 빠른 AI에서는 설정 버튼 자리에 📌 버튼이 표시됩니다.

## 0.11.4

- A **Shortcuts** tool beside the launcher, like the notes and the timers: every shortcut you gave an app, folder, command or window layout, with its keys. Find one by name or keys; a click runs it. Open it from the tools menu (Ctrl+J).
- The tools button is back at the end of the search line, left of the settings. It hides in the quick AI.

### 한국어

- 메모 · 타이머처럼 런처 옆에 붙는 **단축키** 도구가 생겼어요. 앱 · 폴더 · 명령 · 창 배치에 지정한 단축키를 키와 함께 모아 보여 줘요. 이름이나 키로 찾을 수 있고, 누르면 바로 실행돼요. 도구 메뉴(Ctrl+J)에서 열어요.
- 검색줄 끝, 설정 왼쪽에 도구 버튼이 다시 생겼어요. 빠른 AI에서는 보이지 않아요.

## 0.11.3

- Uninstall and Reset check the folders before deleting them: only Bloom's own data folders (Bloom under %APPDATA% and %LOCALAPPDATA%) can go, never another folder by mistake.

### 한국어

- Bloom 삭제와 초기화가 지우기 전에 폴더를 한 번 더 확인해요. Bloom의 데이터 폴더(%APPDATA% · %LOCALAPPDATA% 안의 Bloom)만 지우고, 다른 폴더는 실수로라도 지우지 않아요.

## 0.11.2

- Uninstall Bloom from Bloom itself: Settings › About › Uninstall Bloom. It asks twice: are you sure, and should your chats, settings and favorites go too. A copy run from the portable zip stops starting with Windows and opens its folder for you to delete.
- Install from PowerShell in one line: `irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex`. It downloads the same setup from the latest release and runs it.

### 한국어

- Bloom 안에서 Bloom을 지울 수 있어요. 설정 › 정보 › Bloom 삭제를 누르면 정말 지울지, 대화 · 설정 · 즐겨찾기까지 지울지 두 번 물어봐요. 압축본으로 쓰던 Bloom은 Windows 시작 시 자동 실행을 풀고, 직접 지울 수 있게 폴더를 열어 줘요.
- PowerShell 한 줄로 설치할 수 있어요: `irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex`. 최신 릴리즈의 같은 설치 파일을 받아 실행해요.

## 0.11.0

- Bloom speaks Japanese and Simplified Chinese: 设置 › 常规 › 语言 / 設定 › 一般 › 言語, or Auto with a Japanese or Chinese Windows. Searches, times and units are still typed in English or Korean.
- Text now uses the Windows system font (Segoe UI, Malgun Gothic) instead of Bloom Sans, drawn on whole pixels: sharper on Full HD and other 100% screens. Bloom takes 6 MB less on disk.
- Drag the launcher by its search line, not only the bottom bar. Past the text the line drags; on the text it still places the caret and selects.
- A ⚙ at the end of the search line opens the settings.
- Notes, timers and the other tool windows can be dragged onto another monitor. They used to keep snapping back to the edge of the screen they were on.
- Shortcuts take any key: PrintScreen, Pause, F13–F24, the number pad, punctuation, media keys. Keys that type nothing work on their own; the rest need Ctrl, Alt, Shift or Win.
- The color picker always starts on the screen: the magnifier and the color code follow the mouse, and the color view opens after you click.
- Starting with Codex notices on reads far less: Bloom skips the logs of its own chats (half the start-up allocations).

### 한국어

- 일본어와 간체 중국어를 지원해요. 설정 › 일반 › 언어에서 고르거나, 자동이면 Windows가 일본어 · 중국어일 때 그 언어로 나와요. 검색어 · 시간 · 단위는 지금처럼 한국어나 영어로 적어요.
- 글꼴이 Bloom Sans에서 Windows 시스템 글꼴(맑은 고딕 · Segoe UI)로 바뀌었어요. 글자를 픽셀에 맞춰 그려서 FHD처럼 배율 100%인 화면에서도 또렷해요. 설치 크기도 6 MB 줄었어요.
- 아래쪽 바뿐 아니라 검색줄을 끌어서도 런처를 옮길 수 있어요. 글자가 없는 곳을 끌면 옮겨지고, 글자 위에서는 그대로 커서를 두거나 선택해요.
- 검색줄 끝의 ⚙로 설정을 바로 열어요.
- 메모 · 타이머 같은 도구 창을 다른 모니터로 옮길 수 있어요. 전에는 화면 가장자리에 계속 다시 붙었어요.
- 단축키에 어떤 키든 쓸 수 있어요. PrintScreen, Pause, F13~F24, 숫자 패드, 기호, 미디어 키까지요. 글자를 치지 않는 키는 혼자서도 되고, 나머지는 Ctrl · Alt · Shift · Win과 함께 눌러요.
- 컬러 피커는 항상 화면에서 시작해요. 마우스를 따라 돋보기와 색 코드가 보이고, 클릭하면 색 화면이 열려요.
- Codex 알림을 켜 둔 채 시작할 때 읽는 양이 크게 줄었어요. Bloom이 연 채팅의 기록은 건너뛰어요(시작할 때 쓰는 메모리 할당이 절반).

## 0.10.0

Everything since 0.8.3 in one place: what's new in this update first, then what came in 0.9.0 to 0.9.3.

#### New in this update

- Bloom Lens can capture. Pick an area and it goes to the clipboard as a picture, ready for Ctrl+V. It's `6` in the Lens menu, or search for "screen capture". You can give it its own shortcut in Settings › Bloom Lens (off at first).
- Uninstall apps from Bloom. Right-click an app (or Ctrl+K) › Uninstall…. Bloom asks once more, then opens that program's own uninstaller. Store apps are removed for your account.
- Set a shortcut by pressing it. Click a shortcut in Settings, or Ctrl+K › Set a shortcut, and press the keys you want. Backspace turns it off.
- The quick AI keeps going when you leave. Esc takes you home and the answer carries on. The search line shows it until you look, Tab takes you back, and a card tells you when it's done if Bloom is hidden. ■ Stop or Ctrl+. stops it.
- A tidier quick AI bar, so what you type has room. One chip for the model and thinking level, Stop and End session side by side, and a 📎 button for files. The pin and tools buttons left the bar; Ctrl+P and Ctrl+J still work.

#### Search and windows

- Type a path like `C:\Users`, `D:/work` or `%APPDATA%` and the folder comes first. Enter opens it, Tab goes into the folder you picked.
- Window layout: `left half`, `right third`, `top-left quarter`, `maximize`, `center` and `next screen` move the window in front.
- Dates and other cities' clocks: `in 100 days`, `days until christmas`, `days since 2026-03-01`, `new york time`, `tokyo 3pm`.
- Give anything its own shortcut: Ctrl+K › Set a shortcut on an app, folder, file, site, command or layout. They're listed in Settings › Tools.
- Bloom Lens can pin an area to the screen. It stays on top as a picture you can drag, size and copy.
- Install apps with winget: type `install vlc`. Bloom asks first, installs it in the background and tells you how it went.

#### AI chat

- Copy any part of an answer. Drag over an answer, your own message or a code block, then Ctrl+C or right-click › Copy.
- Chats name themselves per task, like `[◆ Opus • Code ] Fix the login`. You can switch it off in Settings › AI chat.
- Pick several chats and groups and delete them at once: the ✓ next to the chat search, Ctrl+click, or ⋯ › Select.
- A chat you leave alone closes its CLI after 10 minutes. Set your own time in Settings › AI chat; 0 keeps it open.
- Picking a chat up again after its CLI closed costs much less AI usage, because the AI service can reuse what it already cached.

#### Speed, memory and settings

- Draw with the GPU (smooth) or the CPU (about 30 MB less memory), at first start or in Settings › Performance.
- Bloom gives memory back after you close the chat or settings window.
- Long lists open faster, and the emoji view no longer pauses.
- Settings are grouped into Basics, Launcher, AI and Tools.
- After an update, Bloom shows what changed once. See it again in Settings › About.
- Each new feature can be switched off in Settings › Features.

### 한국어

0.8.3 이후 바뀐 점을 한곳에 모았어요. 이번 업데이트에서 새로 생긴 것부터, 그다음 0.9.0~0.9.3에서 생긴 것이에요.

#### 이번 업데이트에서 새로

- Bloom Lens로 캡처할 수 있어요. 영역을 고르면 그림으로 클립보드에 복사돼서 바로 Ctrl+V로 붙여 넣으면 돼요. Lens 메뉴의 `6`번이고, "화면 캡처"로 찾아도 돼요. 설정 › Bloom Lens에서 단축키를 따로 줄 수 있어요(처음엔 꺼져 있어요).
- Bloom에서 앱을 지울 수 있어요. 앱을 오른쪽 클릭(또는 Ctrl+K) › 제거…. 한 번 더 묻고 그 프로그램의 제거 창을 열어요. Store 앱은 내 계정에서 지워요.
- 단축키는 키를 눌러서 정해요. 설정의 단축키 칸이나 Ctrl+K › 단축키 지정에서 쓸 키를 그대로 누르면 돼요. Backspace는 끄기예요.
- 빠른 AI가 자리를 비워도 계속 일해요. Esc를 누르면 홈으로 가고 답은 계속 써요. 확인하기 전까지 검색창에 보이고, Tab으로 돌아가요. Bloom이 숨어 있으면 끝났을 때 카드로 알려 줘요. ■ 정지나 Ctrl+.로 멈춰요.
- 빠른 AI 위쪽 바를 정리해서 적는 글이 잘 보여요. 모델과 생각 수준은 칩 하나로, 정지와 세션 종료는 나란히, 파일은 📎 버튼으로. 고정 · 도구 버튼은 바에서 뺐어요. Ctrl+P · Ctrl+J는 그대로 돼요.

#### 검색 · 창

- `C:\Users`, `D:/작업`, `%APPDATA%`처럼 경로를 적으면 그 폴더가 맨 위에 떠요. Enter로 열고, Tab으로 고른 폴더에 들어가요.
- 창 배치: `왼쪽 절반`, `오른쪽 1/3`, `왼쪽 위 1/4`, `최대화`, `가운데로`, `다음 화면으로`로 앞에 있는 창을 옮겨요.
- 날짜와 다른 도시 시간: `100일 뒤`, `크리스마스까지`, `2026-03-01부터`, `뉴욕 시간`, `도쿄 오후 3시`.
- 무엇이든 단축키를 붙일 수 있어요. 앱 · 폴더 · 파일 · 사이트 · 명령 · 창 배치에서 Ctrl+K › 단축키 지정. 붙인 단축키는 설정 › 도구에 모여 있어요.
- Bloom Lens로 고른 영역을 화면에 고정할 수 있어요. 그림으로 맨 위에 떠 있고, 옮기고 크기를 바꾸고 복사할 수 있어요.
- winget으로 앱 설치: `설치 vlc`처럼 적으면 돼요. 먼저 묻고, 뒤에서 설치한 다음 결과를 알려 줘요.

#### AI 채팅

- AI 답을 원하는 부분만 복사할 수 있어요. 답 · 내가 보낸 말 · 코드 블록을 드래그하고 Ctrl+C나 오른쪽 클릭 › 복사.
- 대화 제목이 작업마다 `[◆ Opus • Code ] 로그인 수정`처럼 붙어요. 설정 › AI 채팅에서 끌 수 있어요.
- 대화와 그룹을 여러 개 골라 한꺼번에 지울 수 있어요. 대화 찾기 옆 ✓, Ctrl+클릭, ⋯ › 선택.
- 안 쓰는 대화의 CLI는 10분 뒤에 닫아요. 설정 › AI 채팅에서 시간을 직접 정할 수 있고, 0이면 닫지 않아요.
- CLI가 닫힌 대화를 다시 이어 갈 때 AI 사용량이 훨씬 덜 들어요. AI 쪽에 저장된 캐시를 그대로 다시 써요.

#### 속도 · 메모리 · 설정

- GPU로 그리기(부드럽게)와 CPU로 그리기(메모리 약 30 MB 덜 씀) 중에 고를 수 있어요. 처음 설정이나 설정 › 성능에서요.
- 채팅 창이나 설정 창을 닫으면 메모리를 돌려줘요.
- 긴 목록이 빨리 뜨고, 이모지 화면이 멈칫하지 않아요.
- 설정을 기본 · 런처 · AI · 도구로 묶었어요.
- 업데이트한 뒤 처음 켜면 바뀐 점을 한 번 보여 줘요. 설정 › 정보에서 다시 볼 수 있어요.
- 새 기능은 모두 설정 › 기능에서 끌 수 있어요.

## 0.8.3

- AI chat with ChatGPT (Codex): MCP servers you switched on no longer flash terminal windows while a chat runs.

### 한국어

- ChatGPT(Codex)로 AI 채팅할 때, 켜 둔 MCP 서버 때문에 터미널 창이 잔뜩 떴다 사라지던 문제를 고쳤어요.

## 0.8.2

- Clipboard history: set how long entries stay in minutes (Settings › Clipboard › Keep for). 0 keeps them. Your old setting carries over, so a day becomes 1440 minutes.
- Short times now hold even when you copy nothing new. Entries go when they come due.

### 한국어

- 클립보드 기록 보관 기간을 분 단위로 직접 적을 수 있어요(설정 › 클립보드 › 보관 기간). 0이면 계속 남겨요. 예전 설정은 그대로 옮겨져서, 하루였다면 1440분이 돼요.
- 짧게 잡아 두면 새로 복사하지 않아도 시간이 되는 대로 지워져요.

## 0.8.1

The first public release.

Bloom is a launcher for Windows. Press Win, type a line, and it opens apps, does sums, finds files, sets reminders and asks the AI you already use.

What's in it
- One search line for apps, files, maths, units and currencies, emoji, reminders and web searches
- AI chat through your own Claude Code or Codex login, no API key
- The AI can move and rename files, write notes and add timers or events, and it asks before each one
- Bloom Lens: pick part of the screen to ask about it, translate it or copy its text
- Notes, timers, a calculator and clipboard history that sit beside the launcher
- Space Cleaner shows where your disk space went
- Bloom updates itself from this page

Install
- Download `BloomLauncher-win-Setup.exe` below and run it. Windows 10 or 11, 64-bit.
- Bloom isn't code-signed yet, so Windows may show "Windows protected your PC". Click More info, then Run anyway.

### 한국어

처음으로 공개하는 버전이에요.

Bloom은 Windows용 런처예요. Win 키를 누르고 한 줄 치면 앱 실행, 계산, 파일 찾기, 알림, AI 질문까지 돼요.

들어 있는 것
- 검색창 하나로 앱, 파일, 계산, 단위·환율, 이모지, 알림, 웹 검색
- 쓰던 Claude Code·Codex 로그인으로 AI 채팅 (API 키 필요 없음)
- AI에게 파일 옮기기·이름 바꾸기, 메모, 타이머·일정 추가를 맡길 수 있고, 할 때마다 먼저 물어봐요
- Bloom Lens: 화면 일부를 골라 묻거나, 번역하거나, 글자를 복사해요
- 런처 옆에 붙여 쓰는 메모, 타이머, 계산기, 클립보드 기록
- Space Cleaner로 디스크 용량이 어디에 쓰였는지 볼 수 있어요
- 이 페이지에서 알아서 업데이트돼요

설치
- 아래 `BloomLauncher-win-Setup.exe`를 받아서 실행하세요. Windows 10, 11 (64비트).
- 아직 코드 서명이 없어서 "Windows의 PC 보호" 창이 뜰 수 있어요. 추가 정보 → 실행을 누르면 돼요.
