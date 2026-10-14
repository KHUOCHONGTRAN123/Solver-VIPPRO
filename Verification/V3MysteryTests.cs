using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class V3MysteryTests
{
    internal static void Run(string root)
    {
        var level=JObject.Parse("""
        {"sizeX":3,"sizeY":3,"holeInfos":[
        {"holeType":10,"position":{"x":0,"y":0},"colorIds":[1],"numOfCats":[1]},
        {"holeType":10,"position":{"x":0,"y":2},"colorIds":[100],"numOfCats":[1]}],
        "catInfos":[{"position":{"x":1,"y":0},"colorId":1,"mysteryBoxId":0},
        {"position":{"x":1,"y":2},"colorId":100}]}
        """);
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        Product.BoardInput Read()=>Product.IO.SolverJson.ReadLevel(level.ToString(),catalog);
        foreach(bool improved in new[]{false,true})
        {
            var result=improved?Product.CatLevelSolver.SolveCore(Read(),CancellationToken.None):Product.LegacyLevelSolver.SolveCore(Read(),CancellationToken.None);
            if(result.status!=Product.SolveStatus.Solved || result.moves.First(m=>m.eaten.Count>0).holeId!=1)
                throw new Exception("Mystery color-zero/base-color unlock failed");
            if(Product.CatLevelSolver.VerifyPlan(Read(),result.moves).status!=Product.SolveStatus.Solved)
                throw new Exception("Mystery replay failed");
        }
        bool rejected=false;
        try{Product.CatLevelSolver.VerifyPlan(Read(),new(){new(){holeId=0,start=new(0,0),path=new(){new(0,0),new(1,0)}}});}
        catch(ArgumentException){rejected=true;}
        if(!rejected)throw new Exception("Mystery cat entered before unlock");
        level["catInfos"][1]["mysteryBoxId"]=1;
        if(Product.CatLevelSolver.SolveLevel(level.ToString()).status!=Product.SolverStatus.NoNextCatReachable)
            throw new Exception("Mutual mystery dependency incorrectly solved");
        Console.WriteLine("PASS mystery base-color unlock, illegal entry, mutual dependency and replay");
    }
}
