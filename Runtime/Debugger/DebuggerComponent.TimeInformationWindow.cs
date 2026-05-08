using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class TimeInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Time Information", out VisualElement card);
                card.Add(CreateRow("Time Scale", Utility.Text.Format("{0} [{1}]", Time.timeScale, GetTimeScaleDescription(Time.timeScale))));
                card.Add(CreateRow("Realtime Since Startup", Time.realtimeSinceStartup.ToString()));
                card.Add(CreateRow("Time Since Level Load", Time.timeSinceLevelLoad.ToString()));
                card.Add(CreateRow("Time", Time.time.ToString()));
                card.Add(CreateRow("Fixed Time", Time.fixedTime.ToString()));
                card.Add(CreateRow("Unscaled Time", Time.unscaledTime.ToString()));
#if UNITY_5_6_OR_NEWER
                card.Add(CreateRow("Fixed Unscaled Time", Time.fixedUnscaledTime.ToString()));
#endif
                card.Add(CreateRow("Delta Time", Time.deltaTime.ToString()));
                card.Add(CreateRow("Fixed Delta Time", Time.fixedDeltaTime.ToString()));
                card.Add(CreateRow("Unscaled Delta Time", Time.unscaledDeltaTime.ToString()));
#if UNITY_5_6_OR_NEWER
                card.Add(CreateRow("Fixed Unscaled Delta Time", Time.fixedUnscaledDeltaTime.ToString()));
#endif
                card.Add(CreateRow("Smooth Delta Time", Time.smoothDeltaTime.ToString()));
                card.Add(CreateRow("Maximum Delta Time", Time.maximumDeltaTime.ToString()));
#if UNITY_5_5_OR_NEWER
                card.Add(CreateRow("Maximum Particle Delta Time", Time.maximumParticleDeltaTime.ToString()));
#endif
                card.Add(CreateRow("Frame Count", Time.frameCount.ToString()));
                card.Add(CreateRow("Rendered Frame Count", Time.renderedFrameCount.ToString()));
                card.Add(CreateRow("Capture Framerate", Time.captureFramerate.ToString()));
#if UNITY_2019_2_OR_NEWER
                card.Add(CreateRow("Capture Delta Time", Time.captureDeltaTime.ToString()));
#endif
#if UNITY_5_6_OR_NEWER
                card.Add(CreateRow("In Fixed Time Step", Time.inFixedTimeStep.ToString()));
#endif
                root.Add(section);
            }

            private string GetTimeScaleDescription(float timeScale)
            {
                if (timeScale <= 0f)
                {
                    return "Pause";
                }

                if (timeScale < 1f)
                {
                    return "Slower";
                }

                if (timeScale > 1f)
                {
                    return "Faster";
                }

                return "Normal";
            }
        }
    }
}
