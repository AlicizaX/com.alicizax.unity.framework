using System;
using System.IO;
using System.Linq;
using AlicizaX.UI.Runtime;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AlicizaX.UI.Tests
{
    [InitializeOnLoad]
    internal static class UIVerificationRunner
    {
        [Serializable]
        private sealed class Request
        {
            public string mode, output;
            public string[] assemblies, tests;
        }

        private const string RequestPath = "Temp/UIVerification.request.json";
        private const string OutputKey = "UIVerification.Output";
        private const string RefreshKey = "UIVerification.Refresh";
        private const string Root = "Packages/com.alicizax.unity.framework/";
        private static readonly TestRunnerApi Api;
        private static readonly System.Reflection.MethodInfo IsRunActive = typeof(TestRunnerApi).GetMethod(
            "IsRunActive", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        static UIVerificationRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(new Callbacks());
            EditorApplication.update += Poll;
        }

        private static bool IsCompiled(Type type, string directory, Func<string, bool> include = null)
        {
            var sources = Directory.GetFiles(Root + directory, "*.cs", SearchOption.AllDirectories);
            DateTime latest = sources.Where(path => include == null || include(path)).Max(File.GetLastWriteTimeUtc);
            return File.GetLastWriteTimeUtc(type.Assembly.Location) >= latest;
        }

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                !File.Exists(RequestPath) || SessionState.GetString(OutputKey, "") != "" || (bool)IsRunActive.Invoke(null, null)) return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            string stamp = request.output + "|" + File.GetLastWriteTimeUtc(RequestPath).Ticks;
            if (SessionState.GetString(RefreshKey, "") != stamp)
            {
                SessionState.SetString(RefreshKey, stamp);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return;
            }
            if (!IsCompiled(typeof(UIBase), "Runtime/Modules/UI") ||
                !IsCompiled(typeof(UIFixture), "Tests/UI", path => !path.Replace('\\', '/').Contains("/EditMode/")) ||
                !IsCompiled(typeof(UIVerificationRunner), "Tests/UI/EditMode")) return;

            Directory.CreateDirectory(Path.GetDirectoryName(request.output));
            File.WriteAllText(request.output + ".build", string.Join("\n",
                new[] { typeof(UIBase).Assembly, typeof(UIFixture).Assembly, typeof(UIVerificationRunner).Assembly }
                .Select(assembly => assembly.GetName().Name + "," + assembly.ManifestModule.ModuleVersionId + "," +
                    File.GetLastWriteTimeUtc(assembly.Location).ToString("O"))));
            File.Delete(RequestPath);
            SessionState.SetString(OutputKey, request.output);
            Api.Execute(new ExecutionSettings(new Filter
            {
                testMode = request.mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode,
                assemblyNames = request.assemblies,
                testNames = request.tests,
            }));
        }

        private sealed class Callbacks : IErrorCallbacks
        {
            public void OnError(string message)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output != "") File.WriteAllText(output + ".error", message);
                SessionState.EraseString(OutputKey);
                SessionState.EraseString(RefreshKey);
            }

            public void RunStarted(ITestAdaptor test) { }

            public void TestStarted(ITestAdaptor test)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output != "") File.WriteAllText(output + ".progress", test.FullName);
            }

            public void TestFinished(ITestResultAdaptor test)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output == "" || test.Test.IsSuite) return;
                File.AppendAllText(output + ".cases", test.FullName + "\t" + test.ResultState + "\n");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output == "") return;
                File.WriteAllText(output, result.ToXml().OuterXml);
                File.WriteAllText(output + ".done", result.ResultState);
                SessionState.EraseString(OutputKey);
                SessionState.EraseString(RefreshKey);
            }
        }
    }
}
