using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reference = CatDom.Reference.V42;

// Independent replay produces a reusable diagnostic input at an exact prefix.
// A phase fixture is not a full-level solve or an acceptance measurement.
internal static class PhaseFixture
{
    internal static void Run(string root, string level, string experiment, int prefix)
    {
        var scope=File.ReadAllLines(Path.Combine(root,"docs/benchmarks/performance.csv"))
            .Skip(1).Select(s=>s.Split(',')).Where(r=>long.Parse(r[2])>10000).Select(r=>r[0]).ToHashSet();
        if(!scope.Contains(level))throw new ArgumentException("Outside research scope");
        string levelPath=Path.Combine(root,"Levels",level+".json");
        string planPath=Path.Combine(root,"docs/research",experiment,level+".json");
        var saved=JObject.Parse(File.ReadAllText(planPath));
        var moves=saved["solution"]!["moves"]!.ToObject<List<Reference.SolverMove>>()!;
        if(prefix<0||prefix>moves.Count)throw new ArgumentOutOfRangeException(nameof(prefix));
        var catalog=Reference.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var input=Reference.IO.SolverJson.ReadLevel(File.ReadAllText(levelPath),catalog);
        var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(Reference.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic)!;
        var result=new Reference.SolveResult();
        object engine=Activator.CreateInstance(type,flags,null,new object[]{input,Reference.SolverConfig.CreateRouteClearingDefault(),CancellationToken.None,Stopwatch.StartNew(),result},null)!;
        type.GetMethod("Replay",flags)!.Invoke(engine,new object[]{moves.Take(prefix).ToList(),true});
        var phase=type.GetMethod("CaptureInput",flags)!.Invoke(engine,null)!;
        string directory=Path.Combine(root,"docs/research/phase-fixtures");
        Directory.CreateDirectory(directory);
        string name=level+"."+experiment+"."+prefix;
        string fixture=Path.Combine(directory,name+".input.json");
        File.WriteAllText(fixture,JsonConvert.SerializeObject(phase,Formatting.Indented));
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(Path.Combine(directory,name+".provenance.json"),JsonConvert.SerializeObject(new{
            level,experiment,prefix,method="Independent v42 replay of prefix, relaxed assignment audited; CaptureInput; diagnostic only",
            levelHash=Hash(levelPath),sourceHash=Hash(planPath),fixtureHash=Hash(fixture),
            referenceAssemblyHash=Hash(typeof(Reference.CatLevelSolver).Assembly.Location)
        },Formatting.Indented));
        Console.WriteLine(fixture);
    }
}
