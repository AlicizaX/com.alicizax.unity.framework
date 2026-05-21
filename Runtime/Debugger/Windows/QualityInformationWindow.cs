using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class QualityInformationWindow : PollingDebuggerWindowBase
        {
            private bool m_ApplyExpensiveChanges;
            private Toggle m_applyExpensiveToggle;
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement qualitySection = CreateSection("Quality Level", out VisualElement qualityCard);
                qualityCard.Add(CreateRow("Current Quality Level", QualitySettings.names[QualitySettings.GetQualityLevel()]));
                m_applyExpensiveToggle = ScrollableDebuggerWindowBase.CreateConsoleFilterToggle("Apply expensive changes on quality level change.", m_ApplyExpensiveChanges, DebuggerTheme.PrimaryText, value => m_ApplyExpensiveChanges = value);
                qualityCard.Add(m_applyExpensiveToggle);
                VisualElement qualityButtons = CreateToolbarRow();
                string[] names = QualitySettings.names;
                for (int i = 0; i < names.Length; i++)
                {
                    int qualityIndex = i;
                    qualityButtons.Add(CreateActionButton(names[i], () => QualitySettings.SetQualityLevel(qualityIndex, m_ApplyExpensiveChanges),
                        QualitySettings.GetQualityLevel() == qualityIndex ? DebuggerTheme.ButtonSurfaceActive : DebuggerTheme.ButtonSurface));
                }

                qualityCard.Add(qualityButtons);
                root.Add(qualitySection);

                VisualElement renderingSection = CreateSection("Rendering Information", out VisualElement renderingCard);
                renderingCard.Add(CreateRow("Active Color Space", QualitySettings.activeColorSpace.ToString()));
                renderingCard.Add(CreateRow("Desired Color Space", QualitySettings.desiredColorSpace.ToString()));
                renderingCard.Add(CreateRow("Max Queued Frames", QualitySettings.maxQueuedFrames.ToString()));
                renderingCard.Add(CreateRow("Pixel Light Count", QualitySettings.pixelLightCount.ToString()));
                renderingCard.Add(CreateRow("Master Texture Limit", QualitySettings.globalTextureMipmapLimit.ToString()));
                renderingCard.Add(CreateRow("Anisotropic Filtering", QualitySettings.anisotropicFiltering.ToString()));
                renderingCard.Add(CreateRow("Anti Aliasing", QualitySettings.antiAliasing.ToString()));
                renderingCard.Add(CreateRow("Realtime Reflection Probes", QualitySettings.realtimeReflectionProbes.ToString()));
                renderingCard.Add(CreateRow("Billboards Face Camera Position", QualitySettings.billboardsFaceCameraPosition.ToString()));
                root.Add(renderingSection);

                VisualElement shadowSection = CreateSection("Shadows Information", out VisualElement shadowCard);
                shadowCard.Add(CreateRow("Shadowmask Mode", QualitySettings.shadowmaskMode.ToString()));
                shadowCard.Add(CreateRow("Shadow Quality", QualitySettings.shadows.ToString()));
                shadowCard.Add(CreateRow("Shadow Resolution", QualitySettings.shadowResolution.ToString()));
                shadowCard.Add(CreateRow("Shadow Projection", QualitySettings.shadowProjection.ToString()));
                shadowCard.Add(CreateRow("Shadow Distance", QualitySettings.shadowDistance.ToString()));
                shadowCard.Add(CreateRow("Shadow Near Plane Offset", QualitySettings.shadowNearPlaneOffset.ToString()));
                shadowCard.Add(CreateRow("Shadow Cascades", QualitySettings.shadowCascades.ToString()));
                shadowCard.Add(CreateRow("Shadow Cascade 2 Split", QualitySettings.shadowCascade2Split.ToString()));
                shadowCard.Add(CreateRow("Shadow Cascade 4 Split", QualitySettings.shadowCascade4Split.ToString()));
                root.Add(shadowSection);

                VisualElement otherSection = CreateSection("Other Information", out VisualElement otherCard);
                otherCard.Add(CreateRow("Skin Weights", QualitySettings.skinWeights.ToString()));
                otherCard.Add(CreateRow("VSync Count", QualitySettings.vSyncCount.ToString()));
                otherCard.Add(CreateRow("LOD Bias", QualitySettings.lodBias.ToString()));
                otherCard.Add(CreateRow("Maximum LOD Level", QualitySettings.maximumLODLevel.ToString()));
                otherCard.Add(CreateRow("Particle Raycast Budget", QualitySettings.particleRaycastBudget.ToString()));
                otherCard.Add(CreateRow("Async Upload Time Slice", Utility.Text.Format("{0} ms", QualitySettings.asyncUploadTimeSlice)));
                otherCard.Add(CreateRow("Async Upload Buffer Size", Utility.Text.Format("{0} MB", QualitySettings.asyncUploadBufferSize)));
                otherCard.Add(CreateRow("Async Upload Persistent Buffer", QualitySettings.asyncUploadPersistentBuffer.ToString()));
                root.Add(otherSection);
            }
        }
    }
}
