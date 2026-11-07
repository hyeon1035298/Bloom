using System;
using Bloom.App;

namespace Bloom.Ui;

/// The clipboard view's two tabs, 기록 | 상용구 (the history and the snippets of 사용자 명령), on a Segmented at the
/// search line's right; Ctrl+Tab steps between them as it steps the file kinds.
internal sealed partial class Launcher
{
    public const string ClipHistoryTab = "history", ClipSnippetsTab = "snippets";

    private string clipTab = ClipHistoryTab;
    private bool clipTabPicked; // the next entry into the view keeps clipTab (a hotkey picked it)

    public string ClipTab => clipTab;

    private bool OnSnippets => clipTab == ClipSnippetsTab;

    /// The tab the clipboard view shows, switching it in place when the view is open.
    public void SetClipTab(string tab)
    {
        if (tab is not (ClipHistoryTab or ClipSnippetsTab) || tab == clipTab) return;
        clipTab = tab;
        if (Mode != Mode.Clipboard) return;
        actions.Close();
        // the typed text filtered the other tab; it starts empty here
        Go(Mode.Clipboard);
    }

    /// The tab the next opening of the view starts on (Host.ToggleClipTab, right before it opens the view).
    public void PickClipTab(string tab)
    {
        clipTab = tab;
        clipTabPicked = true;
    }

    /// Entering the view from elsewhere starts on 기록 (the home's 최근 복사, the search's 클립보드) unless a hotkey
    /// picked the tab; coming back from a form (상용구 고치기) keeps the tab it left.
    private void EnterClipTab(Mode from)
    {
        if (from is not (Mode.Clipboard or Mode.Form) && !clipTabPicked) clipTab = ClipHistoryTab;
        clipTabPicked = false;
    }

    private void StepClipTab() => SetClipTab(OnSnippets ? ClipHistoryTab : ClipSnippetsTab);

    private Segmented ClipTabs()
    {
        var tabs = new Segmented(new[] { (ClipHistoryTab, Tr("기록", "History")), (ClipSnippetsTab, Tr("상용구", "Snippets")) }, clipTab);
        tabs.ToolTip = Tr("Ctrl+Tab으로 넘기기", "Ctrl+Tab switches");
        tabs.Picked += v => Dispatcher.BeginInvoke(new Action(() => SetClipTab(v))); // after the click: the chrome is rebuilt
        return tabs;
    }
}
