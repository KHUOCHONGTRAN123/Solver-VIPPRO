using Newtonsoft.Json;
using Reference = CatDom.Reference.V42;
using Product = CatDom.CoreSolver;

internal static class V1TestAdapter
{
    // Legacy types are fixture/oracle data only, never part of the product API.
    internal static Reference.SolveResult Solve(Reference.BoardInput input, Reference.SolverConfig oracle, CancellationToken token = default)
    {
        if(oracle.strategy==Reference.SearchStrategy.Backtracking)return Reference.CatLevelSolver.Solve(input,oracle,token);
        var board=JsonConvert.DeserializeObject<Product.BoardInput>(JsonConvert.SerializeObject(input));
        var result=Product.CatLevelSolver.SolveCore(board,token);
        return JsonConvert.DeserializeObject<Reference.SolveResult>(JsonConvert.SerializeObject(result));
    }
    internal static Reference.SolveResult FindNextCat(Reference.BoardInput input, Reference.SolverConfig unused) => Solve(input,unused);
    internal static Reference.SolveResult VerifyPlan(Reference.BoardInput input,List<Reference.SolverMove> moves,double unused,bool audit=false)
    {
        var board=JsonConvert.DeserializeObject<Product.BoardInput>(JsonConvert.SerializeObject(input));
        var plan=JsonConvert.DeserializeObject<List<Product.EngineMove>>(JsonConvert.SerializeObject(moves));
        var result=Product.CatLevelSolver.VerifyPlan(board,plan,audit);
        return JsonConvert.DeserializeObject<Reference.SolveResult>(JsonConvert.SerializeObject(result));
    }
}
