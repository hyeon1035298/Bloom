using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// 내 AI 명령's own runs (UserCommand.Every): one timer, set to the next run that is due (at most 15 minutes ahead, so a
/// sleep or a clock change is caught up soon). The first look waits a minute after the start. Runs go one at a time; each
/// answer goes into the command's AI chat, 알림 센터 and a result card.
internal sealed partial class Host
{
    private static readonly TimeSpan ScheduleFirstLook = TimeSpan.FromMinutes(1), ScheduleLongest = TimeSpan.FromMinutes(15), ScheduleShortest = TimeSpan.FromSeconds(5);
    private DispatcherTimer? schedule;
    private DateTime scheduleFrom;
    private bool scheduleRunning;
    private readonly CancellationTokenSource scheduleStop = new();

    /// Host, once (not in QA runs or the recording mode).
    private void StartSchedule()
    {
        scheduleFrom = DateTime.Now + ScheduleFirstLook;
        schedule = new DispatcherTimer(DispatcherPriority.Background, ui);
        schedule.Tick += async (_, _) => await ScheduleTick();
        UserCommands.Changed += ArmSchedule;
        ArmSchedule();
    }

    private static bool Scheduled(UserCommand c) => c.IsAi && c.Every is not null && c.Input == AiInput.None;

    /// The timer waits for the earliest run (none scheduled: it stops).
    private void ArmSchedule()
    {
        if (schedule is null || scheduleRunning) return;
        DateTime? next = null;
        if (Features.On("mine"))
            foreach (var c in UserCommands.Current.Items.Where(Scheduled))
                if (AiSchedule.Next(c.Every!, Local(c.LastRun)) is { } at && (next is null || at < next)) next = at;
        schedule.Stop();
        if (next is null) return;
        var wait = (next.Value < scheduleFrom ? scheduleFrom : next.Value) - DateTime.Now;
        schedule.Interval = wait < ScheduleShortest ? ScheduleShortest : wait > ScheduleLongest ? ScheduleLongest : wait;
        schedule.Start();
    }

    private static DateTime Local(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    private async Task ScheduleTick()
    {
        schedule?.Stop();
        if (scheduleRunning) return;
        scheduleRunning = true;
        try
        {
            // one at a time, the most overdue first; each run moves its own next time on, so the loop ends
            while (!scheduleStop.IsCancellationRequested && Features.On("mine"))
            {
                var now = DateTime.Now;
                var due = UserCommands.Current.Items.Where(c => Scheduled(c) && AiSchedule.Due(c.Every!, Local(c.LastRun), now))
                    .OrderBy(c => AiSchedule.Next(c.Every!, Local(c.LastRun))).FirstOrDefault();
                if (due is null) break;
                await RunScheduled(due);
            }
        }
        catch (Exception e)
        {
            Log.Error("ai schedule", e);
        }
        finally
        {
            scheduleRunning = false;
            ArmSchedule();
        }
    }

    /// One run: marked as run before it starts (a crash or a quit never repeats it), then its answer is kept.
    private async Task RunScheduled(UserCommand c)
    {
        c.LastRun = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        UserCommands.Upsert(c);
        Log.Write($"ai schedule: {c.Id} runs");
        var (text, error, model) = await AiCommandRun.RunAsync(c, "", UserCommands.Current.Items, null, scheduleStop.Token);
        if (scheduleStop.IsCancellationRequested) return;
        bool ok = text is not null;
        string answer = text ?? error ?? Tr("응답 없음 · 다시 시도하세요", "No response. Try again.");
        string asked = $"{c.Name} · {AiSchedule.Name(c.Every!)}\n\n" + string.Join("\n→ ", AiCommandRun.Split(c.Body));
        var thread = Chats.Record(c.Thread, c.Name, model, asked, answer, !ok);
        // the command may have been changed or deleted while it ran: the chat goes to the one that is there now
        if (UserCommands.Current.Items.FirstOrDefault(x => x.Id == c.Id) is { } now && now.Thread != thread.Id)
        {
            now.Thread = thread.Id;
            UserCommands.Upsert(now);
        }
        AiCommandDone(ok, c.Name, answer, thread.Id);
    }

    /// The end of an AI command run the user wasn't watching: 알림 센터 and a card; both open the chat.
    private void AiCommandDone(bool ok, string name, string answer, string? thread)
    {
        string head = ok ? Tr("AI 명령 · 완료", "AI command · Done") : Tr("AI 명령 · 실패", "AI command · Failed");
        NoticeLog.Add(ok, head, name, answer, thread);
        Action? open = thread is null ? null : () => OpenThread(thread);
        Notices.Result(ok, head, name, NoticeLog.Preview(answer), open is null ? null : Tr("채팅 열기", "Open chat"), open, open);
    }

    /// 알림 센터's click: the AI chat window on that conversation (a deleted one says so).
    public void OpenThread(string id)
    {
        if (Chats.Threads.Find(t => t.Id == id) is { } t) OpenChatWindow(t);
        else Window.ShowToast(Tr("대화가 삭제되었습니다", "The chat was deleted"));
    }

    private void StopSchedule()
    {
        scheduleStop.Cancel();
        schedule?.Stop();
    }
}
