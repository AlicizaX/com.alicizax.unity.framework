using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class InputInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Input Summary", out VisualElement card);
                card.Add(CreateRow("Back Button Leaves App", Input.backButtonLeavesApp.ToString()));
                card.Add(CreateRow("Device Orientation", Input.deviceOrientation.ToString()));
                card.Add(CreateRow("Mouse Present", Input.mousePresent.ToString()));
                card.Add(CreateRow("Mouse Position", Input.mousePosition.ToString()));
                card.Add(CreateRow("Mouse Scroll Delta", Input.mouseScrollDelta.ToString()));
                card.Add(CreateRow("Any Key", Input.anyKey.ToString()));
                card.Add(CreateRow("Any Key Down", Input.anyKeyDown.ToString()));
                card.Add(CreateRow("Input String", Input.inputString));
                card.Add(CreateRow("IME Is Selected", Input.imeIsSelected.ToString()));
                card.Add(CreateRow("IME Composition Mode", Input.imeCompositionMode.ToString()));
                card.Add(CreateRow("Compensate Sensors", Input.compensateSensors.ToString()));
                card.Add(CreateRow("Composition Cursor Position", Input.compositionCursorPos.ToString()));
                card.Add(CreateRow("Composition String", Input.compositionString));
                root.Add(section);

                BuildTouchInformation(root);
                BuildLocationInformation(root);
                BuildAccelerationInformation(root);
                BuildGyroscopeInformation(root);
                BuildCompassInformation(root);
            }

            private void BuildTouchInformation(VisualElement root)
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

            private void BuildLocationInformation(VisualElement root)
            {
                VisualElement actions = CreateSection("Location Actions", out VisualElement actionCard);
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

            private void BuildAccelerationInformation(VisualElement root)
            {
                VisualElement section = CreateSection("Input Acceleration", out VisualElement card);
                card.Add(CreateRow("Acceleration", Input.acceleration.ToString()));
                card.Add(CreateRow("Acceleration Event Count", Input.accelerationEventCount.ToString()));
                card.Add(CreateRow("Acceleration Events", GetAccelerationEventsString(Input.accelerationEvents)));
                root.Add(section);
            }

            private void BuildGyroscopeInformation(VisualElement root)
            {
                VisualElement actions = CreateSection("Gyroscope Actions", out VisualElement actionCard);
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

            private void BuildCompassInformation(VisualElement root)
            {
                VisualElement actions = CreateSection("Compass Actions", out VisualElement actionCard);
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

            private string GetTouchString(Touch touch)
            {
                return Utility.Text.Format("{0}, {1}, {2}, {3}, {4}", touch.position, touch.deltaPosition, touch.rawPosition, touch.pressure, touch.phase);
            }

            private string GetTouchesString(Touch[] touches)
            {
                using (var builder = Cysharp.Text.ZString.CreateStringBuilder())
                {
                    for (int i = 0; i < touches.Length; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append("; ");
                        }

                        builder.Append(GetTouchString(touches[i]));
                    }

                    return builder.ToString();
                }
            }

            private string GetAccelerationEventString(AccelerationEvent accelerationEvent)
            {
                return Utility.Text.Format("{0}, {1}", accelerationEvent.acceleration, accelerationEvent.deltaTime);
            }

            private string GetAccelerationEventsString(AccelerationEvent[] accelerationEvents)
            {
                using (var builder = Cysharp.Text.ZString.CreateStringBuilder())
                {
                    for (int i = 0; i < accelerationEvents.Length; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append("; ");
                        }

                        builder.Append(GetAccelerationEventString(accelerationEvents[i]));
                    }

                    return builder.ToString();
                }
            }
        }
    }
}
