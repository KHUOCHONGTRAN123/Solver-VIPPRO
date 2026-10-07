using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using CatDom.CoreSolver;
using Newtonsoft.Json;
using UnityEditor;

public static class SolverSmoke
{
    public static void Run()
    {
        try
        {
            var arguments = Environment.GetCommandLineArgs();
            string level = arguments[Array.IndexOf(arguments, "--solver-level") + 1];
            string output = arguments[Array.IndexOf(arguments, "--solver-result") + 1];
            string framework = ((TargetFrameworkAttribute)Attribute.GetCustomAttribute(typeof(CatLevelSolver).Assembly, typeof(TargetFrameworkAttribute))).FrameworkName;
            if (framework != ".NETStandard,Version=v2.1") throw new Exception("Unexpected target framework: " + framework);
            var standardName = Enum.GetNames(typeof(ApiCompatibilityLevel)).First(name => name.StartsWith("NET_Standard", StringComparison.Ordinal));
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, (ApiCompatibilityLevel)Enum.Parse(typeof(ApiCompatibilityLevel), standardName));
            var result = CatLevelSolver.SolveLevel(File.ReadAllText(level));
            if (result.status != SolverStatus.Solved || double.IsNaN(result.solveTimeMs) || double.IsInfinity(result.solveTimeMs)) throw new Exception("Unity solve failed");
            if (result.algorithmVersion != "2.0.0" || result.expanded != result.baselineExpanded + result.improvedExpanded) throw new Exception("Unity v2 contract failed");
            File.WriteAllText(output, JsonConvert.SerializeObject(new { status = "Passed", unityVersion = UnityEngine.Application.unityVersion, framework, result.algorithmVersion, result.expanded, result.solveTimeMs }));
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
