using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class InputGyroscopeInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement actions = CreateSection("Actions", out VisualElement actionCard);
                VisualElement toolbar = CreateToolbarRow();
                toolbar.Add(CreateActionButton("Enable", () => Input.gyro.enabled = true, DebuggerTheme.ButtonSurface));
                toolbar.Add(CreateActionButton("Disable", () => Input.gyro.enabled = false, DebuggerTheme.ButtonSurface));
                actionCard.Add(toolbar);
                root.Add(actions);

                VisualElement section = CreateSection("Input Gyroscope", out VisualElement card);
                card.Add(CreateRow("Enabled", Input.gyro.enabled.ToString()));
                if (Input.gyro.enabled)
                {
                    card.Add(CreateRow("Update Interval", Input.gyro.updateInterval.ToString()));
                    card.Add(CreateRow("Attitude", Input.gyro.attitude.eulerAngles.ToString()));
                    card.Add(CreateRow("Gravity", Input.gyro.gravity.ToString()));
                    card.Add(CreateRow("Rotation Rate", Input.gyro.rotationRate.ToString()));
                    card.Add(CreateRow("Rotation Rate Unbiased", Input.gyro.rotationRateUnbiased.ToString()));
                    card.Add(CreateRow("User Acceleration", Input.gyro.userAcceleration.ToString()));
                }

                root.Add(section);
            }
        }
    }
}
