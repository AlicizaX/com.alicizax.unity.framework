using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Editor
{
    public sealed class FrameworkInstallerWindow : EditorWindow
    {
        private const string MenuPath = "AlicizaX/Installer";
        private const string InstallerPath = "Packages/com.alicizax.unity.framework/Editor/Installer";
        private const string NormalTemplateName = "NormalTemplate";
        private const string HybridTemplateName = "HybridTemplate";
        private const string TemplatesPath = InstallerPath + "/Templates~";
        private const string NormalTemplatePath = TemplatesPath + "/" + NormalTemplateName;
        private const string HybridTemplatePath = TemplatesPath + "/" + HybridTemplateName;
        private const string EnableLogSymbol = "ENABLE_LOG";
        private const string EnableHybridClrSymbol = "ENABLE_HYBRIDCLR";
        private const string UrpPackageName = "com.unity.render-pipelines.universal";
        private const string HybridClrPackageName = "com.code-philosophy.hybridclr";
        private const string RequiredRegistryUrl = "https://package.openupm.com";
        private const string RequiredRegistryScopeCysharp = "com.cysharp";
        private const string RequiredRegistryScopeTuyooGame = "com.tuyoogame";
        private const string ManifestPath = "Packages/manifest.json";
        private const string InstallStatePath = "ProjectSettings/AlicizaXFrameworkInstaller.json";

        private static readonly string[] RuntimeAssetMarkers =
        {
            "Assets/Scripts/Startup",
            "Assets/Bundles",
            "Assets/YooAsset"
        };

        private static readonly string[] HybridAssetMarkers =
        {
            "Assets/Scripts/Hotfix",
            "Assets/HybridCLRGenerate",
            "Assets/Bundles/DLL"
        };

        private InstallCheckResult _checkResult;
        private TemplateType _selectedTemplate;
        private Vector2 _scrollPosition;

        private enum TemplateType
        {
            Normal,
            Hybrid
        }

        private enum ProjectInstallState
        {
            NotInstalled,
            Custom,
            NormalTemplate,
            HybridTemplate
        }

        [MenuItem(MenuPath, false, -3000)]
        private static void OpenWindow()
        {
            FrameworkInstallerWindow window = GetWindow<FrameworkInstallerWindow>();
            window.titleContent = new GUIContent("AlicizaX Installer", EditorUtils.Styles.Database.image);
            window.minSize = new Vector2(520f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            RunInstallCheck();
        }

        private void OnGUI()
        {
            EnsureCheckResult();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawHeader();
            DrawEnvironmentPanel();
            DrawTemplatePanel();
            DrawActionPanel();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            using (new EditorUtils.BoxGroupScope("AlicizaX Framework Installer", 26f))
            {
                EditorGUILayout.LabelField("Install state is checked when this window opens. Installed templates will not be overwritten.", AlicizaEditorGUI.Styles.MutedLabel);
            }
        }

        private void DrawEnvironmentPanel()
        {
            using (new EditorUtils.BoxGroupScope("Environment", 24f))
            {
                DrawStatusRow("Installer state", _checkResult.ProjectState != ProjectInstallState.NotInstalled, _checkResult.StateText, MessageType.Warning);
                DrawStatusRow("State source", true, _checkResult.StateSource);
                DrawStatusRow("Unity 2022.3 or newer", _checkResult.UnityVersionSupported, _checkResult.UnityVersion);
                DrawStatusRow("OpenUPM registry", _checkResult.HasRequiredScopedRegistry, _checkResult.RequiredScopedRegistryText);
                DrawStatusRow("URP package", _checkResult.HasUrp, _checkResult.UrpVersionText);
                DrawStatusRow("HybridCLR package", _checkResult.HasHybridClr, _checkResult.HybridClrVersionText, MessageType.Warning);
                DrawStatusRow("Normal template folder", _checkResult.HasNormalTemplate, NormalTemplatePath);
                DrawStatusRow("Hybrid template folder", _checkResult.HasHybridTemplate, HybridTemplatePath, MessageType.Warning);
            }
        }

        private void DrawTemplatePanel()
        {
            using (new EditorUtils.BoxGroupScope("Template", 24f))
            {
                if (_checkResult.ProjectState == ProjectInstallState.Custom)
                {
                    EditorUtils.TrHelpIconText("Project is marked as custom/no template required. Template import is disabled by persisted state.", MessageType.Info);
                    return;
                }

                if (_checkResult.ProjectState == ProjectInstallState.HybridTemplate)
                {
                    EditorUtils.TrHelpIconText("Hybrid template is already initialized. Installer is locked to avoid overwriting project files.", MessageType.Info);
                    return;
                }

                if (_checkResult.ProjectState == ProjectInstallState.NormalTemplate)
                {
                    EditorUtils.TrHelpIconText("Normal template is initialized. You can upgrade to Hybrid template after HybridCLR is installed.", MessageType.Info);
                    using (new EditorGUI.DisabledScope(!_checkResult.HasHybridClr || !_checkResult.HasHybridTemplate))
                    {
                        _selectedTemplate = TemplateType.Hybrid;
                        DrawTemplateChoice("Hybrid Template", true, "Upgrade current project to hot update template.");
                    }
                    return;
                }

                DrawTemplateChoice("Normal Template", _selectedTemplate == TemplateType.Normal, "Standalone framework template. Adds ENABLE_LOG.");

                using (new EditorGUI.DisabledScope(!_checkResult.HasHybridClr))
                {
                    bool selected = _selectedTemplate == TemplateType.Hybrid;
                    bool nextSelected = DrawTemplateChoice("Hybrid Template", selected, "Hot update framework template. Adds ENABLE_LOG and ENABLE_HYBRIDCLR.");
                    if (nextSelected && !selected)
                    {
                        _selectedTemplate = TemplateType.Hybrid;
                    }
                }

                if (!_checkResult.HasHybridClr)
                {
                    EditorUtils.TrHelpIconText("Hybrid template requires HybridCLR package.", MessageType.Warning);
                }
            }
        }

        private void DrawActionPanel()
        {
            using (new EditorUtils.BoxGroupScope("Actions", 24f))
            {
                bool canInstall = CanInstallSelectedTemplate(out string blockReason);

                if (!string.IsNullOrEmpty(blockReason))
                {
                    EditorUtils.TrHelpIconText(blockReason, MessageType.Error);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Refresh Check", AlicizaEditorGUI.Styles.InlineButton, GUILayout.Width(130f)))
                {
                    RunInstallCheck();
                }

                if (_checkResult.ProjectState == ProjectInstallState.NotInstalled)
                {
                    if (GUILayout.Button("Use Custom", AlicizaEditorGUI.Styles.InlineButton, GUILayout.Width(120f)))
                    {
                        SaveInstallState(ProjectInstallState.Custom);
                        RunInstallCheck();
                    }
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!canInstall))
                {
                    string label = _checkResult.ProjectState == ProjectInstallState.NormalTemplate
                        ? "Upgrade Template"
                        : "Install Template";
                    if (GUILayout.Button(label, AlicizaEditorGUI.Styles.InlineButton, GUILayout.Width(180f)))
                    {
                        InstallSelectedTemplate();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private bool DrawTemplateChoice(string title, bool selected, string description)
        {
            EditorGUILayout.BeginVertical(AlicizaEditorGUI.Styles.EntryBody);
            EditorGUILayout.BeginHorizontal();

            bool nextSelected = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(18f));
            EditorGUILayout.LabelField(title, AlicizaEditorGUI.Styles.RowLabel);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(description, AlicizaEditorGUI.Styles.MutedMiniLabel);
            EditorGUILayout.EndVertical();

            if (nextSelected)
            {
                _selectedTemplate = title.StartsWith("Hybrid", StringComparison.Ordinal) ? TemplateType.Hybrid : TemplateType.Normal;
            }

            return nextSelected;
        }

        private void DrawStatusRow(string label, bool success, string message, MessageType failedType = MessageType.Error)
        {
            EditorGUILayout.BeginHorizontal(AlicizaEditorGUI.Styles.FieldRow);
            GUIContent icon = success ? EditorUtils.Styles.GreenLight : EditorUtils.Styles.RedLight;
            GUILayout.Label(icon, GUILayout.Width(22f), GUILayout.Height(18f));
            EditorGUILayout.LabelField(label, AlicizaEditorGUI.Styles.FieldLabel, GUILayout.Width(160f));

            GUIStyle valueStyle = success ? AlicizaEditorGUI.Styles.RowLabel :
                failedType == MessageType.Warning ? AlicizaEditorGUI.Styles.WarningLabel : AlicizaEditorGUI.Styles.WarningLabel;
            EditorGUILayout.LabelField(message, valueStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureCheckResult()
        {
            if (_checkResult == null)
            {
                RunInstallCheck();
            }
        }

        private void RunInstallCheck()
        {
            _checkResult = InstallCheckResult.Create();

            if (_checkResult.ProjectState == ProjectInstallState.NotInstalled)
            {
                _selectedTemplate = _checkResult.HasHybridClr ? TemplateType.Hybrid : TemplateType.Normal;
            }
            else if (_checkResult.ProjectState == ProjectInstallState.NormalTemplate)
            {
                _selectedTemplate = TemplateType.Hybrid;
            }
            else
            {
                _selectedTemplate = TemplateType.Normal;
            }

            Repaint();
        }

        private bool CanInstallSelectedTemplate(out string blockReason)
        {
            blockReason = string.Empty;

            if (!_checkResult.UnityVersionSupported)
            {
                blockReason = "Unity version must be 2022.3.x or newer.";
                return false;
            }

            if (!_checkResult.HasUrp)
            {
                blockReason = "URP package is required before installing AlicizaX framework.";
                return false;
            }

            if (_checkResult.ProjectState == ProjectInstallState.Custom)
            {
                blockReason = "Project is marked as custom/no template required.";
                return false;
            }

            if (_checkResult.ProjectState == ProjectInstallState.HybridTemplate)
            {
                blockReason = "Hybrid template is already initialized.";
                return false;
            }

            if (_checkResult.ProjectState == ProjectInstallState.NormalTemplate && _selectedTemplate != TemplateType.Hybrid)
            {
                blockReason = "Normal template is already initialized and cannot be overwritten.";
                return false;
            }

            if (_selectedTemplate == TemplateType.Hybrid && !_checkResult.HasHybridClr)
            {
                blockReason = "Hybrid template requires HybridCLR package.";
                return false;
            }

            string templatePath = GetSelectedTemplatePath();
            if (!Directory.Exists(templatePath))
            {
                blockReason = "Template folder is missing: " + templatePath;
                return false;
            }

            return true;
        }

        private void InstallSelectedTemplate()
        {
            if (!EnsureRequiredScopedRegistry(out string registryError))
            {
                EditorUtility.DisplayDialog("AlicizaX 安装器", registryError, "确定");
                RunInstallCheck();
                return;
            }

            if (!CanInstallSelectedTemplate(out string blockReason))
            {
                EditorUtility.DisplayDialog("AlicizaX 安装器", blockReason, "确定");
                RunInstallCheck();
                return;
            }

            string templatePath = GetSelectedTemplatePath();
            string prompt = _checkResult.ProjectState == ProjectInstallState.NormalTemplate
                ? "是否将当前项目升级为热更模板？"
                : "是否将选中的模板安装到当前项目？";

            if (!EditorUtility.DisplayDialog("AlicizaX 安装器", prompt + "\n\n" + templatePath, "安装", "取消"))
            {
                return;
            }

            if (_selectedTemplate == TemplateType.Hybrid && !ConfirmHybridPlayerSettings())
            {
                return;
            }

            CopyTemplateDirectory(templatePath);
            ApplyPlayerSettings();
            ApplyScriptingDefineSymbols(_selectedTemplate);
            SaveInstallState(_selectedTemplate == TemplateType.Hybrid ? ProjectInstallState.HybridTemplate : ProjectInstallState.NormalTemplate);
            AssetDatabase.Refresh();
            RunInstallCheck();

            EditorUtility.DisplayDialog("AlicizaX 安装器", "模板安装完成。", "确定");
        }

        private static bool EnsureRequiredScopedRegistry(out string error)
        {
            error = string.Empty;

            if (InstallCheckResult.HasScopedRegistry(
                    RequiredRegistryUrl,
                    RequiredRegistryScopeCysharp,
                    RequiredRegistryScopeTuyooGame))
            {
                return true;
            }

            try
            {
                if (!File.Exists(ManifestPath))
                {
                    error = "Package manifest is missing: " + ManifestPath;
                    return false;
                }

                string manifest = File.ReadAllText(ManifestPath);
                string nextManifest = AddOrUpdateScopedRegistry(
                    manifest,
                    "OpenUPM",
                    RequiredRegistryUrl,
                    RequiredRegistryScopeCysharp,
                    RequiredRegistryScopeTuyooGame);

                if (string.Equals(manifest, nextManifest, StringComparison.Ordinal))
                {
                    error = "Failed to update OpenUPM scoped registry in " + ManifestPath + ".";
                    return false;
                }

                File.WriteAllText(ManifestPath, nextManifest);
                Debug.Log("AlicizaX installer added OpenUPM scoped registry to " + ManifestPath + ".");
                UnityEditor.PackageManager.Client.Resolve();
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to update Package Manager scoped registry: " + ex.Message;
                return false;
            }
        }

        private static string AddOrUpdateScopedRegistry(string manifest, string registryName, string registryUrl, params string[] requiredScopes)
        {
            Match registryMatch = Regex.Match(
                manifest,
                "\\{\\s*\"name\"\\s*:\\s*\"[^\"]*\"\\s*,\\s*\"url\"\\s*:\\s*\"" + Regex.Escape(registryUrl) + "\"\\s*,\\s*\"scopes\"\\s*:\\s*\\[(?<scopes>[\\s\\S]*?)\\]\\s*\\}",
                RegexOptions.Singleline);

            if (registryMatch.Success)
            {
                string scopeBlock = registryMatch.Groups["scopes"].Value;
                string nextScopeBlock = scopeBlock;
                foreach (string scope in requiredScopes)
                {
                    if (Regex.IsMatch(scopeBlock, "\"" + Regex.Escape(scope) + "\""))
                    {
                        continue;
                    }

                    nextScopeBlock = AppendJsonArrayString(nextScopeBlock, scope, GetIndent(scopeBlock, 8));
                }

                return manifest.Substring(0, registryMatch.Groups["scopes"].Index) +
                       nextScopeBlock +
                       manifest.Substring(registryMatch.Groups["scopes"].Index + registryMatch.Groups["scopes"].Length);
            }

            string registryJson = BuildScopedRegistryJson(registryName, registryUrl, requiredScopes, "    ");
            Match scopedRegistriesMatch = Regex.Match(manifest, "\"scopedRegistries\"\\s*:\\s*\\[(?<content>[\\s\\S]*?)\\]\\s*(?=\\n\\s*\\})", RegexOptions.Singleline);
            if (scopedRegistriesMatch.Success)
            {
                string content = scopedRegistriesMatch.Groups["content"].Value;
                string nextContent = string.IsNullOrWhiteSpace(content)
                    ? "\n" + registryJson + "\n  "
                    : content.TrimEnd() + ",\n" + registryJson + "\n  ";

                return manifest.Substring(0, scopedRegistriesMatch.Groups["content"].Index) +
                       nextContent +
                       manifest.Substring(scopedRegistriesMatch.Groups["content"].Index + scopedRegistriesMatch.Groups["content"].Length);
            }

            int insertIndex = manifest.LastIndexOf('}');
            if (insertIndex < 0)
            {
                return manifest;
            }

            string prefix = manifest.Substring(0, insertIndex).TrimEnd();
            string suffix = manifest.Substring(insertIndex);
            string separator = prefix.EndsWith("{", StringComparison.Ordinal) ? "\n" : ",\n";
            return prefix + separator + "  \"scopedRegistries\": [\n" + registryJson + "\n  ]\n" + suffix;
        }

        private static string AppendJsonArrayString(string arrayContent, string value, string indent)
        {
            string trimmed = arrayContent.TrimEnd();
            string separator = string.IsNullOrWhiteSpace(trimmed) ? string.Empty : ",";
            return trimmed + separator + "\n" + indent + "\"" + value + "\"";
        }

        private static string BuildScopedRegistryJson(string registryName, string registryUrl, string[] scopes, string indent)
        {
            string scopeIndent = indent + "    ";
            string json = indent + "{\n" +
                          indent + "  \"name\": \"" + registryName + "\",\n" +
                          indent + "  \"url\": \"" + registryUrl + "\",\n" +
                          indent + "  \"scopes\": [";

            for (int i = 0; i < scopes.Length; i++)
            {
                json += "\n" + scopeIndent + "\"" + scopes[i] + "\"";
                if (i < scopes.Length - 1)
                {
                    json += ",";
                }
            }

            return json + "\n" + indent + "  ]\n" + indent + "}";
        }

        private static string GetIndent(string text, int fallbackSpaces)
        {
            Match match = Regex.Match(text, "\\n(?<indent>\\s*)\"[^\"]+\"");
            if (match.Success)
            {
                return match.Groups["indent"].Value;
            }

            return new string(' ', fallbackSpaces);
        }

        private string GetSelectedTemplatePath()
        {
            return _selectedTemplate == TemplateType.Hybrid ? HybridTemplatePath : NormalTemplatePath;
        }

        private static void CopyTemplateDirectory(string templatePath)
        {
            string sourceRoot = Path.GetFullPath(templatePath);
            string targetRoot = Application.dataPath;

            foreach (string sourceDirectory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relativeDirectory = GetRelativePath(sourceRoot, sourceDirectory);
                Directory.CreateDirectory(Path.Combine(targetRoot, relativeDirectory));
            }

            foreach (string sourceFile in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if (IsTemplatePlaceholder(sourceFile))
                {
                    continue;
                }

                string relativeFile = GetRelativePath(sourceRoot, sourceFile);
                string targetFile = Path.Combine(targetRoot, relativeFile);
                string targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                if (File.Exists(targetFile))
                {
                    Debug.LogWarning("AlicizaX installer skipped existing file: " + relativeFile);
                    continue;
                }

                File.Copy(sourceFile, targetFile);
            }
        }

        private static bool IsTemplatePlaceholder(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, ".keep", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, ".gitkeep", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string fileNameWithoutMeta = fileName;
            if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                fileNameWithoutMeta = Path.GetFileNameWithoutExtension(fileName);
            }

            return string.Equals(fileNameWithoutMeta, ".keep", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileNameWithoutMeta, ".gitkeep", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRelativePath(string rootPath, string path)
        {
            Uri rootUri = new Uri(AppendDirectorySeparatorChar(rootPath));
            Uri pathUri = new Uri(path);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static void ApplyScriptingDefineSymbols(TemplateType templateType)
        {
            ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableLogSymbol);

            if (templateType == TemplateType.Hybrid)
            {
                ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableHybridClrSymbol);
            }
        }

        private static void ApplyPlayerSettings()
        {
            if (!PlayerSettings.allowUnsafeCode)
            {
                PlayerSettings.allowUnsafeCode = true;
            }
        }

        private static bool ConfirmHybridPlayerSettings()
        {
            int option = EditorUtility.DisplayDialogComplex(
                "AlicizaX 安装器",
                "安装热更模板需要确认以下 Player Settings：\n\n" +
                "1. Scripting Backend 设置为 IL2CPP。\n" +
                "2. Api Compatibility Level 设置为 .NET Framework。\n" +
                "3. Use incremental GC 需要关闭。\n\n" +
                "是否需要安装器为当前平台一键设置？",
                "一键设置",
                "手动设置",
                "取消安装");

            if (option == 2)
            {
                return false;
            }

            if (option == 0)
            {
                ApplyHybridPlayerSettings();
            }

            return true;
        }

        private static void ApplyHybridPlayerSettings()
        {
            BuildTargetGroup buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            PlayerSettings.SetScriptingBackend(buildTargetGroup, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(buildTargetGroup, ApiCompatibilityLevel.NET_4_6);
            PlayerSettings.gcIncremental = false;
        }

        private static void SaveInstallState(ProjectInstallState state)
        {
            string json = JsonUtility.ToJson(new InstallStateData
            {
                installerState = state.ToString(),
                template = ToTemplateText(state),
                unityVersion = Application.unityVersion,
                projectPath = Path.GetFullPath("."),
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            }, true);

            File.WriteAllText(InstallStatePath, json);
        }

        private static ProjectInstallState ReadInstallState(out string source)
        {
            if (File.Exists(InstallStatePath))
            {
                try
                {
                    InstallStateData data = JsonUtility.FromJson<InstallStateData>(File.ReadAllText(InstallStatePath));
                    if (TryParseState(data.installerState, out ProjectInstallState fileState))
                    {
                        source = InstallStatePath;
                        return fileState;
                    }

                    if (!string.IsNullOrEmpty(data.template) && TryParseLegacyTemplate(data.template, out fileState))
                    {
                        source = InstallStatePath + " (legacy)";
                        return fileState;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Failed to read AlicizaX installer state: " + ex.Message);
                }
            }

            bool hasLogSymbol = ScriptingDefineSymbols.HasScriptingDefineSymbol(EditorUserBuildSettings.selectedBuildTargetGroup, EnableLogSymbol);
            bool hasHybridSymbol = ScriptingDefineSymbols.HasScriptingDefineSymbol(EditorUserBuildSettings.selectedBuildTargetGroup, EnableHybridClrSymbol);

            if (hasHybridSymbol || HasHybridAssetMarkers())
            {
                source = "Compatibility fallback";
                return ProjectInstallState.HybridTemplate;
            }

            if (hasLogSymbol || HasRuntimeAssetMarkers())
            {
                source = "Compatibility fallback";
                return ProjectInstallState.NormalTemplate;
            }

            source = "Default";
            return ProjectInstallState.NotInstalled;
        }

        private static bool TryParseState(string value, out ProjectInstallState state)
        {
            if (string.IsNullOrEmpty(value))
            {
                state = ProjectInstallState.NotInstalled;
                return false;
            }

            if (Enum.TryParse(value, true, out state))
            {
                return true;
            }

            state = ProjectInstallState.NotInstalled;
            return false;
        }

        private static bool TryParseLegacyTemplate(string value, out ProjectInstallState state)
        {
            if (string.Equals(value, "Normal", StringComparison.OrdinalIgnoreCase))
            {
                state = ProjectInstallState.NormalTemplate;
                return true;
            }

            if (string.Equals(value, "Hybrid", StringComparison.OrdinalIgnoreCase))
            {
                state = ProjectInstallState.HybridTemplate;
                return true;
            }

            return TryParseState(value, out state);
        }

        private static string ToTemplateText(ProjectInstallState state)
        {
            if (state == ProjectInstallState.NormalTemplate)
            {
                return "Normal";
            }

            if (state == ProjectInstallState.HybridTemplate)
            {
                return "Hybrid";
            }

            if (state == ProjectInstallState.Custom)
            {
                return "Custom";
            }

            return string.Empty;
        }

        private static bool HasRuntimeAssetMarkers()
        {
            foreach (string marker in RuntimeAssetMarkers)
            {
                if (AssetDatabase.IsValidFolder(marker) || File.Exists(marker))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasHybridAssetMarkers()
        {
            foreach (string marker in HybridAssetMarkers)
            {
                if (AssetDatabase.IsValidFolder(marker) || File.Exists(marker))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class InstallCheckResult
        {
            public ProjectInstallState ProjectState;
            public string StateSource;
            public bool UnityVersionSupported;
            public string UnityVersion;
            public bool HasRequiredScopedRegistry;
            public string RequiredScopedRegistryText;
            public bool HasUrp;
            public string UrpVersionText;
            public bool HasHybridClr;
            public string HybridClrVersionText;
            public bool HasNormalTemplate;
            public bool HasHybridTemplate;

            public string StateText
            {
                get
                {
                    switch (ProjectState)
                    {
                        case ProjectInstallState.Custom:
                            return "Custom / no template required";
                        case ProjectInstallState.NormalTemplate:
                            return "Normal template";
                        case ProjectInstallState.HybridTemplate:
                            return "Hybrid template";
                        default:
                            return "Not initialized";
                    }
                }
            }

            public static InstallCheckResult Create()
            {
                string urpVersion = FindManifestDependencyVersion(UrpPackageName);
                string hybridClrVersion = FindManifestDependencyVersion(HybridClrPackageName);
                bool hasRequiredScopedRegistry = HasScopedRegistry(
                    RequiredRegistryUrl,
                    RequiredRegistryScopeCysharp,
                    RequiredRegistryScopeTuyooGame);
                ProjectInstallState projectState = ReadInstallState(out string stateSource);

                return new InstallCheckResult
                {
                    ProjectState = projectState,
                    StateSource = stateSource,
                    UnityVersionSupported = IsUnityVersionSupported(Application.unityVersion),
                    UnityVersion = Application.unityVersion,
                    HasRequiredScopedRegistry = hasRequiredScopedRegistry,
                    RequiredScopedRegistryText = hasRequiredScopedRegistry
                        ? RequiredRegistryUrl + " (" + RequiredRegistryScopeCysharp + ", " + RequiredRegistryScopeTuyooGame + ")"
                        : "Missing " + RequiredRegistryUrl + " scopes: " + RequiredRegistryScopeCysharp + ", " + RequiredRegistryScopeTuyooGame,
                    HasUrp = !string.IsNullOrEmpty(urpVersion),
                    UrpVersionText = string.IsNullOrEmpty(urpVersion) ? "Not installed" : urpVersion,
                    HasHybridClr = !string.IsNullOrEmpty(hybridClrVersion),
                    HybridClrVersionText = string.IsNullOrEmpty(hybridClrVersion) ? "Not installed" : hybridClrVersion,
                    HasNormalTemplate = Directory.Exists(NormalTemplatePath),
                    HasHybridTemplate = Directory.Exists(HybridTemplatePath)
                };
            }

            private static string FindManifestDependencyVersion(string packageName)
            {
                if (string.IsNullOrEmpty(packageName) || !File.Exists(ManifestPath))
                {
                    return string.Empty;
                }

                string manifest = File.ReadAllText(ManifestPath);
                Match match = Regex.Match(manifest, "\"" + Regex.Escape(packageName) + "\"\\s*:\\s*\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : string.Empty;
            }

            public static bool HasScopedRegistry(string url, params string[] scopes)
            {
                if (string.IsNullOrEmpty(url) || scopes == null || scopes.Length == 0 || !File.Exists(ManifestPath))
                {
                    return false;
                }

                try
                {
                    ManifestData manifestData = JsonUtility.FromJson<ManifestData>(File.ReadAllText(ManifestPath));
                    if (manifestData?.scopedRegistries == null)
                    {
                        return false;
                    }

                    foreach (ScopedRegistryData registry in manifestData.scopedRegistries)
                    {
                        if (registry == null ||
                            !string.Equals(NormalizeUrl(registry.url), NormalizeUrl(url), StringComparison.OrdinalIgnoreCase) ||
                            registry.scopes == null)
                        {
                            continue;
                        }

                        bool containsAllScopes = true;
                        foreach (string scope in scopes)
                        {
                            if (!Array.Exists(registry.scopes, registryScope => string.Equals(registryScope, scope, StringComparison.Ordinal)))
                            {
                                containsAllScopes = false;
                                break;
                            }
                        }

                        if (containsAllScopes)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Failed to read Package Manager scoped registries: " + ex.Message);
                }

                return false;
            }

            private static string NormalizeUrl(string url)
            {
                return string.IsNullOrEmpty(url) ? string.Empty : url.Trim().TrimEnd('/');
            }

            private static bool IsUnityVersionSupported(string version)
            {
                if (string.IsNullOrEmpty(version))
                {
                    return false;
                }

                string[] parts = version.Split('.');
                if (parts.Length < 2)
                {
                    return false;
                }

                if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
                {
                    return false;
                }

                return major > 2022 || major == 2022 && minor >= 3;
            }
        }

        [Serializable]
        private sealed class InstallStateData
        {
            public string installerState;
            public string template;
            public string unityVersion;
            public string projectPath;
            public string updatedAt;
        }

        [Serializable]
        private sealed class ManifestData
        {
            public ScopedRegistryData[] scopedRegistries;
        }

        [Serializable]
        private sealed class ScopedRegistryData
        {
            public string name;
            public string url;
            public string[] scopes;
        }
    }
}
