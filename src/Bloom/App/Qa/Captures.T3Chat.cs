using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Bloom.Ai;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set t3chat`: the AI chat window's T3 side with made-up threads and projects, nothing asked of T3
/// (QuietT3 answers every look "busy", so the made-up list stays, and refuses every send): the list by folder with a
/// running thread open, a question in the orange box, an approval to give in T3, and a new chat's folder cards; dark
/// and light.
internal static partial class Captures
{
    /// A T3 that is there and never answers: a look is "busy" (nothing read, nothing changed), the rest t3-off.
    private sealed class QuietT3 : IT3Link
    {
        public bool Present => true;
        public Task<T3Agent.Got> Look(string lane, string tool, JsonObject arguments, CancellationToken ct) => Task.FromResult(new T3Agent.Got(null, "busy"));
        public Task<T3Agent.Got> Act(string tool, JsonObject arguments, CancellationToken ct) => Task.FromResult(new T3Agent.Got(null, "t3-off"));
        public Task<T3Agent.Sent> Say(string thread, string text, string clientRequestId, CancellationToken ct) => Task.FromResult(new T3Agent.Sent(false, "t3-off"));
        public Task<T3Agent.Sent> Answer(string thread, IReadOnlyDictionary<string, string> given, CancellationToken ct) => Task.FromResult(new T3Agent.Sent(false, "t3-off"));
        public Task<T3Agent.Sent> Stop(string thread, CancellationToken ct) => Task.FromResult(new T3Agent.Sent(false, "t3-off"));
        public Task<(string RequestId, List<(string Id, CardQuestion Question)> Questions)?> Pending(string thread, CancellationToken ct) => Task.FromResult<(string, List<(string, CardQuestion)>)?>(null);
    }

    private static async Task T3ChatScreens(Host host, Launcher w, string dir)
    {
        string l = En ? "en" : "ko";
        var t3 = host.Chats.T3;
        var link = t3.Link;
        var s = Settings.Current;
        var (side, pick, project) = (s.ChatSide, s.T3Pick, s.T3Project);
        t3.Link = new QuietT3();
        s.ChatSide = "t3";
        DateTime now = DateTime.UtcNow;
        T3Row Row(string id, string? projectId, string ko, string en, string status, string model, double minutesAgo, bool settled = false, int pending = 0) =>
            new(id, projectId, Tr(ko, en), status, "claudeAgent", model, now.AddMinutes(-minutesAgo), settled, pending);
        var rows = new List<T3Row>
        {
            Row("qa-a1", "p1", "결제 화면 테스트 보강", "Shore up the checkout tests", "running", "claude-opus-5-5", 1),
            Row("qa-a2", "p1", "로그인 범위 정하기", "Scope the login rework", "waiting", "claude-opus-5-5", 6, pending: 1),
            Row("qa-a3", "p1", "의존성 올리기", "Bump the dependencies", "idle", "claude-sonnet-5-5", 95),
            Row("qa-b1", "p2", "README 번역 점검", "Check the README translation", "waiting", "gpt-6", 30, pending: 1),
            Row("qa-b2", "p2", "변경 기록 정리", "Tidy the changelog", "completed", "claude-sonnet-5-5", 26 * 60, settled: true),
            Row("qa-s1", null, "정규식 하나 짜기", "Write one regex", "idle", "claude-haiku-5-5", 3 * 24 * 60),
        };
        for (int i = 0; i < 4; i++) rows.Insert(3 + i, Row("qa-a" + (4 + i), "p1", "작은 수정 " + (i + 1), "Small fix " + (i + 1), "idle", "claude-sonnet-5-5", 200 + 60 * i));
        var projects = new List<T3Project>
        {
            new("p1", "shop-web", @"C:\work\shop-web", "claudeAgent", "claude-opus-5-5", null),
            new("p2", "docs", @"C:\work\docs", null, null, null),
            new("p3", "infra", @"C:\work\infra", null, null, null),
        };
        T3Item It(int position, string id, string type, string status, string ko, string en, string? title = null) => new(position, id, type, status, Tr(ko, en), title, false);
        ChatWindow? cw = null;
        try
        {
            t3.SetForQa(rows, projects);
            var running = t3.ChatForQa(rows[0], new[]
            {
                It(0, "u1", "user_message", "completed", "결제 화면 테스트를 보강해줘. 실패 경로 위주로.", "Shore up the checkout tests, mostly the failure paths."),
                It(1, "w1", "assistant_message", "completed", "지금 테스트는 성공 경로만 봐요. 카드 거절·시간 초과·중복 결제 세 경우를 더할게요.", "The tests only cover the happy path now. I'll add a declined card, a timeout and a double charge."),
                It(2, "t1", "dynamic_tool", "completed", "{}", "{}", Tr("checkout.test.ts 읽음", "Read checkout.test.ts")),
                It(3, "t2", "dynamic_tool", "completed", "{}", "{}", Tr("payment.ts 읽음", "Read payment.ts")),
                It(4, "t3", "command_execution", "inProgress", "{\"command\":\"pnpm test checkout\"}", "{\"command\":\"pnpm test checkout\"}"),
            });
            cw = new ChatWindow(host, running) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowActivated = false };
            cw.Show();
            var window = cw;
            async Task Cap(string name)
            {
                await Task.Delay(350);
                await window.Dispatcher.InvokeAsync(() => window.Capture(Path.Combine(dir, $"{l}-{name}.png")), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            foreach (string theme in new[] { "dark", "light" })
            {
                Theme.Apply(theme);
                cw.Open(running);
                await Cap($"{theme}-t3-1-running");
            }
            var asking = t3.ChatForQa(rows[1], new[]
            {
                It(0, "u1", "user_message", "completed", "로그인 화면을 정리해줘", "Tidy up the login screen"),
                It(1, "w1", "assistant_message", "completed", "로그인 화면과 세션 처리가 한 파일에 섞여 있어요. 바꾸기 전에 범위를 정해 주세요.", "The login screen and the session handling share one file. Tell me how far to go first."),
            }, new T3Question("qa-req", new[]
            {
                new CardQuestion(Tr("어디까지 바꿀까요?", "How far should I go?"), Tr("범위", "Scope"), new[]
                {
                    new CardChoice(Tr("로그인 화면만", "Just the login screen"), Tr("세션 코드는 그대로 둬요", "The session code stays as it is")),
                    new CardChoice(Tr("세션 처리까지", "The session handling too"), Tr("auth 폴더 전체를 나눠요", "Split up the whole auth folder")),
                }, false),
            }));
            foreach (string theme in new[] { "dark", "light" })
            {
                Theme.Apply(theme);
                cw.Open(asking);
                await Cap($"{theme}-t3-2-question");
            }
            Theme.Apply("dark");
            var approval = t3.ChatForQa(rows.Find(r => r.Id == "qa-b1")!, new[]
            {
                It(0, "u1", "user_message", "completed", "README 번역이 원문과 맞는지 봐줘", "Check the README translation against the original"),
                It(1, "w1", "assistant_message", "completed", "두 파일을 비교하려면 docs 밖의 파일을 읽어야 해요.", "Comparing them needs a file outside docs."),
            }, approval: true);
            cw.Open(approval);
            await Cap("dark-t3-3-approval");
            s.T3Project = "p1";
            s.T3Pick = null;
            foreach (string theme in new[] { "dark", "light" })
            {
                Theme.Apply(theme);
                cw.Open(t3.Draft());
                await Cap($"{theme}-t3-4-new");
            }
            cw.Width = cw.MinWidth;
            cw.Height = cw.MinHeight;
            cw.Open(running);
            await Cap("light-t3-5-small");
        }
        finally
        {
            cw?.Close();
            t3.Link = link;
            (s.ChatSide, s.T3Pick, s.T3Project) = (side, pick, project);
            t3.SetForQa(new List<T3Row>(), new List<T3Project>());
        }
    }
}
