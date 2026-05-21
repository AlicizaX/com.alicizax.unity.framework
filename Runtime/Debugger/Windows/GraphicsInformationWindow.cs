using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class GraphicsInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Graphics Information", out VisualElement card);
                card.Add(CreateRow("Device ID", SystemInfo.graphicsDeviceID.ToString()));
                card.Add(CreateRow("Device Name", SystemInfo.graphicsDeviceName));
                card.Add(CreateRow("Device Vendor ID", SystemInfo.graphicsDeviceVendorID.ToString()));
                card.Add(CreateRow("Device Vendor", SystemInfo.graphicsDeviceVendor));
                card.Add(CreateRow("Device Type", SystemInfo.graphicsDeviceType.ToString()));
                card.Add(CreateRow("Device Version", SystemInfo.graphicsDeviceVersion));
                card.Add(CreateRow("Memory Size", Utility.Text.Format("{0} MB", SystemInfo.graphicsMemorySize)));
                card.Add(CreateRow("Multi Threaded", SystemInfo.graphicsMultiThreaded.ToString()));
                card.Add(CreateRow("Rendering Threading Mode", SystemInfo.renderingThreadingMode.ToString()));
                card.Add(CreateRow("HDR Display Support Flags", SystemInfo.hdrDisplaySupportFlags.ToString()));
                card.Add(CreateRow("Shader Level", GetShaderLevelString(SystemInfo.graphicsShaderLevel)));
                card.Add(CreateRow("Global Maximum LOD", Shader.globalMaximumLOD.ToString()));
                card.Add(CreateRow("Global Render Pipeline", Shader.globalRenderPipeline));
                card.Add(CreateRow("Min OpenGLES Version", Graphics.minOpenGLESVersion.ToString()));
                card.Add(CreateRow("Active Tier", Graphics.activeTier.ToString()));
                card.Add(CreateRow("Active Color Gamut", Graphics.activeColorGamut.ToString()));
                card.Add(CreateRow("Preserve Frame Buffer Alpha", Graphics.preserveFramebufferAlpha.ToString()));
                card.Add(CreateRow("NPOT Support", SystemInfo.npotSupport.ToString()));
                card.Add(CreateRow("Max Texture Size", SystemInfo.maxTextureSize.ToString()));
                card.Add(CreateRow("Supported Render Target Count", SystemInfo.supportedRenderTargetCount.ToString()));
                card.Add(CreateRow("Supported Random Write Target Count", SystemInfo.supportedRandomWriteTargetCount.ToString()));
                card.Add(CreateRow("Copy Texture Support", SystemInfo.copyTextureSupport.ToString()));
                card.Add(CreateRow("Uses Reversed ZBuffer", SystemInfo.usesReversedZBuffer.ToString()));
                card.Add(CreateRow("Supports Sparse Textures", SystemInfo.supportsSparseTextures.ToString()));
                card.Add(CreateRow("Supports 3D Textures", SystemInfo.supports3DTextures.ToString()));
                card.Add(CreateRow("Supports Shadows", SystemInfo.supportsShadows.ToString()));
                card.Add(CreateRow("Supports Compute Shader", SystemInfo.supportsComputeShaders.ToString()));
                card.Add(CreateRow("Supports Instancing", SystemInfo.supportsInstancing.ToString()));
                card.Add(CreateRow("Supports Async GPU Readback", SystemInfo.supportsAsyncGPUReadback.ToString()));
                card.Add(CreateRow("Supports Geometry Shaders", SystemInfo.supportsGeometryShaders.ToString()));
                card.Add(CreateRow("Supports Ray Tracing", SystemInfo.supportsRayTracing.ToString()));
                card.Add(CreateRow("Supports Tessellation Shaders", SystemInfo.supportsTessellationShaders.ToString()));
                root.Add(section);
            }

            private string GetShaderLevelString(int shaderLevel)
            {
                return Utility.Text.Format("Shader Model {0}.{1}", shaderLevel / 10, shaderLevel % 10);
            }
        }
    }
}
