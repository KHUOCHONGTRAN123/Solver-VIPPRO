using System.Diagnostics;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;
using ReferenceJson = CatDom.Reference.V42.IO.SolverJson;

static class ResearchBenchmark
{
    internal static void Run(string root, string experiment, string levelFilter = null, long maxExpanded = 150000)
    {
        var rows = File.ReadAllLines(Path.Combine(root,"docs/benchmarks/performance.csv"))
            .Skip(1).Select(line=>line.Split(',')).Where(row=>long.Parse(row[2])>10000).ToArray();
        if(rows.Length!=21)throw new Exception("Expected exactly 21 research levels.");
        if(levelFilter!=null)
        {
            var requested=levelFilter.Split(',').ToHashSet();
            if(requested.Any(level=>!rows.Any(row=>row[0]==level)))throw new Exception("Level outside research scope.");
            rows=rows.Where(row=>requested.Contains(row[0])).ToArray();
        }
        var directory=Path.Combine(root,"docs/research",experiment);
        Directory.CreateDirectory(directory);
        var catalog=ReferenceJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        using (var warmupCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            Product.CatLevelSolver.SolveLevel(File.ReadAllText(Path.Combine(root,"Levels",rows[0][0]+".json")), warmupCancellation.Token);
        var results=new List<object>();
        foreach(var row in rows)
        {
            var file=Path.Combine(root,"Levels",row[0]+".json");
            var json=File.ReadAllText(file);
            using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var trace=new List<object>();
            var searchClock=Stopwatch.StartNew();
            var result=Product.CatLevelSolver.SolveLevelWithProgress(json,cancellation.Token,r=>{
                trace.Add(new {expanded=r.expanded,abstractExpanded=r.abstractExpanded,drags=r.moves.Count,stage=r.searchStage,elapsedMs=searchClock.Elapsed.TotalMilliseconds});
                if(r.expanded>=maxExpanded)cancellation.Cancel();
            });
            bool replayed=false;
            if(result.status==Product.SolverStatus.Solved)
            {
                var moves=JsonConvert.DeserializeObject<List<Reference.SolverMove>>(JsonConvert.SerializeObject(result.moves));
                var replay=Reference.CatLevelSolver.VerifyPlan(ReferenceJson.ReadLevel(json,catalog),moves,0,true);
                replayed=replay.status==Reference.SolveStatus.Solved&&replay.finalState.remainingCatIds.Count==0;
                if(!replayed)throw new Exception("Independent replay failed: "+row[0]);
            }
            var item=new {level=row[0],baselineExpanded=long.Parse(row[2]),baselineMs=double.Parse(row[4],System.Globalization.CultureInfo.InvariantCulture),
                status=result.status.ToString(),result.expanded,result.abstractExpanded,result.solveTimeMs,replayed,passed=replayed&&result.expanded<30000&&result.solveTimeMs<3000,
                expandedExclusiveLimit=30000,solveTimeExclusiveLimitMs=3000,
                diagnosticExpandedCeiling=maxExpanded,
                measurementKind="single-run screening; acceptance requires three measured runs after target warm-up",
                drags=result.moves.Count,steps=result.moves.Sum(m=>m.path.Count-1),
                levelHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
                assemblyHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Product.CatLevelSolver).Assembly.Location)))};
            results.Add(item);
            File.WriteAllText(Path.Combine(directory,row[0]+".json"),JsonConvert.SerializeObject(new {measurement=item,trace,solution=result},Formatting.Indented));
            File.WriteAllText(Path.Combine(directory,"summary.json"),JsonConvert.SerializeObject(results,Formatting.Indented));
            Console.WriteLine($"{row[0]} {result.status} expanded={result.expanded} ms={result.solveTimeMs:F1} drags={result.moves.Count} replay={replayed} pass={item.passed}");
        }
    }
}
