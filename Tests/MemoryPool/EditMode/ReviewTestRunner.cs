using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AlicizaX.MemoryPoolTests
{
    [InitializeOnLoad]
    internal static class ReviewTestRunner
    {
        [Serializable]
        private sealed class Request
        {
            public string mode;
            public string output;
            public string[] assemblies;
            public string[] tests;
        }

        private static readonly TestRunnerApi Api;
        private static readonly string RequestPath = Path.GetFullPath("Temp/MemoryPoolReview.request.json");
        private const string OutputKey = "MemoryPoolReview.Output";

        static ReviewTestRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(new Callbacks());
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            File.Delete(RequestPath);
            SessionState.SetString(OutputKey, request.output);
            Directory.CreateDirectory(Path.GetDirectoryName(request.output));
            Api.Execute(new ExecutionSettings(new Filter
            {
                testMode = request.mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode,
                assemblyNames = request.assemblies,
                testNames = request.tests
            }));
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (!string.IsNullOrEmpty(output)) File.WriteAllText(output + ".progress", test.FullName);
            }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (string.IsNullOrEmpty(output)) return;
                File.WriteAllText(output, result.ToXml().OuterXml);
                File.WriteAllText(output + ".progress", "Finished: " + result.ResultState);
                SessionState.EraseString(OutputKey);
            }
        }
    }
}
