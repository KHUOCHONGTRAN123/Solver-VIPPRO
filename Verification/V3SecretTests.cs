using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class V3SecretTests
{
    internal static void Run(string root)
    {
        var level=JObject.Parse("""
        {"sizeX":4,"sizeY":3,"holeInfos":[
        {"holeType":10,"position":{"x":0,"y":0},"colorIds":[1],"numOfCats":[1]},
        {"holeType":10,"position":{"x":0,"y":2},"colorIds":[100],"numOfCats":[2]}],
        "catInfos":[{"position":{"x":1,"y":0},"colorId":1},
        {"position":{"x":1,"y":2},"colorId":100},{"position":{"x":2,"y":2},"colorId":100}],
        "secretLockboxInfos":[{"position":{"x":0,"y":0},"sizeX":2,"sizeY":1,"colorIds":[0,0],"numCats":[1,1]}]}
        """);
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        Product.BoardInput Read()=>Product.IO.SolverJson.ReadLevel(level.ToString(),catalog);
        var initial=Read();
        if(initial.covers.Count!=1 || initial.covers[0].remainingHits!=2 || !initial.Copy().covers[0].secretLockCounter)throw new Exception("Secret duplicate-color aggregation/copy failed");
        var partial=Product.CatLevelSolver.VerifyPlan(Read(),new(){new(){holeId=1,start=new(0,2),path=new(){new(0,2),new(1,2)}}});
        if(partial.finalState.covers[0].remainingHits!=1)throw new Exception("Secret counter did not match base color");
        foreach(bool improved in new[]{false,true})
        {
            var result=improved?Product.CatLevelSolver.SolveCore(Read(),CancellationToken.None):Product.LegacyLevelSolver.SolveCore(Read(),CancellationToken.None);
            if(result.status!=Product.SolveStatus.Solved || result.finalState.covers[0].remainingHits!=0)throw new Exception("Secret unlock chain failed");
            if(Product.CatLevelSolver.VerifyPlan(Read(),result.moves).status!=Product.SolveStatus.Solved)throw new Exception("Secret replay failed");
        }
        level["secretLockboxInfos"][0]["numCats"]=new JArray(0,0);
        if(Product.CatLevelSolver.SolveLevel(level.ToString()).status!=Product.SolverStatus.NoNextCatReachable)throw new Exception("Empty-requirement secret box incorrectly opened");
        Console.WriteLine("PASS secret rectangle, duplicate requirements, base color, zero requirement and replay");
    }
}
