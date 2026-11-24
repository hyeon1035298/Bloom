using System.Collections.Generic;
using Bloom.App;

namespace Bloom.Ai;

/// The question a T3 thread waits on, as the chat's orange box asks it: T3's request id and its questions.
internal sealed record T3Question(string RequestId, IReadOnlyList<CardQuestion> Items);
