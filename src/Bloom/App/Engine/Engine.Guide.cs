using System.Collections.Generic;
using Bloom.Core;
using Bloom.Search;

namespace Bloom.App;

/// "?" alone in the root search: 쓸 수 있는 표현, the examples by kind (Core\Expressions). Enter puts the example into the
/// search line, so its answer shows right there and the launcher stays open.
internal sealed partial class Engine
{
    private List<Section> RootGuide()
    {
        var sections = new List<Section>();
        foreach (var kind in Expressions.On())
        {
            var s = new Section(kind.Title);
            for (int n = 0; n < kind.Examples.Length; n++)
            {
                var example = kind.Examples[n];
                string input = example.Input;
                s.Items.Add(new ResultItem
                {
                    Id = $"guide:{kind.Id}:{n}",
                    Kind = ItemKind.Command,
                    Title = input,
                    Subtitle = example.Does,
                    Icon = IconSpec.Of(kind.Glyph),
                    PrimaryLabel = Tr("검색줄에 넣기", "Insert into search bar"),
                    StayOpen = true,
                    Run = () => host.Window.Go(Mode.Root, input),
                });
            }
            sections.Add(s);
        }
        return sections;
    }
}
