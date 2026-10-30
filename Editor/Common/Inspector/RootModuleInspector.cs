using System;
using System.Collections.Generic;
using AlicizaX;
using AlicizaX.Editor.UIToolkit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Editor
{

    [CustomEditor(typeof(RootModule))]
    internal sealed class RootModuleInspector : AlicizaUIEditor
    {
        private const int MinFrameRate = 1;
        private const int MaxFrameRate = 120;
        private const int RuntimeRefreshIntervalMs = 250;
        private const string UnavailableText = "Unavailable.";

        private const string EnableLogSymbol = "ENABLE_LOG";
        private const string EnableInfoLogSymbol = "ENABLE_INFO_LOG";
        private const string EnableWarningLogSymbol = "ENABLE_WARNING_LOG";
        private const string EnableErrorLogSymbol = "ENABLE_ERROR_LOG";
        private const string EnableAssertLogSymbol = "ENABLE_ASSERT_LOG";
        private const string EnableExceptionLogSymbol = "ENABLE_EXCEPTION_LOG";

        [Flags]
        private enum LogDefineSelection
        {
            None = 0,
            Info = 1 << 0,
            Warning = 1 << 1,
            Error = 1 << 2,
            Assert = 1 << 3,
            Exception = 1 << 4
        }

        private const LogDefineSelection AllLogDefineSelection = LogDefineSelection.Info |
                                                                 LogDefineSelection.Warning |
                                                                 LogDefineSelection.Error |
                                                                 LogDefineSelection.Assert |
                                                                 LogDefineSelection.Exception;

        private static readonly string[] ManagedLogSymbols =
        {
            EnableLogSymbol,
            EnableInfoLogSymbol,
            EnableWarningLogSymbol,
            EnableErrorLogSymbol,
            EnableAssertLogSymbol,
            EnableExceptionLogSymbol,
            "ENABLE_DEBUG_LOG",
            "ENABLE_DEBUG_AND_ABOVE_LOG",
            "ENABLE_INFO_AND_ABOVE_LOG",
            "ENABLE_WARNING_AND_ABOVE_LOG",
            "ENABLE_ERROR_AND_ABOVE_LOG",
            "ENABLE_FATAL_LOG",
            "ENABLE_FATAL_AND_ABOVE_LOG"
        };

        private static readonly (string Label, LogDefineSelection Flag, string Symbol)[] LogDefineEntries =
        {
            ("Info", LogDefineSelection.Info, EnableInfoLogSymbol),
            ("Warning", LogDefineSelection.Warning, EnableWarningLogSymbol),
            ("Error", LogDefineSelection.Error, EnableErrorLogSymbol),
            ("Assert", LogDefineSelection.Assert, EnableAssertLogSymbol),
            ("Exception", LogDefineSelection.Exception, EnableExceptionLogSymbol)
        };

        private SerializedProperty _frameRate = null;
        private SerializedProperty _runInBackground = null;
        private SerializedProperty _neverSleep = null;
        private AlicizaIntSliderField _frameRateField = null;
        private AlicizaFilterChip[] _logDefineChips = Array.Empty<AlicizaFilterChip>();
        private VisualElement _runtimeRoot = null;
        private LogDefineSelection _logDefineSelection = LogDefineSelection.None;

        private void OnEnable()
        {
            _frameRate = serializedObject.FindProperty("frameRate");
            _runInBackground = serializedObject.FindProperty("runInBackground");
            _neverSleep = serializedObject.FindProperty("neverSleep");
            _logDefineSelection = ReadLogDefineSelection();
        }

        protected override void BuildInspector(VisualElement root)
        {
            RootModule rootModule = target as RootModule;

            AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
            root.Add(panel);

            BuildRuntimeSettings(panel.Body, rootModule);

            _runtimeRoot = new VisualElement { name = "alicia-root-module-runtime" };
            panel.Body.Add(_runtimeRoot);

            RefreshRuntimeStatus();
            AlicizaEditorUI.RefreshEvery(root, RefreshRuntimeStatus, RuntimeRefreshIntervalMs);
        }

        private void BuildRuntimeSettings(VisualElement parent, RootModule rootModule)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Runtime Settings");
            parent.Add(section);

            if (rootModule == null)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("Root module is unavailable."));
                return;
            }

            if (_frameRate == null && _runInBackground == null && _neverSleep == null)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("Root module settings are unavailable."));
                return;
            }

            BuildFrameRateRow(section.Body);
            AddToggleRow(section.Body, "Run In Background", _runInBackground, value =>
            {
                rootModule.RunInBackground = value;
            });
            AddToggleRow(section.Body, "Never Sleep", _neverSleep, value =>
            {
                rootModule.NeverSleep = value;
            });
            BuildLogDefines(section.Body);
        }

        private void BuildFrameRateRow(VisualElement parent)
        {
            if (_frameRate == null)
            {
                AlicizaEditorUI.AddRow(parent, "Framerate", AlicizaEditorUI.CreateHint(UnavailableText));
                return;
            }

            var field = new AlicizaIntSliderField(MinFrameRate, MaxFrameRate);
            field.SetValueWithoutNotify(Mathf.Clamp(_frameRate.intValue, MinFrameRate, MaxFrameRate));
            field.RegisterValueChangedCallback(evt => ApplyFrameRate(evt.newValue));
            _frameRateField = field;
            AlicizaEditorUI.AddRow(parent, "Framerate", field);
        }

        private void ApplyFrameRate(int value)
        {
            int clamped = Mathf.Clamp(value, MinFrameRate, MaxFrameRate);
            RootModule rootModule = target as RootModule;

            if (EditorApplication.isPlaying && rootModule != null)
            {
                rootModule.FrameRate = clamped;
            }
            else if (_frameRate != null)
            {
                _frameRate.intValue = clamped;
                _frameRate.serializedObject.ApplyModifiedProperties();
            }

            if (_frameRateField != null && _frameRateField.value != clamped)
            {
                _frameRateField.SetValueWithoutNotify(clamped);
            }
        }

        private static void AddToggleRow(
            VisualElement parent,
            string caption,
            SerializedProperty property,
            Action<bool> applyRuntimeValue)
        {
            if (property == null)
            {
                AlicizaEditorUI.AddRow(parent, caption, AlicizaEditorUI.CreateHint(UnavailableText));
                return;
            }

            var toggle = new AlicizaPillToggle();
            toggle.SetValueWithoutNotify(property.boolValue);
            toggle.RegisterValueChangedCallback(evt => ApplyToggle(property, evt.newValue, applyRuntimeValue));
            AlicizaEditorUI.AddRow(parent, caption, toggle);
        }

        private static void ApplyToggle(
            SerializedProperty property,
            bool value,
            Action<bool> applyRuntimeValue)
        {
            if (property == null)
            {
                return;
            }

            if (EditorApplication.isPlaying && applyRuntimeValue != null)
            {
                applyRuntimeValue(value);
                return;
            }

            property.boolValue = value;
            property.serializedObject.ApplyModifiedProperties();
        }

        private void BuildLogDefines(VisualElement parent)
        {
            var host = new VisualElement { name = "alicia-root-module-log-defines" };
            host.style.flexDirection = FlexDirection.Row;
            host.style.flexWrap = Wrap.Wrap;
            host.style.alignItems = Align.Center;
            host.style.flexGrow = 1f;
            host.style.flexShrink = 1f;
            host.style.minWidth = 0f;

            _logDefineChips = new AlicizaFilterChip[LogDefineEntries.Length];
            for (int i = 0; i < LogDefineEntries.Length; i++)
            {
                (string label, LogDefineSelection flag, string symbol) = LogDefineEntries[i];
                var chip = new AlicizaFilterChip(label)
                {
                    tooltip = "Toggle " + symbol + " on every build target."
                };
                chip.SetSelected((_logDefineSelection & flag) != 0);
                LogDefineSelection capturedFlag = flag;
                chip.clicked += () => ToggleLogDefine(capturedFlag);
                _logDefineChips[i] = chip;
                host.Add(chip);
            }

            AlicizaEditorUI.AddRow(parent, "Log Defines", host);

            if (HasLogSymbol(EnableLogSymbol))
            {
                parent.Add(AlicizaEditorUI.CreateHint(
                    "ENABLE_LOG currently enables every channel; changing a channel replaces it with per-channel defines."));
            }

            parent.Add(AlicizaEditorUI.CreateHint(
                "Applies to Standalone, iOS, Android, WSA and WebGL, and triggers a script recompile."));
        }

        private void ToggleLogDefine(LogDefineSelection flag)
        {
            _logDefineSelection = (_logDefineSelection ^ flag) & AllLogDefineSelection;
            ApplyLogDefineSelection(_logDefineSelection);
            _logDefineSelection = ReadLogDefineSelection();
            SyncLogDefineChips();
        }

        private void SyncLogDefineChips()
        {
            for (int i = 0; i < _logDefineChips.Length && i < LogDefineEntries.Length; i++)
            {
                AlicizaFilterChip chip = _logDefineChips[i];
                if (chip != null)
                {
                    chip.SetSelected((_logDefineSelection & LogDefineEntries[i].Flag) != 0);
                }
            }
        }

        private void RefreshRuntimeStatus()
        {
            if (_runtimeRoot == null)
            {
                return;
            }

            _runtimeRoot.Clear();

            RootModule rootModule = target as RootModule;
            if (rootModule == null)
            {
                _runtimeRoot.Add(AlicizaEditorUI.CreateEmptyState("Root module is unavailable."));
                return;
            }

            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Runtime Status");
            _runtimeRoot.Add(section);

            bool playing = EditorApplication.isPlaying;
            AlicizaEditorUI.AddRow(
                section.Body,
                "Play Mode",
                AlicizaEditorUI.CreateBadge(
                    playing ? "Playing" : "Edit Mode",
                    playing ? AlicizaValueTone.Success : AlicizaValueTone.Muted));

            if (!playing)
            {
                section.Body.Add(AlicizaEditorUI.CreateHint("Live runtime values are available in Play mode."));
                return;
            }

            int targetFrameRate = Application.targetFrameRate;
            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Target Frame Rate",
                targetFrameRate.ToString(),
                targetFrameRate == rootModule.FrameRate ? AlicizaValueTone.Default : AlicizaValueTone.Warning);

            AlicizaEditorUI.AddValueRow(
                section.Body,
                "Game Speed",
                rootModule.GameSpeed.ToString("0.###"),
                rootModule.IsGamePaused ? AlicizaValueTone.Warning : AlicizaValueTone.Default);

            string gameState;
            AlicizaValueTone gameStateTone;
            if (rootModule.IsGamePaused)
            {
                gameState = "Paused";
                gameStateTone = AlicizaValueTone.Warning;
            }
            else if (rootModule.IsNormalGameSpeed)
            {
                gameState = "Normal";
                gameStateTone = AlicizaValueTone.Success;
            }
            else
            {
                gameState = "Custom";
                gameStateTone = AlicizaValueTone.Accent;
            }

            AlicizaEditorUI.AddRow(
                section.Body,
                "Game State",
                AlicizaEditorUI.CreateBadge(gameState, gameStateTone));
        }

        private static LogDefineSelection ReadLogDefineSelection()
        {
            if (HasLogSymbol(EnableLogSymbol))
            {
                return AllLogDefineSelection;
            }

            LogDefineSelection current = LogDefineSelection.None;
            if (HasLogSymbol(EnableInfoLogSymbol))
            {
                current |= LogDefineSelection.Info;
            }

            if (HasLogSymbol(EnableWarningLogSymbol))
            {
                current |= LogDefineSelection.Warning;
            }

            if (HasLogSymbol(EnableErrorLogSymbol))
            {
                current |= LogDefineSelection.Error;
            }

            if (HasLogSymbol(EnableAssertLogSymbol))
            {
                current |= LogDefineSelection.Assert;
            }

            if (HasLogSymbol(EnableExceptionLogSymbol))
            {
                current |= LogDefineSelection.Exception;
            }

            return current;
        }

        private static bool HasLogSymbol(string symbol)
        {
            foreach (BuildTargetGroup buildTargetGroup in GetBuildTargetGroups())
            {
                if (ScriptingDefineSymbols.HasScriptingDefineSymbol(buildTargetGroup, symbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyLogDefineSelection(LogDefineSelection selection)
        {
            selection &= AllLogDefineSelection;

            foreach (BuildTargetGroup buildTargetGroup in GetBuildTargetGroups())
            {
                List<string> defines = new List<string>(ScriptingDefineSymbols.GetScriptingDefineSymbols(buildTargetGroup));
                foreach (string symbol in ManagedLogSymbols)
                {
                    defines.RemoveAll(item => item == symbol);
                }

                AddSelectedLogSymbol(defines, selection, LogDefineSelection.Info, EnableInfoLogSymbol);
                AddSelectedLogSymbol(defines, selection, LogDefineSelection.Warning, EnableWarningLogSymbol);
                AddSelectedLogSymbol(defines, selection, LogDefineSelection.Error, EnableErrorLogSymbol);
                AddSelectedLogSymbol(defines, selection, LogDefineSelection.Assert, EnableAssertLogSymbol);
                AddSelectedLogSymbol(defines, selection, LogDefineSelection.Exception, EnableExceptionLogSymbol);

                ScriptingDefineSymbols.SetScriptingDefineSymbols(buildTargetGroup, defines.ToArray());
            }
        }

        private static void AddSelectedLogSymbol(
            List<string> defines,
            LogDefineSelection selection,
            LogDefineSelection flag,
            string symbol)
        {
            if ((selection & flag) != 0 && !defines.Contains(symbol))
            {
                defines.Add(symbol);
            }
        }

        private static BuildTargetGroup[] GetBuildTargetGroups()
        {
            return new[]
            {
                BuildTargetGroup.Standalone,
                BuildTargetGroup.iOS,
                BuildTargetGroup.Android,
                BuildTargetGroup.WSA,
                BuildTargetGroup.WebGL
            };
        }
    }
}
