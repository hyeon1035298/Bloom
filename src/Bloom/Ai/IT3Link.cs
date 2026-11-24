using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.App;

namespace Bloom.Ai;

/// What the T3 side asks of T3: T3Agent (its switch, its sign-in), or a test's stand-in.
internal interface IT3Link
{
    bool Present { get; }
    Task<T3Agent.Got> Look(string lane, string tool, JsonObject arguments, CancellationToken ct);
    Task<T3Agent.Got> Act(string tool, JsonObject arguments, CancellationToken ct);
    Task<T3Agent.Sent> Say(string thread, string text, string clientRequestId, CancellationToken ct);
    Task<T3Agent.Sent> Answer(string thread, IReadOnlyDictionary<string, string> given, CancellationToken ct);
    Task<T3Agent.Sent> Stop(string thread, CancellationToken ct);
    Task<(string RequestId, List<(string Id, CardQuestion Question)> Questions)?> Pending(string thread, CancellationToken ct);
}
