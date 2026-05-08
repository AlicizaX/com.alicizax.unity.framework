using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class SystemInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("System Information", out VisualElement card);
                card.Add(CreateRow("Device Unique ID", SystemInfo.deviceUniqueIdentifier));
                card.Add(CreateRow("Device Name", SystemInfo.deviceName));
                card.Add(CreateRow("Device Type", SystemInfo.deviceType.ToString()));
                card.Add(CreateRow("Device Model", SystemInfo.deviceModel));
                card.Add(CreateRow("Processor Type", SystemInfo.processorType));
                card.Add(CreateRow("Processor Count", SystemInfo.processorCount.ToString()));
                card.Add(CreateRow("Processor Frequency", Utility.Text.Format("{0} MHz", SystemInfo.processorFrequency)));
                card.Add(CreateRow("System Memory Size", Utility.Text.Format("{0} MB", SystemInfo.systemMemorySize)));
#if UNITY_5_5_OR_NEWER
                card.Add(CreateRow("Operating System Family", SystemInfo.operatingSystemFamily.ToString()));
#endif
                card.Add(CreateRow("Operating System", SystemInfo.operatingSystem));
#if UNITY_5_6_OR_NEWER
                card.Add(CreateRow("Battery Status", SystemInfo.batteryStatus.ToString()));
                card.Add(CreateRow("Battery Level", GetBatteryLevelString(SystemInfo.batteryLevel)));
#endif
#if UNITY_5_4_OR_NEWER
                card.Add(CreateRow("Supports Audio", SystemInfo.supportsAudio.ToString()));
#endif
                card.Add(CreateRow("Supports Location Service", SystemInfo.supportsLocationService.ToString()));
                card.Add(CreateRow("Supports Accelerometer", SystemInfo.supportsAccelerometer.ToString()));
                card.Add(CreateRow("Supports Gyroscope", SystemInfo.supportsGyroscope.ToString()));
                card.Add(CreateRow("Supports Vibration", SystemInfo.supportsVibration.ToString()));
                card.Add(CreateRow("Genuine", Application.genuine.ToString()));
                card.Add(CreateRow("Genuine Check Available", Application.genuineCheckAvailable.ToString()));
                root.Add(section);
            }

            private string GetBatteryLevelString(float batteryLevel)
            {
                if (batteryLevel < 0f)
                {
                    return "Unavailable";
                }

                return batteryLevel.ToString("P0");
            }
        }
    }
}
