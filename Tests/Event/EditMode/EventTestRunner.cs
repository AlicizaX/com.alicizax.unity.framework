using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AlicizaX.EventTests.Editor
{
    [InitializeOnLoad]
    internal static class EventTestRunner
    {
        private const string RequestPath = "Temp/EventTests.request.json";
        private const string ActivePath = "Temp/EventTests.active.json";
        private static readonly TestRunnerApi Api;
        private static readonly System.Reflection.MethodInfo IsRunActive = typeof(TestRunnerApi).GetMethod(
            "IsRunActive", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        [Serializable]
        private sealed class Request
        {
            public string mode;
            public string output;
            public string[] assemblies;
            public string[] tests;
        }

        static EventTestRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(new Results());
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                File.Exists(ActivePath) || !File.Exists(RequestPath) || (bool)IsRunActive.Invoke(null, null)) return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            string stamp = request.output + File.GetLastWriteTimeUtc(RequestPath).Ticks;
            if (SessionState.GetString("EventTests.Refresh", "") != stamp)
            {
                SessionState.SetString("EventTests.Refresh", stamp);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return;
            }
            const string root = "Packages/com.alicizax.unity.framework/";
            if (File.GetLastWriteTimeUtc(typeof(EventBus).Assembly.Location) < Directory.GetFiles(root + "Runtime/Modules/Event", "*.cs").Max(File.GetLastWriteTimeUtc) ||
                File.GetLastWriteTimeUtc(typeof(EventBus).Assembly.Location) < File.GetLastWriteTimeUtc(root + "Plugins/SourceGenerators/Event/EventSourceGenerator.dll") ||
                File.GetLastWriteTimeUtc(typeof(EventTestRunner).Assembly.Location) < Directory.GetFiles(root + "Tests/Event/EditMode", "*.cs").Max(File.GetLastWriteTimeUtc) ||
                File.GetLastWriteTimeUtc(typeof(EventFixture).Assembly.Location) < Directory.GetFiles(root + "Tests/Event", "*.cs").Concat(Directory.GetFiles(root + "Tests/Event/PlayMode", "*.cs")).Max(File.GetLastWriteTimeUtc)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(request.output));
            File.Move(RequestPath, ActivePath);
            File.WriteAllLines(request.output + ".build", new[] { DateTime.UtcNow.ToString("O") }.Concat(
                new[] { typeof(EventBus).Assembly, typeof(EventFixture).Assembly, typeof(EventTestRunner).Assembly }.Select(
                    assembly => assembly.FullName + " | " + assembly.ManifestModule.ModuleVersionId + " | " + File.GetLastWriteTimeUtc(assembly.Location).ToString("O"))));
            Api.Execute(new ExecutionSettings(new Filter
            {
                testMode = request.mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode,
                assemblyNames = request.assemblies,
                testNames = request.tests
            }));
        }

        private sealed class Results : IErrorCallbacks
        {
            private static Request Active => File.Exists(ActivePath) ? JsonUtility.FromJson<Request>(File.ReadAllText(ActivePath)) : null;
            public void OnError(string message)
            {
                var request = Active;
                if (request == null) return;
                File.WriteAllText(request.output + ".error", message);
                File.Delete(ActivePath);
            }
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test)
            {
                var request = Active;
                if (request != null) File.WriteAllText(request.output + ".progress", test.FullName);
            }
            public void TestFinished(ITestResultAdaptor result)
            {
                var request = Active;
                if (request != null && !result.HasChildren)
                    File.AppendAllText(request.output + ".cases", result.ResultState + " " + result.FullName + "\n");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                var request = Active;
                if (request == null) return;
                File.WriteAllText(request.output, result.ToXml().OuterXml);
                File.WriteAllText(request.output + ".done", result.ResultState);
                File.Delete(ActivePath);
            }
        }
    }
}
