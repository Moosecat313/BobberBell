using System.Windows.Forms;

namespace BobberBell.UI.Controls;

// Tab strip button: the gold underline marks the active tab, so no system focus rectangle.
internal sealed class TabButton : Button
{
    protected override bool ShowFocusCues => false;
}
