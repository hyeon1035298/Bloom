using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bloom.App;

/// One approval request on the Run Card add-on's card (RunCardLink.Ask): whether the card took it, then its answer.
/// Both settle when the connection drops (false, null), so a wait on them never outlives the add-on.
internal sealed class RunCardAsk
{
    private readonly TaskCompletionSource<bool> shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string?> answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IReadOnlyList<CardQuestion>? questions;

    public RunCardAsk(string id, RunCardPeer peer, IReadOnlyList<CardQuestion>? questions)
    {
        Id = id;
        Peer = peer;
        this.questions = questions;
    }

    public string Id { get; }

    public RunCardPeer Peer { get; }

    /// True: the card shows it; false: it can't (`none`) or the add-on went.
    public Task<bool> Shown => shown.Task;

    /// The answer as RunCardProtocol.AnswerFor makes it; null: the card has none to give (`none`, an answer that doesn't
    /// fit the request, or the add-on went first), and Bloom's own card asks.
    public Task<string?> Answered => answered.Task;

    /// A line about this request. An answer says the card showed it too. `none` may come after `shown` (the card was
    /// closed), and the add-on drops its card after any answer, one that doesn't fit included: either way nothing more
    /// comes about it.
    public void Heard(RunCardMessage m)
    {
        switch (m.Kind)
        {
            case RunCardProtocol.Shown:
                shown.TrySetResult(true);
                break;
            case RunCardProtocol.None:
                shown.TrySetResult(false);
                answered.TrySetResult(null);
                break;
            case RunCardProtocol.Answer:
                shown.TrySetResult(true);
                answered.TrySetResult(RunCardProtocol.AnswerFor(m, questions));
                break;
        }
    }

    public void Dropped()
    {
        shown.TrySetResult(false);
        answered.TrySetResult(null);
    }

    /// Bloom stopped waiting: the add-on's card goes.
    public void Cancel() => RunCardLink.Cancel(this);
}
