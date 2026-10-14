using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class V3BombTests
{
    internal static void Run(string root)
    {
        var level=JObject.Parse("""
        {"sizeX":3,"sizeY":3,"holeInfos":[
        {"holeType":10,"position":{"x":0,"y":0},"colorIds":[1],"numOfCats":[1],"timeLimited":1},
        {"holeType":10,"position":{"x":0,"y":2},"colorIds":[2],"numOfCats":[1]}],
        "catInfos":[{"position":{"x":1,"y":0},"colorId":1},{"position":{"x":1,"y":2},"colorId":2}]}
        """);
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        Product.BoardInput Read()=>Product.IO.SolverJson.ReadLevel(level.ToString(),catalog);
        var result=Product.CatLevelSolver.SolveLevel(level.ToString());
        if(result.status!=Product.SolverStatus.Solved||result.moves.First(m=>m.eaten.Count>0).holeId!=0)throw new Exception("Bomb hole not finished before deadline");
        var moves=result.moves.Select(m=>new Product.EngineMove{holeId=m.holeId,start=m.start,path=m.path,eaten=m.eaten}).ToList();
        if(Product.CatLevelSolver.VerifyPlan(Read(),moves).status!=Product.SolveStatus.Solved)throw new Exception("Bomb replay failed");
        HoleBombReplayOracle.Verify(level.ToString(),result.diagnosticInitialEaten.Concat(result.moves.SelectMany(m=>m.eaten)));
        bool oracleRejected=false;
        try{HoleBombReplayOracle.Verify(level.ToString(),new[]{new Product.EatEvent{holeId=1}});}
        catch(ArgumentException){oracleRejected=true;}
        if(!oracleRejected)throw new Exception("Independent bomb oracle missed expiry");
        bool rejected=false;
        try{Product.CatLevelSolver.VerifyPlan(Read(),new(){new(){holeId=1,start=new(0,2),path=new(){new(0,2),new(1,2)}}});}
        catch(ArgumentException){rejected=true;}
        if(!rejected)throw new Exception("Expired bomb accepted in replay");
        level["holeInfos"][1]["timeLimited"]=1;
        if(Product.CatLevelSolver.SolveLevel(level.ToString()).status!=Product.SolverStatus.NoNextCatReachable)throw new Exception("Mutual bomb deadline incorrectly solved");
        Console.WriteLine("PASS hole bomb completion ordering, failure replay and mutual deadline");
    }
}
