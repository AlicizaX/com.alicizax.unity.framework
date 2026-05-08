using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class InputAccelerationInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Input Acceleration", out VisualElement card);
                card.Add(CreateRow("Acceleration", Input.acceleration.ToString()));
                card.Add(CreateRow("Acceleration Event Count", Input.accelerationEventCount.ToString()));
                card.Add(CreateRow("Acceleration Events", GetAccelerationEventsString(Input.accelerationEvents)));
                root.Add(section);
            }

            private string GetAccelerationEventString(AccelerationEvent accelerationEvent)
            {
                return Utility.Text.Format("{0}, {1}", accelerationEvent.acceleration, accelerationEvent.deltaTime);
            }

            private string GetAccelerationEventsString(AccelerationEvent[] accelerationEvents)
            {
                string[] accelerationEventStrings = new string[accelerationEvents.Length];
                for (int i = 0; i < accelerationEvents.Length; i++)
                {
                    accelerationEventStrings[i] = GetAccelerationEventString(accelerationEvents[i]);
                }

                return string.Join("; ", accelerationEventStrings);
            }
        }
    }
}
