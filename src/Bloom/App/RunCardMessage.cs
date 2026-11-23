using System.Collections.Generic;

namespace Bloom.App;

/// One line from the Run Card add-on (RunCardProtocol.Parse): its kind and the fields that kind carries — hello and
/// prefs: Approvals (설정 '카드에서 승인·질문'); shown, none: Id; answer: Id and Value (allow | deny | terminal) or Picks
/// (the choice indexes per question, in order); open: Session and Pid (0: not known).
internal sealed record RunCardMessage(string Kind, string? Id = null, bool Approvals = false, string? Value = null,
    IReadOnlyList<IReadOnlyList<int>>? Picks = null, string? Session = null, int Pid = 0);
