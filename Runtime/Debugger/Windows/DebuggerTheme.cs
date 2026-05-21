using UnityEngine;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private static class DebuggerTheme
        {
            public static readonly Color Background = new Color32(12, 13, 17, 240);
            public static readonly Color OverlayBackground = new Color32(9, 10, 14, 225);
            public static readonly Color SidebarBackground = new Color32(15, 17, 22, 255);
            public static readonly Color PanelSurface = new Color32(20, 22, 28, 255);
            public static readonly Color PanelSurfaceAlt = new Color32(25, 28, 36, 255);
            public static readonly Color PanelSurfaceRaised = new Color32(30, 34, 43, 255);
            public static readonly Color PanelSurfaceStrong = new Color32(14, 16, 21, 255);
            public static readonly Color ButtonSurface = new Color32(32, 36, 46, 255);
            public static readonly Color ButtonSurfaceHover = new Color32(42, 48, 60, 255);
            public static readonly Color ButtonSurfacePressed = new Color32(26, 30, 38, 255);
            public static readonly Color ButtonSurfaceActive = new Color32(55, 75, 112, 255);
            public static readonly Color ButtonSurfaceActiveHover = new Color32(65, 90, 135, 255);
            public static readonly Color ButtonSurfaceActivePressed = new Color32(46, 64, 98, 255);
            public static readonly Color GhostHover = new Color32(255, 255, 255, 10);
            public static readonly Color GhostPressed = new Color32(255, 255, 255, 18);
            public static readonly Color ToggleSurface = new Color32(24, 27, 35, 255);
            public static readonly Color ToggleSurfaceHover = new Color32(32, 37, 48, 255);
            public static readonly Color ToggleSurfacePressed = new Color32(20, 23, 30, 255);
            public static readonly Color ToggleSurfaceActive = new Color32(38, 55, 85, 255);
            public static readonly Color ToggleSurfaceActiveHover = new Color32(46, 68, 105, 255);
            public static readonly Color ToggleSurfaceActivePressed = new Color32(32, 47, 74, 255);
            public static readonly Color SidebarRow = new Color32(255, 255, 255, 0);
            public static readonly Color SidebarRowHover = new Color32(255, 255, 255, 8);
            public static readonly Color SidebarRowPressed = new Color32(255, 255, 255, 14);
            public static readonly Color SidebarRowSelected = new Color32(40, 58, 90, 255);
            public static readonly Color SidebarRowSelectedHover = new Color32(48, 70, 108, 255);
            public static readonly Color SidebarRowSelectedPressed = new Color32(34, 50, 78, 255);
            public static readonly Color ScrollbarTrack = new Color32(12, 13, 17, 180);
            public static readonly Color ScrollbarThumb = new Color32(55, 65, 82, 255);
            public static readonly Color ScrollbarThumbHover = new Color32(75, 90, 112, 255);
            public static readonly Color ScrollbarThumbPressed = new Color32(95, 115, 145, 255);
            public static readonly Color SelectionFill = new Color32(38, 54, 82, 255);
            public static readonly Color SelectionBorder = new Color32(88, 120, 180, 255);
            public static readonly Color Border = new Color32(36, 40, 50, 255);
            public static readonly Color PrimaryText = new Color32(230, 234, 240, 255);
            public static readonly Color SecondaryText = new Color32(128, 138, 158, 255);
            public static readonly Color Positive = new Color32(110, 190, 135, 255);
            public static readonly Color Warning = new Color32(225, 180, 75, 255);
            public static readonly Color Danger = new Color32(210, 85, 85, 255);
            public static readonly Color Accent = new Color32(82, 130, 210, 255);
            public static readonly Color Fatal = new Color32(160, 68, 68, 255);
        }
    }
}
