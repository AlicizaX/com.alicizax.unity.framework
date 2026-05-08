using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class InputTouchInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Input Touch", out VisualElement card);
                card.Add(CreateRow("Touch Supported", Input.touchSupported.ToString()));
                card.Add(CreateRow("Touch Pressure Supported", Input.touchPressureSupported.ToString()));
                card.Add(CreateRow("Stylus Touch Supported", Input.stylusTouchSupported.ToString()));
                card.Add(CreateRow("Simulate Mouse With Touches", Input.simulateMouseWithTouches.ToString()));
                card.Add(CreateRow("Multi Touch Enabled", Input.multiTouchEnabled.ToString()));
                card.Add(CreateRow("Touch Count", Input.touchCount.ToString()));
                card.Add(CreateRow("Touches", GetTouchesString(Input.touches)));
                root.Add(section);
            }

            private string GetTouchString(Touch touch)
            {
                return Utility.Text.Format("{0}, {1}, {2}, {3}, {4}", touch.position, touch.deltaPosition, touch.rawPosition, touch.pressure, touch.phase);
            }

            private string GetTouchesString(Touch[] touches)
            {
                string[] touchStrings = new string[touches.Length];
                for (int i = 0; i < touches.Length; i++)
                {
                    touchStrings[i] = GetTouchString(touches[i]);
                }

                return string.Join("; ", touchStrings);
            }
        }
    }
}
