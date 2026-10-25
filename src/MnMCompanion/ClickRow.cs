using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace MnMCompanion;

/// <summary>
/// A row of the small window that does something when it is clicked. It looks like a panel, so to Windows' accessibility interface (screen readers,
/// UI automation) it would be nothing; this makes it a button named for the row, which can be invoked without a mouse.
/// </summary>
internal sealed class ClickRow : Grid
{
    /// <summary>What a click, or an invoke from accessibility, does.</summary>
    public Action? Activate { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ClickRow owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(ClickRow);

        protected override bool IsControlElementCore() => true;

        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : base.GetNameCore();

        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPattern(pattern);

        public void Invoke() => owner.Dispatcher.BeginInvoke(() => owner.Activate?.Invoke());
    }
}
