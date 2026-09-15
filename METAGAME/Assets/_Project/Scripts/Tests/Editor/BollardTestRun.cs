using System.IO;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public static class BollardTestRun
    {
        public static void Run()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(api));
            api.Execute(new ExecutionSettings(new Filter { testMode=TestMode.EditMode,
                testNames=new[] { "Metagame.Gameplay.Tests.AssemblyGuidanceBollardTests" } }) { runSynchronously=true });
        }
        sealed class Callbacks : ICallbacks
        {
            readonly TestRunnerApi _api;
            public Callbacks(TestRunnerApi api) { _api=api; }
            public void RunStarted(ITestAdaptor test) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                string dir=Path.GetFullPath("../tmp/bollard-integration");
                TestRunnerApi.SaveResultToFile(result,dir+"/tests.xml");
                File.WriteAllText(dir+"/tests.txt", "passed="+result.PassCount+" failed="+result.FailCount+" skipped="+result.SkipCount+" result="+result.ResultState);
                Object.DestroyImmediate(_api);
            }
        }
    }
}
