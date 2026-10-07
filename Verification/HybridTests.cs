using Newtonsoft.Json;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;

internal static class HybridTests
{
    internal static void Run(string root)
    {
        string json=File.ReadAllText(Path.Combine(root,"Levels/Level00001.json"));
        var original=Product.CatLevelSolver.SolveLevel(json);
        if(original.searchAlgorithm!="baseline"||original.improvedExpanded!=0||original.baselineExpanded!=original.expanded)
            throw new Exception("Baseline-only accounting failed");
        var exact=Product.CatLevelSolver.SolveLevelWithProgress(json,CancellationToken.None,null,original.expanded);
        if(exact.status!=Product.SolverStatus.Solved||exact.searchAlgorithm!="baseline")throw new Exception("Exact-budget solved boundary failed");
        var fallback=Product.CatLevelSolver.SolveLevelWithProgress(json,CancellationToken.None,null,1);
        if(fallback.status!=Product.SolverStatus.Solved||fallback.searchAlgorithm!="baseline+improved"||fallback.baselineExpanded!=1||fallback.expanded!=1+fallback.improvedExpanded)
            throw new Exception("Small-budget fallback accounting failed");
        var catalog=Reference.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var moves=JsonConvert.DeserializeObject<List<Reference.SolverMove>>(JsonConvert.SerializeObject(fallback.moves));
        if(Reference.CatLevelSolver.VerifyPlan(Reference.IO.SolverJson.ReadLevel(json,catalog),moves,0,true).status!=Reference.SolveStatus.Solved)
            throw new Exception("Restart did not produce a valid fresh plan");
        using var cancellation=new CancellationTokenSource();
        var cancelled=Product.CatLevelSolver.SolveLevelWithProgress(File.ReadAllText(Path.Combine(root,"Levels/Level00269.json")),cancellation.Token,r=>
        {
            if(r.expanded>=1)cancellation.Cancel();
        },1);
        if(cancelled.status!=Product.SolverStatus.Cancelled||cancelled.expanded!=cancelled.baselineExpanded+cancelled.improvedExpanded||cancelled.baselineExpanded>1)
            throw new Exception("Cancellation accounting failed");
        Console.WriteLine("PASS hybrid exact-budget boundary, restart replay, cumulative accounting and cancellation");
    }
}
