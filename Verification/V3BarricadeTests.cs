using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class V3BarricadeTests
{
    internal static void Run(string root)
    {
        var level=JObject.Parse("""
        {"sizeX":4,"sizeY":3,"holeInfos":[
        {"holeType":10,"position":{"x":0,"y":0},"movementType":2,"colorIds":[1],"numOfCats":[1]},
        {"holeType":10,"position":{"x":0,"y":2},"colorIds":[2],"numOfCats":[2]}],
        "catInfos":[{"position":{"x":3,"y":0},"colorId":1},
        {"position":{"x":1,"y":2},"colorId":2},{"position":{"x":2,"y":2},"colorId":2}],
        "barricades":[{"pos":{"x":1,"y":0},"direction":1,"range":1}]}
        """);
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        Product.BoardInput Read()=>Product.IO.SolverJson.ReadLevel(level.ToString(),catalog);
        foreach(bool improved in new[]{false,true})
        {
            var input=Read();
            if(!input.Copy().covers[0].barricade || input.ignoredMechanics.Count!=0)throw new Exception("Barricade parser/copy failed");
            var result=improved?Product.CatLevelSolver.SolveCore(input,CancellationToken.None):Product.LegacyLevelSolver.SolveCore(input,CancellationToken.None);
            if(result.status!=Product.SolveStatus.Solved || result.finalState.covers[0].remainingHits!=0)throw new Exception("Barricade completion failed");
            if(Product.CatLevelSolver.VerifyPlan(Read(),result.moves).status!=Product.SolveStatus.Solved)throw new Exception("Barricade replay failed");
        }
        level["barricades"][0]["range"]=0;
        if(Product.CatLevelSolver.SolveLevel(level.ToString()).status!=Product.SolverStatus.NoNextCatReachable)throw new Exception("Initial range-zero barricade disappeared");
        level["barricades"][0]["range"]=2;
        var partial=Product.CatLevelSolver.VerifyPlan(Read(),new(){new(){holeId=1,start=new(0,2),path=new(){new(0,2),new(1,2)}}});
        if(partial.finalState.covers[0].remainingHits!=2)throw new Exception("Barricade did not shrink one tail cell per Cat");
        Console.WriteLine("PASS barricade tail shrink, full removal, range-zero persistence and replay");
    }
}
