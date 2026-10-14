using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Product=CatDom.CoreSolver;

internal static class Group2Profile
{
    internal static void Run(string root,string level,string output)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        var profileField=type.GetField("searchProfile",flags);
        if(profileField==null)throw new Exception("Profiling requires a build with DefineConstants=CATDOM_PROFILE.");
        string file=Path.Combine(root,"Levels_Group2_Assessment_2026_10_12_Run2/Group2_Backtracking",level+".json");
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var board=Product.IO.SolverJson.ReadLevel(File.ReadAllText(file),catalog);
        var result=new Product.EngineResult();
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var context=new Product.EngineContext{progress=r=>{if(r.expanded>=100000)cancellation.Cancel();}};
        long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
        var engine=Activator.CreateInstance(type,flags,null,new object[]{board,context,cancellation.Token,result},null);
        string terminal="Returned";
        try{type.GetMethod("Run",flags).Invoke(engine,null);}
        catch(TargetInvocationException ex) when(ex.InnerException is OperationCanceledException){terminal="Cancelled";}
        watch.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        var timings=(Dictionary<string,long[]>)profileField.GetValue(engine);
        var metrics=timings.ToDictionary(p=>p.Key,p=>new{calls=p.Value[1],inclusiveMs=p.Value[0]*1000.0/Stopwatch.Frequency});
        var evidence=new{method="Instrumented Core search diagnostic, inclusive nested timings; no internal replay, not API acceptance. Default product compiles profiler out.",
            level,inputHash=Hash(file),productHash=Hash(typeof(Product.CatLevelSolver).Assembly.Location),verifierHash=Hash(typeof(Group2Profile).Assembly.Location),
            terminal,status=result.status.ToString(),result.expanded,elapsedMs=watch.Elapsed.TotalMilliseconds,allocatedBytes=allocated,metrics};
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output,JsonConvert.SerializeObject(evidence,Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(new{level,terminal,result.expanded,allocatedBytes=allocated,metrics}));
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
