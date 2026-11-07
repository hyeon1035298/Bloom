<p align="center"><img src="promo/bloom-github-ko.png" alt="Bloom 마크와 '키 하나, 검색줄 한 줄.'이라는 제목, AI에게 묻는 런처, 뒤에 떠 있는 홈 화면 조각들"></p>

<p align="center">Windows용 런처입니다. 검색줄에 원하는 것을 적고 Enter로 바로 열거나, Tab을 눌러 AI에게 물어보세요.</p>

<p align="center">
  <a href="#다운로드">다운로드</a> | <a href="#기능">기능</a> | <a href="#애드온">애드온</a> | <a href="#단축키">단축키</a> | <a href="#ai-기능에-대해">AI와 개인정보</a> | <a href="README.md">English</a> | <a href="README.ja.md">日本語</a> | <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <img src="docs/media/ko/loop.webp" alt="Bloom 데모. AI 채팅, 검색줄 하나로, 홈, Bloom Lens, 옆에 붙는 도구">
</p>

<p align="center"><sub>네이티브 WPF (.NET 10), WebView 없음, 한국어와 English, 日本語, 简体中文, MIT</sub></p>

## Bloom을 쓰는 이유

- Win 키 하나만 누르면 열리는 검색줄에서 앱 실행, 계산, 파일 찾기를 모두 해결합니다.
- 이 PC에 이미 로그인해 둔 Claude Code나 Codex, 또는 직접 등록한 API 키를 연결해 AI 기능을 바로 씁니다.
- 쓰지 않는 기능을 끄면 메뉴와 단축키에서 깔끔하게 사라지고 메모리도 차지하지 않습니다.

## 다운로드

