using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class InputCompassInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement actions = CreateSection("Actions", out VisualElement actionCard);
                VisualElement toolbar = CreateToolbarRow();
                toolbar.Add(CreateActionButton("Enable", () => Input.compass.enabled = true, DebuggerTheme.ButtonSurface));
                toolbar.Add(CreateActionButton("Disable", () => Input.compass.enabled = false, DebuggerTheme.ButtonSurface));
                actionCard.Add(toolbar);
                root.Add(actions);

                VisualElement section = CreateSection("Input Compass", out VisualElement card);
                card.Add(CreateRow("Enabled", Input.compass.enabled.ToString()));
                if (Input.compass.enabled)
                {
                    card.Add(CreateRow("Heading Accuracy", Input.compass.headingAccuracy.ToString()));
                    card.Add(CreateRow("Magnetic Heading", Input.compass.magneticHeading.ToString()));
                    card.Add(CreateRow("Raw Vector", Input.compass.rawVector.ToString()));
                    card.Add(CreateRow("Timestamp", Input.compass.timestamp.ToString()));
                    card.Add(CreateRow("True Heading", Input.compass.trueHeading.ToString()));
                }

                root.Add(section);
            }
        }
    }
}
