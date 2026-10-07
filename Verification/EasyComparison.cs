using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;

internal static class EasyComparison
{
    internal static void Run(string root, bool budgetProbe = false)
    {
        var levels=File.ReadAllLines(Path.Combine(root,"docs/benchmarks/performance.csv")).Skip(1)
            .Select(s=>s.Split(',')).Where(r=>long.Parse(r[2])<10000&&(!budgetProbe||long.Parse(r[2])>5000)).Select(r=>r[0]).ToArray();
        string directory=Path.Combine(root,budgetProbe?"docs/research/budget-5000-comparison":"docs/research/easy-comparison");Directory.CreateDirectory(directory);
        string baselinePath=Path.Combine(root,"artifacts/unity/IndependentSolver.dll");
        var baseline=Assembly.LoadFile(baselinePath);
        var method=baseline.GetType("CatDom.CoreSolver.CatLevelSolver",true)!.GetMethod("SolveLevel",new[]{typeof(string),typeof(CancellationToken)})!;
        var catalog=Reference.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var reports=new List<object>();
        foreach(string level in levels)
        {
            string file=Path.Combine(root,"Levels",level+".json"),json=File.ReadAllText(file);
            var modes=new List<object>();
            // Alternate mode order to reduce systematic warm-machine bias.
            foreach(string mode in budgetProbe?new[]{"hybrid5000"}:reports.Count%2==0?new[]{"baseline","current"}:new[]{"current","baseline"})
            {
                object Solve(CancellationToken token)=>mode=="baseline"?method.Invoke(null,new object[]{json,token})!:budgetProbe
                    ?Product.CatLevelSolver.SolveLevelWithProgress(json,token,null,5000):Product.CatLevelSolver.SolveLevel(json,token);
                using(var warm=new CancellationTokenSource(TimeSpan.FromSeconds(10)))Solve(warm.Token);
                var samples=new List<object>();
                for(int iteration=0;iteration<3;iteration++)
                {
                    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                    using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    long start=GC.GetAllocatedBytesForCurrentThread();int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
                    object raw=Solve(cancellation.Token);
                    long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
                    int gen0=GC.CollectionCount(0)-g0,gen1=GC.CollectionCount(1)-g1,gen2=GC.CollectionCount(2)-g2;
                    // JSON serialization and independent replay are outside resource measurement.
                    var result=JObject.FromObject(raw);
                    if(budgetProbe&&((long)result["baselineExpanded"]!=5000||(long)result["expanded"]!=5000+(long)result["improvedExpanded"]))
                        throw new Exception("5000-state accounting mismatch "+level);
                    var moves=result["moves"]!.ToObject<List<Reference.SolverMove>>()!;
                    bool replayed=false;
                    if((string)result["status"]=="Solved")
                    {
                        var replay=Reference.CatLevelSolver.VerifyPlan(Reference.IO.SolverJson.ReadLevel(json,catalog),moves,0,true);
                        replayed=replay.status==Reference.SolveStatus.Solved&&replay.finalState.remainingCatIds.Count==0;
                        if(!replayed)throw new Exception("Replay failed "+level+" "+mode);
                    }
                    samples.Add(new {iteration,status=(string)result["status"],expanded=(long)result["expanded"],baselineExpanded=(long?)result["baselineExpanded"],improvedExpanded=(long?)result["improvedExpanded"],solveTimeMs=(double)result["solveTimeMs"],allocatedBytes=allocated,gen0,gen1,gen2,replayed,moves=moves.Count,steps=moves.Sum(m=>m.path.Count-1),planHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(result["moves"]!.ToString(Formatting.None))))});
                    File.WriteAllText(Path.Combine(directory,$"{level}.{mode}.sample{iteration}.json"),result.ToString(Formatting.Indented));
                }
                modes.Add(new {mode,samples});
            }
            var report=new {level,levelHash=Hash(file),modes};reports.Add(report);
            File.WriteAllText(Path.Combine(directory,level+".json"),JsonConvert.SerializeObject(report,Formatting.Indented));
            File.WriteAllText(Path.Combine(directory,"summary.json"),JsonConvert.SerializeObject(new {method="Release; sequential; own-level warm-up; three samples; alternate mode order; 10s diagnostic cancellation; allocation current thread includes API solve, excludes serialize/audit; GC before sample; no per-level process RAM isolation",count=levels.Length,baselineHash=Hash(baselinePath),currentHash=Hash(typeof(Product.CatLevelSolver).Assembly.Location),referenceHash=Hash(typeof(Reference.CatLevelSolver).Assembly.Location),runtime=Environment.Version.ToString(),machine=Environment.MachineName,reports},Formatting.Indented));
            Console.WriteLine($"{reports.Count}/{levels.Length} {level}");
        }
    }
    private static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
}