**[Windows용 Bloom 받기](https://github.com/hyeon1035298/Bloom/releases/latest)** 최신 릴리스에서 `BloomLauncher-win-Setup.exe`를 받아 실행하세요.

아래 PowerShell 명령으로도 똑같이 받아 설치할 수 있습니다.

```powershell
irm https://raw.githubusercontent.com/hyeon1035298/Bloom/main/install.ps1 | iex
```

- Windows 10, 11 (64비트)에서 돌아가며, PC에 .NET 10 Desktop Runtime이 없으면 설치할 때 알아서 함께 받습니다.
- `%LOCALAPPDATA%\BloomLauncher` 폴더에 설치되고 시작 메뉴에 바로 가기가 만들어집니다.
- 아직 코드 서명 전이라 설치할 때 "Windows의 PC 보호" 창이 뜨면 추가 정보 › 실행을 누르면 됩니다.
- 앱을 켠 뒤 새 버전이 있는지 한 번 확인하고, 다음번에 켤 때 자동으로 업데이트합니다. 원하지 않으면 설정 › 정보에서 끌 수 있습니다.
- 앱을 지울 때는 설정 › 데이터 › Bloom 삭제에서 제거하며, 그동안 쓰던 데이터를 함께 지울지 고를 수 있습니다.
- AI 기능을 쓰려면 Claude Code, Codex, omp 로그인이나 API 키가 필요합니다. 파일 검색 프로그램인 [Everything](https://www.voidtools.com/)이 깔려 있으면 PC 전체에서 파일을 순식간에 찾습니다.

## 기능

### 검색줄 하나로

<img src="docs/media/ko/search.webp" alt="초성 ㅁㅁㅈ로 메모장을 찾은 런처와 창 배치 목록, 그리고 초성, 계산과 단위, 알림, 이모지, 경로, 창 배치, 날짜, 앱 설치 카드">

- 설치된 앱과 Windows 설정, Bloom 명령을 빠르게 찾습니다. 초성(`ㅁㅁㅈ`)이나 영어 첫 글자(`gc`)만 적어도 되고, 한/영 키를 안 바꾸고 그냥 친 글자(`초개ㅡㄷ`)도 알아듣습니다.
- `45000*1.1`, `84m2 to 평`, `100 usd`처럼 계산식이나 단위, 환율을 적으면 바로 계산해 주고, Enter를 누르면 결과를 클립보드에 복사합니다.
- `10분 타이머`, `30분 뒤 빨래`처럼 적으면 타이머와 알림을 맞춰 줍니다.
- `:하트`처럼 적어 이모지를 찾고, `yt 고양이`처럼 앞글자를 붙여 원하는 사이트에서 바로 검색합니다.
- Everything이 깔려 있으면 PC 안의 모든 파일을 이름으로 바로 찾아 줍니다.
- 검색 결과에서 Ctrl+K를 누르면 그 항목으로 할 수 있는 작업 목록이 열리고, 빈 검색줄에 `?`를 적으면 쓸 수 있는 입력 예시를 한눈에 보여 줍니다.
- `C:\Users`처럼 폴더 경로를 적으면 탐색기를 따로 열지 않고도 그 안의 파일과 폴더를 바로 둘러봅니다.
- `왼쪽 절반`, `최대화`, `다음 화면으로`처럼 적어 지금 보고 있는 창을 화면 절반에 맞추거나 다른 모니터로 옮깁니다.
- `100일 뒤`, `도쿄 오후 3시`처럼 적어 날짜를 계산하고 다른 나라 시간을 확인합니다.
- `설치 vlc`라고 적으면 winget과 Microsoft Store에서 앱을 찾아 주며, 확인을 거친 뒤 바로 설치합니다.
- Ctrl+K › 단축키 지정으로 자주 쓰는 앱, 폴더, 웹사이트, 창 배치 동작에 나만의 전역 단축키를 붙일 수 있습니다.

### AI 채팅

<img src="docs/media/ko/link.webp" alt="Claude Code, Codex, omp, API 키가 Bloom 하나로 이어지고, PC 작업, 메모와 타이머와 캘린더, AI 빠른 동작, 작업 알림으로 나뉘는 그림">

<img src="docs/media/ko/ai.webp" alt="파일을 옮기기 전에 허용과 거절을 묻는 채팅 카드와, Claude Code와 Codex의 작업 알림 카드들">

- 검색줄에 궁금한 것을 적고 Tab을 누르면 빠른 AI가 그 자리에서 바로 답합니다. Shift+Tab을 누르면 이전 맥락 없이 새 대화로 묻습니다.
- 대화를 길게 이어 가거나 파일과 사진을 첨부할 때는 따로 열리는 AI 채팅 창(Ctrl+Alt+C)을 씁니다.
- 파일 정리처럼 PC를 다루는 작업도 맡길 수 있습니다. 파일을 옮기거나 무언가를 바꾸는 작업은 실행하기 전에 허용 카드를 띄워 먼저 물어봅니다.
- "내일 오후 3시 회의 잡아 줘"처럼 말하면 Bloom 캘린더에 일정을 등록해 줍니다. 메모와 타이머도 말로 편하게 남길 수 있습니다.
- 다른 앱에서 글을 드래그해 고른 뒤 AI 빠른 동작(Ctrl+Alt+A)을 누르면 요약, 번역, 문장 다듬기를 거쳐 원래 자리에 바로 붙여 넣어 줍니다.
- Claude Code나 Codex에 시켜 둔 작업이 끝났을 때, 승인을 기다릴 때, 사용 한도가 얼마 남지 않았을 때 알림 카드로 알려 줍니다.
- API 키는 OpenAI, Anthropic, Gemini, OpenRouter, DeepSeek, xAI, OpenCode Go를 지원하며, Windows 암호화 기능으로 이 PC에만 안전하게 보관합니다.

### 홈

<img src="docs/media/ko/home.webp" alt="두 열로 나뉜 넓은 홈. Claude 계정 3개와 ChatGPT 계정 1개의 사용량 카드, 토큰 비용과 그래프, 플레이어, 즐겨찾기, 이번 주">

- 검색줄이 비어 있을 때는 아래쪽에 즐겨찾기, AI 사용량, 이번 주 일정, 타이머, 메모, 미디어 플레이어가 모여 있는 홈 화면이 나타납니다.
- AI 사용량 블록은 Claude와 ChatGPT 계정마다 5시간 한도와 주간 한도가 얼마나 남았는지 보여 줍니다. 계정을 여러 개 등록해 두고 함께 볼 수도 있습니다.
- 토큰 블록은 이 PC에 남은 Claude Code와 Codex 사용 기록을 세어 API 요금으로 치면 얼마인지 환산해 보여 줍니다. 처음에는 꺼져 있으며 필요할 때 켤 수 있습니다.
- Ctrl+E를 누르면 홈에 놓인 블록의 순서를 바꾸거나 안 쓰는 블록을 숨기고 다시 꺼낼 수 있습니다.

### Bloom Lens와 컬러 피커

<img src="docs/media/ko/lens.webp" alt="화면의 영어 메시지를 Bloom Lens로 골라 한국어로 번역한 카드">

- Bloom Lens(Ctrl+Alt+L)로 화면 일부를 마우스로 끌어 선택한 뒤 숫자 키로 할 일을 정합니다. `1` 묻기, `2` 번역, `3` 글자 복사, `4` 실시간 번역, `5` 화면에 고정, `6` 캡처, `7` 컬러 팔레트를 지원합니다.
- 자주 쓰는 번역(Ctrl+Alt+T)과 글자 복사(Ctrl+Alt+O)는 단계를 거치지 않고 바로 실행하는 전용 단축키가 따로 있습니다.
- 컬러 피커로 화면의 원하는 색을 찍으면 HEX, RGB, HSL 색상 코드와 글자 가독성(명암 대비)을 함께 보여 줍니다.
- 컬러 팔레트는 선택한 화면 영역에서 주요 색상을 뽑아 주며, AI를 거치지 않고 이 PC에서 바로 계산합니다.

### 옆에 붙는 도구

<img src="docs/media/ko/tools.webp" alt="고정한 런처의 옆에 메모가, 아래에 계산기가 붙어 있는 모습">

- Ctrl+J를 누르면 메모, 타이머, 계산기, 클립보드 기록, 단축키 안내 창을 엽니다.
- 도구 창을 런처 가까이에 놓으면 자석처럼 옆에 붙어 함께 움직이고, 끌어당겨 떼면 일반 창처럼 따로 띄워 둘 수 있습니다.
- 메모는 마크다운 문법을 지원하며, 창을 열지 않고 검색줄에 `메모 우유 사기`라고 적어서 바로 남길 수도 있습니다.
- 클립보드 기록(Ctrl+Alt+V)은 그동안 복사했던 글과 이미지, 파일을 차곡차곡 모아 두어 언제든 다시 꺼내 쓰게 해 줍니다.
- Ctrl+P로 런처를 다른 창들보다 항상 위에 고정할 수 있고, Ctrl+= / Ctrl+-로 런처 크기를 80~150% 사이에서 조절합니다.

### Bloom Space Cleaner

<img src="docs/media/ko/space.webp" alt="폴더 안의 영상, 다운로드, 사진, 프로젝트가 크기만큼의 블록으로 나뉜 지도와 큰 파일 목록">

- 드라이브나 폴더 안에서 어떤 파일과 폴더가 용량을 많이 차지하는지 크기만큼의 블록 지도로 한눈에 보여 줍니다.
- 블록을 눌러 하위 폴더로 들어가면서 살펴보고, 큰 파일 목록에서 필요 없는 파일을 골라 바로 휴지통으로 보냅니다.
- 실수로 중요한 영역을 지우지 않도록 드라이브 루트와 Windows 폴더는 선택할 수 없게 막아 두었으며, 지운 파일은 언제든 휴지통에서 되살릴 수 있습니다.

### 설정

<img src="docs/media/ko/settings.webp" alt="테마와 강조색을 고르는 일반 설정과, Bloom이 저장한 것을 크기와 함께 보여 주는 데이터 페이지">

- 설정 창을 일일이 뒤지지 않아도 검색줄에 설정 이름을 적으면 해당 설정 페이지가 바로 열립니다.
- 원하는 강조색을 자유롭게 고를 수 있고, 다크·라이트 테마에서 글자가 잘 읽히는지 대비도 함께 보여 줍니다.
- 쓰지 않는 기능을 끄면 메뉴와 단축키 목록에서 깔끔하게 빠지며, 기존에 저장된 데이터는 지워지지 않고 그대로 남습니다.
- 데이터 페이지에서는 항목별로 저장된 데이터와 용량을 확인하고, 원하는 항목만 골라 지우거나 백업 파일로 내보낼 수 있습니다.
- 사용자 명령에 자주 쓰는 스크립트나 상용구(스니펫), 여러 명령을 이어 붙인 묶음(`메모장 ; 볼륨 30 ; yt lofi`)을 등록해 두면 단축어 하나로 한 번에 실행합니다.
- 화면 언어는 한국어, English, 日本語, 简体中文 가운데 고를 수 있습니다.
- 문제 보고를 누르면 로그에서 사용자 이름과 이메일을 가린 뒤 클립보드에 복사하고 GitHub 이슈 페이지를 열어 줍니다. Bloom이 알아서 외부로 전송하는 정보는 없습니다.

## 애드온

<img src="docs/media/ko/addons.webp" alt="설정 › 애드온의 작업표시줄 폴더 편집 화면과 알약 모양 미리 보기">

애드온은 Bloom 안에서 내려받아 확인하고 실행할 수 있는 별도 프로그램입니다. 애드온 코드는 Bloom 본체에 포함되어 있지 않으며, 현재 제공하는 공식 애드온 5종은 모두 GPL-3.0 라이선스로 별도 저장소에 공개되어 있습니다.

### 작업표시줄 (PillTaskbar)

<img src="docs/media/ko/addon-taskbar.webp" alt="위 끝의 아일랜드가 알약 작업표시줄로 펼쳐지고, 폴더를 열어 안의 앱을 켭니다">

- Windows 11 기본 작업표시줄을 화면 가운데에 떠 있는 반투명 유리 알약 모양으로 바꿔 줍니다.
- 작업표시줄의 앱 아이콘을 다른 아이콘 위로 끌어다 놓으면 폴더로 묶이고, 설정 › 애드온에서 폴더 이름과 구성을 편집할 수 있습니다.
- 시작 단추는 Bloom 꽃 모양 버튼으로 바뀌고, 시스템 트레이 앞쪽에 지금 재생 중인 음악 정보가 표시됩니다.
- 등록해 둔 계정마다 남은 AI 사용량 막대를 작업표시줄에 나란히 띄워 볼 수 있습니다.
- 숨김 아일랜드 옵션을 켜면 평소에는 작업표시줄이 숨고, 화면 맨 위 가운데에 작은 아일랜드 바가 생깁니다.
- 처음 켤 때 한 번 Microsoft 기호 서버에서 실행에 필요한 기호 파일(약 70 MB)을 내려받습니다.

### 모니터별 Alt+Tab

<img src="docs/media/ko/addon-alttab.webp" alt="커서가 있는 모니터에 Alt+Tab이 뜨고 그 모니터의 창만 보입니다">

모니터를 여러 대 쓸 때 Alt+Tab을 누르면 마우스 커서가 있는 모니터에 창 전환 목록이 뜨고, 그 모니터에 열려 있는 창만 추려서 보여 줍니다.

### 메모리 정리

<img src="docs/media/ko/addon-memory.webp" alt="시계 옆 램 모듈을 누르면 창이 열리고, 지금 정리로 메모리를 비웁니다">

- 작업표시줄 시계 옆 램 모듈에 메모리 사용률이 나옵니다. 누르면 창이 열리고, 지금 정리로 메모리를 비웁니다.
- 프로그램 탭에서 메모리를 많이 쓰는 프로그램을 골라 끝낼 수 있습니다.

### 모니터 밝기 조정

<img src="docs/media/ko/addon-brightness.webp" alt="시계 옆 모니터를 누르면 창이 열리고, 슬라이더 하나로 두 모니터가 함께 밝아지며, 휠로 다시 어둡게 합니다">

- 시계 옆 모니터 아이콘에 밝기가 나옵니다. 슬라이더 하나로 모든 모니터를 함께 바꾸고, 아이콘 위에서 휠을 돌려도 바뀝니다.
- 단축키로 밝기를 바꾸고, 자리를 비우면 화면을 어둡게 할 수 있습니다.

### 커피

<img src="docs/media/ko/addon-coffee.webp" alt="커피가 꺼져 있으면 입력 없이 10분 뒤 화면이 꺼지고, 커피를 1시간 켜 두면 같은 10분이 지나도 화면이 켜져 있습니다">

- 시계 옆 커피잔을 켜 두면 PC와 화면이 잠들지 않습니다.
- 끌 때까지 또는 정한 시간 동안 켜 둡니다. 커피잔을 오른쪽 클릭하면 바로 켜고 끕니다.

### 받기와 확인

- 설정 › 애드온에서 버튼 하나로 간편하게 내려받습니다.
- 내려받은 파일이 Bloom에 기록된 SHA-256 해시값과 정확히 일치할 때만 설치를 진행합니다.
- 처음 받은 애드온은 바로 켜지지 않고 꺼진 상태로 등록되며, 켤 때 어떤 동작을 하는지 먼저 안내합니다.
- 애드온을 켤 때와 Bloom을 시작할 때 새 버전이 나왔는지 확인합니다.
- 만약 애드온 때문에 문제가 생기면 Bloom을 켤 때 Shift를 누르고 있으세요. 그 실행에서는 모든 애드온을 건너뛰고 안전하게 켜집니다.

### 직접 넣는 애드온

직접 만든 애드온은 `%LOCALAPPDATA%\Bloom\user-addons` 폴더에 넣고 Bloom을 다시 시작하면 설정 › 애드온 목록에 나타납니다. 처음에는 꺼진 상태로 등록되며, 실행할 때 `addon.json`에 적힌 SHA-256 해시로 파일이 바뀌지 않았는지 확인합니다.

## 단축키

| 키 | 하는 일 |
| --- | --- |
| `Win` · `Alt+Space` | Bloom 열기 (관리자 권한 앱 위에서 `Win`은 설정 › 일반 › 관리자 앱 위에서도 Win 키 사용) |
| `Tab` / `Shift+Tab` | 검색줄의 글로 빠른 AI에게 묻기 / 새 대화로 묻기 |
| `Ctrl+K` | 고른 결과로 할 수 있는 일 |
| `Ctrl+1` … `Ctrl+9` | 즐겨찾기 |
| `Ctrl+J` | 도구 (메모 · 타이머 · 계산기 · 클립보드 기록 · 단축키) |
| `Ctrl+P` | 런처를 맨 위에 고정 |
| `Ctrl+E` | 홈 편집 |
| `Ctrl+,` | 설정 |
| `Ctrl+W` | 창 전환에서 고른 창 닫기 |
| `?` | 빈 검색줄에서 Bloom이 알아듣는 표현을 종류별로 표시 |
| `Ctrl+=` · `Ctrl+-` · `Ctrl+0` | 크게 · 작게 · 100% |
| `Esc` | 뒤로 → 글자 지우기 → 닫기 (빠른 AI의 답은 계속 만들어짐) |
| `Ctrl+.` | 빠른 AI 답 멈추기 |
| `Ctrl+Shift+M` · `Ctrl+M` | AI 채팅에서 AI 바꾸기 · 모델 고르기 |
| `Ctrl+Alt+C` | AI 채팅 창 |
| `Ctrl+Alt+A` | AI 빠른 동작 |
| `Ctrl+Alt+L` | Bloom Lens |
| `1` … `7` | Bloom Lens에서 AI에게 묻기 · 번역 · 글자 복사 · 실시간 번역 · 화면에 고정 · 캡처 · 컬러 팔레트 |
| `← →` · `−` `+` · `Enter` | 컬러 팔레트에서 색 고르기 · 색 개수 줄이기 · 늘리기 · HEX 복사 |
| `Ctrl+Alt+T` · `Ctrl+Alt+O` | 화면 번역 · 화면 글자 복사 |
| `Ctrl+Alt+V` | 클립보드 기록 |
| Bloom이 켜질 때 `Shift` | 이번 실행에서 애드온 켜지 않기 |

전역 단축키는 설정 › 단축키에서 바꾸거나 끕니다. 입력 칸을 고른 뒤 쓸 키를 누르고, Backspace로 지웁니다. Ctrl, Alt, Shift, Win과 함께 누르는 키를 쓸 수 있고, F1~F24와 PrintScreen, Pause, 미디어 키는 단독으로도 씁니다. 창 전환, 이모지, 컬러 피커, 바로 AI에게 묻기, 실시간 번역, 캡처에도 단축키를 붙일 수 있으며 기본은 꺼져 있습니다.

## AI 기능에 대해

AI 기능은 이 PC에 설치하고 로그인한 공식 CLI를 씁니다. 로그인 정보는 그 CLI가 관리하고, 사용량과 이용 약관은 로그인한 계정의 기준을 따릅니다. CLI가 없으면 AI 기능만 꺼지고 나머지는 그대로 동작합니다.

- Claude는 [Claude Code](https://docs.anthropic.com/en/docs/claude-code) CLI(`claude`)를 씁니다.
- ChatGPT는 [Codex CLI](https://github.com/openai/codex)(`codex`)를 씁니다.
- omp는 [oh-my-pi](https://github.com/can1357/oh-my-pi) CLI(`omp`)이며, 직접 고를 때만 씁니다.
- API 키는 Windows가 암호화해 이 PC에만 저장하고, 각 키는 그 서비스로만 보냅니다.

AI에게는 사용자가 직접 보낸 것(질문, 첨부한 파일, Bloom Lens로 고른 화면, 번역할 글)과 PC 작업 도구가 돌려준 결과만 전달됩니다. Claude는 대화별 폴더 `%LOCALAPPDATA%\Bloom\ai\files\<대화>` 안에서 파일을 읽고 씁니다. ChatGPT(Codex)와 omp에서는 텍스트 첨부를 메시지에 넣고, 만든 파일은 답에서 꺼내 저장합니다.

### PC 작업과 승인

Bloom은 이 PC 안에서만 열리는 MCP 서버(`Bloom.exe --mcp`, 이 Windows 사용자만 여는 named pipe)로 `pc` 도구 하나를 AI에게 줍니다. 할 수 있는 일은 5묶음이고, 설정 › PC 작업 · MCP에서 묶음마다 켜고 끕니다.

| 묶음 | 하는 일 |
| --- | --- |
| 찾아보기 | 앱, 파일(Everything), 최근 파일, 창, 타이머와 일정, 메모, 사용자 명령 조회. 승인 없이 실행 |
| 열기 · 실행 | 앱, 파일, 사이트 열기, 검색줄 한 줄 실행(`볼륨 30`, `yt lofi`), 사용자 명령 실행 |
| 메모 · 타이머 · 일정 | Bloom 메모에 적기, 타이머, 캘린더 일정 |
| 파일 정리 | 이름 바꾸기, 옮기기, 복사, 휴지통 |
| 클립보드 | 읽기, 넣기 |

조회가 아닌 작업은 채팅 안의 허용, 이 대화에서 항상 허용, 거절 단추로 정합니다. 이 PC에 이미 등록된 다른 MCP 서버도 같은 페이지에 서버마다 스위치로 나오며 기본은 꺼져 있고, 켠 서버의 도구도 실행 전에 승인 카드를 띄웁니다. 도구를 쓰면 요청마다 입력 토큰이 약 250~500개 늘어나고, 묶음을 끄면 줄어듭니다.

<details>
<summary>CLI별 연결 방식, 사용량 조회, 토큰 기록, 업데이트 확인</summary>

**Claude Code**는 Bloom이 고른 MCP 서버만 쓰도록 `--strict-mcp-config`로 실행하고, 승인은 `--permission-prompt-tool stdio`로 채팅에 띄웁니다. 도구 정의가 하나라 늘어나는 입력 토큰은 요청마다 약 500개입니다.

**Codex**는 대화마다 실행할 때 `-c mcp_servers.bloom…`으로만 Bloom 도구를 더합니다. Codex의 실행 모드에는 자체 승인 질문이 없어서 Bloom의 MCP 서버가 작업 직전에 승인 카드를 띄우고 기다립니다. Codex는 필요한 도구 설명만 불러오므로 늘어나는 입력 토큰은 요청마다 약 250개입니다. Bloom의 Codex는 Bloom 전용 설정으로 실행되어 사용자의 `config.toml`이 Bloom 채팅에 영향을 주지 않습니다.

**omp**도 `--no-ui`로 실행되어 스스로 묻지 못하므로 Bloom의 MCP 서버가 같은 카드를 띄웁니다. Bloom 도구와 켜 둔 다른 서버는 `%LOCALAPPDATA%\Bloom\ai\omp-work` 아래 대화별 폴더의 `.omp/mcp.json`에만 적고, omp 자체 설정은 건드리지 않습니다.

**다른 MCP 서버**는 Claude Code, Claude 데스크톱, Cursor, VS Code, Windsurf, Codex의 설정과 Claude Code로 연 프로젝트의 `.mcp.json`에서 찾습니다. 서버 설정은 실행할 때마다 원래 파일에서 읽습니다. ChatGPT에서는 서버 앞에 Bloom 중계(`Bloom.exe --mcp-proxy`)를 두어 승인을 받습니다.

**AI 사용량**은 `claude -p /usage`(모델 호출 없음)와 `codex app-server`의 한도 조회로 계정마다 차례로 확인합니다. 추가한 계정은 `%LOCALAPPDATA%\Bloom\accounts`의 폴더를 `CLAUDE_CONFIG_DIR`와 `CODEX_HOME`으로 씁니다. ChatGPT의 가벼운 방식을 고르면 그 계정의 Codex 로그인으로 Codex 상태 화면이 읽는 주소에 HTTPS 요청을 한 번 보냅니다. 이 주소는 공식 API가 아니며, 토큰 갱신은 Bloom이 만든 폴더에서만 하고 `~/.codex`에서는 하지 않습니다. Bloom은 Claude 로그인 정보를 읽지 않습니다.

**사용량 파일 내보내기**(설정 › AI 계정, 기본 꺼짐)를 켜면 사용 퍼센트와 리셋 시각, 직접 붙인 계정 이름을 이 PC의 다른 프로그램이 읽도록 `%LOCALAPPDATA%\Bloom\export\ai-usage.json`에 씁니다. 작업표시줄 애드온은 이 파일을 읽습니다. 이메일과 로그인 정보는 들어 있지 않습니다.

**작업 알림**은 이 PC의 Claude Code와 Codex 기록(`~/.claude/sessions`, `~/.codex/sessions`)을 읽고, 기록은 이 PC에 남습니다.

**토큰 블록**은 이 PC에 있는 모든 계정의 Claude Code와 Codex 기록을 읽습니다(Claude 폴더마다 `projects`, Codex 폴더마다 `sessions`와 `archived_sessions`). Bloom은 `%LOCALAPPDATA%\Bloom\tokens`에 모델과 effort, 세션별 시간 합계, 그리고 세션마다 Claude Code나 Codex가 붙인 제목과 마지막 폴더 이름만 둡니다. 메시지 내용과 전체 경로는 저장하지 않습니다. 처음에는 기록 전체를 백그라운드에서 낮은 우선순위로 한 번 읽고, 그다음부터는 늘어난 부분만 읽습니다. Codex가 이미 압축한 기록(`.zst`)은 읽지 않습니다. Claude Code는 30일 지난 기록을 지우므로 30일보다 긴 기간은 Bloom이 세기 시작한 날부터 채워집니다. 환산액은 같은 토큰을 각 회사의 API 가격으로 냈을 때의 금액이며(내장 가격표, 2026-11-02 확인) 실제로 낸 돈이 아닙니다. 가격은 설정 › AI 계정 › 가격표 고치기에서 `prices.json`으로 바꿉니다.

**업데이트 확인**은 GitHub API(`api.github.com`)에서 이 저장소의 최신 릴리스를 조회하고 새 버전을 GitHub에서 받습니다. 이 요청에는 저장소 정보만 쓰며 설정과 파일, 대화는 이 PC에 남습니다.

</details>

## 설치와 빌드

<details>
<summary>직접 빌드하기, 데이터 폴더, 릴리스</summary>

직접 빌드하려면 Windows 10/11과 [.NET 10 SDK](https://dotnet.microsoft.com/download)가 필요합니다. `tools\package.ps1`은 Win 키 도우미 때문에 C++ 빌드 도구(Visual Studio의 'C++를 사용한 데스크톱 개발')도 필요합니다.

```powershell
dotnet build src\Bloom\Bloom.csproj -c Release
# 또는 설치본 만들기
powershell -File tools\package.ps1   # → dist\app
powershell -File tools\install.ps1   # → %LOCALAPPDATA%\Programs\Bloom, 시작 메뉴 바로 가기
```

데이터는 `%APPDATA%\Bloom`과 `%LOCALAPPDATA%\Bloom`에 저장됩니다. 개발용 실행은 `Bloom.exe --data <폴더>`로 다른 데이터 폴더를 쓰고, `--lang ko|en`으로 언어를 고정합니다. 자동 업데이트는 설치 파일로 설치한 Bloom에서만 동작하며, `tools\install.ps1`로 만든 설치본은 직접 업데이트해야 합니다.

릴리스는 `src/Bloom/Bloom.csproj`의 `<Version>`을 올리고 `CHANGELOG.md`의 `## <Version>` 제목 아래에 바뀐 점을 적어 푸시한 뒤, GitHub에서 Actions › Release › Run workflow를 실행합니다. 워크플로가 테스트와 빌드를 거쳐 [Velopack](https://velopack.io) 설치 파일과 업데이트 패키지를 만들고 `v<Version>` 릴리스를 게시합니다. `addons/catalog.json`의 SHA-256이 비었거나 틀리면 릴리스하지 않습니다.

</details>

## 코드 구조

<details>
<summary>폴더 구성, 개발 규칙, 테스트와 Lint</summary>

`src/Bloom`은 기능별 폴더로 나뉩니다. 큰 클래스는 `partial`로 주제별 파일에 나눕니다.

| 폴더 | 내용 |
| --- | --- |
| `App/` | `Host`(시작, 단축키, 명령, Lens, 사용자 명령 실행, 파일 동작 연결), 작업 알림(`Notices`), 업데이트(`Updates`, Velopack), 로그인 작업(`LogonTask`), 문제 보고, 트레이, Win 키 훅 |
| `App/Engine/` | 검색줄에 보일 결과 만들기. 루트 검색, 홈 블록, 항목 변환, 보기별 결과(파일, 클립보드, 캘린더, 타이머, 색, winget 등) |
| `App/Mcp/` | MCP 서버와 중계, `pc` 도구(`BloomTools`) |
| `App/Qa/` | 화면 밖 캡처 QA (`--capture <폴더> --data <폴더> --lang en`) |
| `Addons/` | 애드온 목록(`AddonCatalog`, `addons/catalog.json`), 받기와 SHA-256 확인(`AddonDownload`), addon.json 읽기(`AddonManifest`), 실행과 재시작(`AddonRunner`), 파일로 주고받기(`AddonProtocol`), 작업표시줄 상태 |
| `Core/` | 저장되는 것(설정, 즐겨찾기, 사용 기록, 입력어 학습, 타이머, 메모, 색)과 JSON 저장, 한글 검색, 매칭, 데이터 지우기, 화면 언어(`Lang`) |
| `Search/` | 앱 목록, 계산기(식, 단위, 환율), Everything, 열린 창, 최근 파일, 이모지와 기호, 시스템 항목, 사용자 명령 |
| `Ai/` | Claude Code, Codex, omp CLI 세션, API 키 대화(`Api/`), 대화와 그룹(`Chats`), 번역, AI 빠른 동작, 사용량(`AiUsage`), 토큰 집계, 작업 기록 읽기(`ClaudeWatch`, `CodexWatch`) |
| `Music/` | 미니 플레이어(재생 대기열, 재생목록, 앨범 아트, 다른 앱의 재생 중인 곡) |
| `Ocr/` | 화면 영역 고르기, Windows OCR, 번역 창, 색 찍기 |
| `Clip/`, `Space/`, `Browser/` | 클립보드 기록, Bloom Space Cleaner, 브라우저로 넘기기 |
| `Native/` | Win32 호출(System32의 DLL만), WinRT 호출, 시스템 토글(`SystemControl`), 탐색기 방식 파일 작업(`ShellFiles`) |
| `Ui/` | 런처 창(`Launcher/`, 보기마다의 칩과 안내, 결과는 `Launcher.ViewSpecs.cs` 한 곳에), AI 채팅 창(`Chat/`), 설정 창(`Settings/`, 애드온 페이지와 작업표시줄 폴더 편집 포함), 도구 창(`Tools/`), 플레이어(`Player/`), 공용 컨트롤(`Controls/`, G2 모서리 `SmoothBorder`, `AccentPicker` 등), 테마(`Theme/`) |
| `Assets/` | 아이콘, 회사 로고(`Logos/`), 이모지와 기호 목록(`symbols.tsv`), 일본어와 중국어 화면 문구(`Lang/`) |
| `src/WinKeyHelper` | 관리자 권한 앱 위에서 Win 키를 받는 도우미 (NativeAOT) |
| `tests/Bloom.Tests` | 자동 테스트. 테스트 프레임워크 없이 콘솔로 실행 |

새 기능은 `Engine.<기능>.cs`(결과)와 `Launcher.ViewSpecs.cs`의 보기 한 줄, 필요하면 `Core/<기능>.cs`(저장)로 나눕니다. 화면 문구는 모두 `Tr("한국어", "English")`로 쓰고, 일본어와 간체 중국어는 영어 문장을 키로 쓰는 `src/Bloom/Assets/Lang/ja.tsv`와 `zh.tsv`에서 불러옵니다. `python tools/lang.py check`로 빠진 키를, `todo ja`로 번역할 문구를 확인합니다. 검색 키워드와 파서 데이터는 한국어와 영어를 함께 둡니다. 화면 문구는 [docs/COPY.md](docs/COPY.md)를 따릅니다. QA와 개발 실행은 늘 `--data <임시 폴더>`로 실제 데이터와 나눕니다.

```powershell
# 테스트 (종료 코드 = 실패 수)
dotnet run --project tests\Bloom.Tests
# Lint (분석기와 서식), 경고 0
dotnet build src\Bloom\Bloom.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet build tests\Bloom.Tests\Bloom.Tests.csproj -c Debug --no-incremental -p:AnalysisLevel=latest-recommended -p:EnforceCodeStyleInBuild=true
dotnet format whitespace src\Bloom\Bloom.csproj --verify-no-changes
dotnet format whitespace tests\Bloom.Tests\Bloom.Tests.csproj --verify-no-changes
```

</details>

## 라이선스

Bloom은 MIT 라이선스입니다([LICENSE](LICENSE)).

환율은 [Rates By Exchange Rate API](https://www.exchangerate-api.com)를 씁니다.

작업표시줄 애드온 PillTaskbar([소스](https://github.com/hyeon1035298/PillTaskbar_BloomAddon))와 모니터별 Alt+Tab 애드온([소스](https://github.com/hyeon1035298/AltTabPerMonitor_BloomAddon))은 GPL-3.0인 별도 프로그램이며 Bloom 설치 파일에 들어 있지 않습니다. Bloom 자체는 MIT 그대로입니다.

AI 사용량에 나오는 OpenAI와 Anthropic 로고는 각 회사의 상표이며 MIT 라이선스에 포함되지 않습니다.

Bloom은 Anthropic, OpenAI, voidtools, Raycast와 관련 없는 비공식 개인 프로젝트입니다.
