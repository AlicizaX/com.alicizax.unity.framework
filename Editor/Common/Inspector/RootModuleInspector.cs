using System;
using System.Collections.Generic;
using AlicizaX;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Editor
{
    [CustomEditor(typeof(RootModule))]
    internal sealed class RootModuleInspector : GameFrameworkInspector
    {
        private const float ToolbarHeight = 28f;
        private const float LabelWidth = 112f;
        private const string EnableLogSymbol = "ENABLE_LOG";
        private const string EnableInfoLogSymbol = "ENABLE_INFO_LOG";
        private const string EnableWarningLogSymbol = "ENABLE_WARNING_LOG";
        private const string EnableErrorLogSymbol = "ENABLE_ERROR_LOG";
        private const string EnableAssertLogSymbol = "ENABLE_ASSERT_LOG";
        private const string EnableExceptionLogSymbol = "ENABLE_EXCEPTION_LOG";

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

        private SerializedProperty _frameRate = null;
        private SerializedProperty _runInBackground = null;
        private SerializedProperty _neverSleep = null;
        private LogDefineSelection _logDefineSelection;
        private GUIStyle _panelStyle;
        private GUIStyle _fieldRowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;

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

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();
            EnsureStyles();

            RootModule t = (RootModule)target;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(_panelStyle);
            {
                DrawToolbar();
                DrawFrameRate(t);
                DrawToggle(_runInBackground, "Run In Background", value => t.RunInBackground = value);
                DrawToggle(_neverSleep, "Never Sleep", value => t.NeverSleep = value);
                DrawLogDefines();
            }
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private void OnEnable()
        {
            _frameRate = serializedObject.FindProperty("frameRate");
            _runInBackground = serializedObject.FindProperty("runInBackground");
            _neverSleep = serializedObject.FindProperty("neverSleep");
            _logDefineSelection = ReadLogDefineSelection();
        }

        private void DrawToolbar()
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect titleRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 4f, toolbarRect.width - 16f, 20f);
            GUI.Label(titleRect, "Runtime Settings", _titleStyle);
        }

        private void DrawFrameRate(RootModule rootModule)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            {
                EditorGUILayout.LabelField("Framerate", _labelStyle, GUILayout.Width(LabelWidth));

                EditorGUI.BeginChangeCheck();
                int frameRate = EditorGUILayout.IntSlider(_frameRate.intValue, 1, 120);
                if (EditorGUI.EndChangeCheck())
                {
                    if (EditorApplication.isPlaying)
                    {
                        rootModule.FrameRate = frameRate;
                    }
                    else
                    {
                        _frameRate.intValue = frameRate;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToggle(SerializedProperty property, string label, Action<bool> applyRuntimeValue)
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            {
                EditorGUILayout.LabelField(label, _labelStyle, GUILayout.Width(LabelWidth));

                EditorGUI.BeginChangeCheck();
                bool value = EditorGUILayout.Toggle(property.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    if (EditorApplication.isPlaying)
                    {
                        applyRuntimeValue(value);
                    }
                    else
                    {
                        property.boolValue = value;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLogDefines()
        {
            EditorGUILayout.BeginHorizontal(_fieldRowStyle);
            {
                EditorGUILayout.LabelField("Log Defines", _labelStyle, GUILayout.Width(LabelWidth));

                EditorGUI.BeginChangeCheck();
                var nextSelection = (LogDefineSelection)EditorGUILayout.EnumFlagsField(_logDefineSelection);
                nextSelection &= AllLogDefineSelection;
                if (EditorGUI.EndChangeCheck())
                {
                    _logDefineSelection = nextSelection;
                    ApplyLogDefineSelection(_logDefineSelection);
                }
            }
            EditorGUILayout.EndHorizontal();
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

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelStyle = AlicizaEditorGUI.Styles.Panel;
            _fieldRowStyle = AlicizaEditorGUI.Styles.FieldRow;
            _labelStyle = AlicizaEditorGUI.Styles.FieldLabel;
            _titleStyle = new GUIStyle(AlicizaEditorGUI.Styles.RowLabel)
            {
                fontStyle = FontStyle.Bold
            };
        }
    }
}
