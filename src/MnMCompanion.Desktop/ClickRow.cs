using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MnMCompanion;

/// <summary>
/// A row of the small window that does something when it is clicked. It looks like a panel, so to the accessibility interface (screen readers,
/// UI automation) it would be nothing; this makes it a button named for the row, which can be invoked without a mouse.
/// </summary>
internal sealed class ClickRow : Grid
{
    /// <summary>What a click, or an invoke from accessibility, does.</summary>
    public Action? Activate { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ClickRow owner) : ControlAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(ClickRow);

        protected override bool IsControlElementCore() => true;

        public void Invoke() => Dispatcher.UIThread.Post(() => owner.Activate?.Invoke());
    }
}
