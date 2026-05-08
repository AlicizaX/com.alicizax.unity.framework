using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class InputSummaryInformationWindow : PollingDebuggerWindowBase
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
            }
        }
    }
}
