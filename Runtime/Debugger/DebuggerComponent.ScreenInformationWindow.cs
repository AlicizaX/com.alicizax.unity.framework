using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class ScreenInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Screen Information", out VisualElement card);
                card.Add(CreateRow("Current Resolution", GetResolutionString(Screen.currentResolution)));
                card.Add(CreateRow("Screen Width", Utility.Text.Format("{0} px / {1:F2} in / {2:F2} cm", Screen.width, Utility.Converter.GetInchesFromPixels(Screen.width), Utility.Converter.GetCentimetersFromPixels(Screen.width))));
                card.Add(CreateRow("Screen Height", Utility.Text.Format("{0} px / {1:F2} in / {2:F2} cm", Screen.height, Utility.Converter.GetInchesFromPixels(Screen.height), Utility.Converter.GetCentimetersFromPixels(Screen.height))));
                card.Add(CreateRow("Screen DPI", Screen.dpi.ToString("F2")));
                card.Add(CreateRow("Screen Orientation", Screen.orientation.ToString()));
                card.Add(CreateRow("Is Full Screen", Screen.fullScreen.ToString()));
#if UNITY_2018_1_OR_NEWER
                card.Add(CreateRow("Full Screen Mode", Screen.fullScreenMode.ToString()));
#endif
                card.Add(CreateRow("Sleep Timeout", GetSleepTimeoutDescription(Screen.sleepTimeout)));
#if UNITY_2019_2_OR_NEWER
                card.Add(CreateRow("Brightness", Screen.brightness.ToString("F2")));
#endif
                card.Add(CreateRow("Cursor Visible", UnityEngine.Cursor.visible.ToString()));
                card.Add(CreateRow("Cursor Lock State", UnityEngine.Cursor.lockState.ToString()));
                card.Add(CreateRow("Auto Landscape Left", Screen.autorotateToLandscapeLeft.ToString()));
                card.Add(CreateRow("Auto Landscape Right", Screen.autorotateToLandscapeRight.ToString()));
                card.Add(CreateRow("Auto Portrait", Screen.autorotateToPortrait.ToString()));
                card.Add(CreateRow("Auto Portrait Upside Down", Screen.autorotateToPortraitUpsideDown.ToString()));
#if UNITY_2017_2_OR_NEWER && !UNITY_2017_2_0
                card.Add(CreateRow("Safe Area", Screen.safeArea.ToString()));
#endif
#if UNITY_2019_2_OR_NEWER
                card.Add(CreateRow("Cutouts", GetCutoutsString(Screen.cutouts)));
#endif
                card.Add(CreateRow("Support Resolutions", GetResolutionsString(Screen.resolutions)));
                root.Add(section);
            }

            private string GetSleepTimeoutDescription(int sleepTimeout)
            {
                if (sleepTimeout == SleepTimeout.NeverSleep)
                {
                    return "Never Sleep";
                }

                if (sleepTimeout == SleepTimeout.SystemSetting)
                {
                    return "System Setting";
                }

                return sleepTimeout.ToString();
            }

            private string GetResolutionString(Resolution resolution)
            {
                return Utility.Text.Format("{0} x {1} @ {2}Hz", resolution.width, resolution.height, resolution.refreshRateRatio);
            }

            private string GetCutoutsString(Rect[] cutouts)
            {
                string[] cutoutStrings = new string[cutouts.Length];
                for (int i = 0; i < cutouts.Length; i++)
                {
                    cutoutStrings[i] = cutouts[i].ToString();
                }

                return string.Join("; ", cutoutStrings);
            }

            private string GetResolutionsString(Resolution[] resolutions)
            {
                string[] resolutionStrings = new string[resolutions.Length];
                for (int i = 0; i < resolutions.Length; i++)
                {
                    resolutionStrings[i] = GetResolutionString(resolutions[i]);
                }

                return string.Join("; ", resolutionStrings);
            }
        }
    }
}
