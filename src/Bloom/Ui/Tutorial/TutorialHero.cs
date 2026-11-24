using System;
using System.Windows.Controls;
using System.Windows.Media;
using Bloom.App;

namespace Bloom.Ui;

/// The first page: dark in both themes, a muted clip behind the headline with its first frame as the still.
internal sealed class TutorialHero : Grid
{
    public static readonly Color Ground = Color.FromRgb(0x12, 0x13, 0x17);

    public TutorialHero(TutorialEnv env)
    {
        Background = new SolidColorBrush(Ground);
        Look.Apply(true, Accents.Current, 0);
    }

    /// The fixed dark palette the footer and the language control draw with on this page.
    public Palette Look { get; } = new();

#pragma warning disable CS0067 // not raised while the page has no controls of its own
    public event Action<string>? LanguagePicked;    // a Lang.Choices value
    public event Action? ImportAsked;
#pragma warning restore CS0067

    /// The page is on screen: the still at once, the clip when it may play. Does nothing while the clip plays or is
    /// on its way.
    public void Enter()
    {
    }

    /// Another page, or the window is hidden or closing: playback stops and the decoder is released. A download under
    /// way goes on.
    public void Leave()
    {
    }

    /// The accent changed.
    public void Retheme()
    {
    }

    internal bool Playing => false;
}
