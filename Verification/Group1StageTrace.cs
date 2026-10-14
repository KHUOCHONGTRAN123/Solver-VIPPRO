using System.Diagnostics;
using Newtonsoft.Json;
using Product=CatDom.CoreSolver;

internal static class Group1StageTrace
{
    internal static void Run(string root,string level)
    {
        var json=File.ReadAllText(Path.Combine(root,"Levels_Current_2026_10_12_TwoSources/Group1_Easy",level+".json"));
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var input=Product.IO.SolverJson.ReadLevel(json,catalog);
        var trace=new List<object>();string previous=null;
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var timer=Stopwatch.StartNew();
        var result=Product.CatLevelSolver.SolveCore(input,cancellation.Token,r=>
        {
            if(r.searchStage!=previous)
            {
                trace.Add(new{stage=r.searchStage,r.expanded,moves=r.moves.Count,ms=timer.Elapsed.TotalMilliseconds});
                previous=r.searchStage;
            }
            if(r.expanded>=50000)cancellation.Cancel();
        });
        string directory=Path.Combine(root,"Verification/results/group1-optimization");
        File.WriteAllText(Path.Combine(directory,level+"-stages.json"),JsonConvert.SerializeObject(new{method="Core stage diagnostic, not API acceptance measurement",level,status=result.status.ToString(),result.expanded,ms=timer.Elapsed.TotalMilliseconds,trace,finalState=result.finalState},Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(new{level,status=result.status.ToString(),result.expanded,phases=trace.Count}));
    }
}
