using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class InputLocationInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement actions = CreateSection("Actions", out VisualElement actionCard);
                VisualElement toolbar = CreateToolbarRow();
                toolbar.Add(CreateActionButton("Enable", () => Input.location.Start(), DebuggerTheme.ButtonSurface));
                toolbar.Add(CreateActionButton("Disable", () => Input.location.Stop(), DebuggerTheme.ButtonSurface));
                actionCard.Add(toolbar);
                root.Add(actions);

                VisualElement section = CreateSection("Input Location", out VisualElement card);
                card.Add(CreateRow("Is Enabled By User", Input.location.isEnabledByUser.ToString()));
                card.Add(CreateRow("Status", Input.location.status.ToString()));
                if (Input.location.status == LocationServiceStatus.Running)
                {
                    card.Add(CreateRow("Horizontal Accuracy", Input.location.lastData.horizontalAccuracy.ToString()));
                    card.Add(CreateRow("Vertical Accuracy", Input.location.lastData.verticalAccuracy.ToString()));
                    card.Add(CreateRow("Longitude", Input.location.lastData.longitude.ToString()));
                    card.Add(CreateRow("Latitude", Input.location.lastData.latitude.ToString()));
                    card.Add(CreateRow("Altitude", Input.location.lastData.altitude.ToString()));
                    card.Add(CreateRow("Timestamp", Input.location.lastData.timestamp.ToString()));
                }

                root.Add(section);
            }
        }
    }
}
