using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;

internal static class V3MechanicsTests
{
    internal static void Run(string root)
    {
        var catalog = Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        JObject Hole(int color, int x, int y) => JObject.FromObject(new {
            colorIds=new[]{color}, numOfCats=new[]{1}, holeType=10, position=new{x,y} });
        JObject Cat(int color, int x, int y) => JObject.FromObject(new {colorId=color,position=new{x,y}});
        var rune = new JObject { ["sizeX"]=3,["sizeY"]=3,
            ["holeInfos"]=new JArray(Hole(2,0,0),Hole(1,2,0),Hole(101,1,0)),
            ["catInfos"]=new JArray(Cat(2,0,1),Cat(1,2,2),Cat(101,1,1)) };
        rune["holeInfos"][0]["runesealedUnlockHoleIndex"]=1;
        rune["holeInfos"][1]["numIced"]=2;
        // The referenced hole cannot finish first. Another hole of the same
        // type and base color must break the rune, then rune completion thaws it.
        foreach (bool improved in new[]{false,true})
        {
            var input=Product.IO.SolverJson.ReadLevel(rune.ToString(),catalog);
            var result=improved ? Product.CatLevelSolver.SolveCore(input,CancellationToken.None)
                                : Product.LegacyLevelSolver.SolveCore(input,CancellationToken.None);
            Check(result.status==Product.SolveStatus.Solved,"Rune signature chain failed");
            Check(result.moves.First(m=>m.eaten.Count>0).eaten[0].holeId==2,"Rune/frozen hole moved before unlock");
            Check(result.finalState.holes[0].runesealedUnlockHoleIndex==-1,"Rune was not cleared");
            var original=Product.IO.SolverJson.ReadLevel(rune.ToString(),catalog);
            Check(Product.CatLevelSolver.VerifyPlan(original,result.moves).status==Product.SolveStatus.Solved,"Rune replay failed");
        }
        var illegal=new Product.EngineMove {holeId=0,start=new Product.Cell(0,0),
            path=new List<Product.Cell>{new(0,0),new(0,1)}};
        bool rejected=false;
        try {Product.CatLevelSolver.VerifyPlan(Product.IO.SolverJson.ReadLevel(rune.ToString(),catalog),new(){illegal});}
        catch(ArgumentException){rejected=true;}
        Check(rejected,"Rune drag accepted before unlock");

        var axis = new JObject { ["sizeX"]=3,["sizeY"]=3,
            ["holeInfos"]=new JArray(Hole(1,0,0),Hole(2,2,2)),
            ["catInfos"]=new JArray(Cat(1,2,0),Cat(2,1,2)) };
        axis["holeInfos"][0]["movementType"]=1;
        axis["holeInfos"][0]["movementTypeLimit"]=1;
        foreach (bool improved in new[]{false,true})
        {
            var input=Product.IO.SolverJson.ReadLevel(axis.ToString(),catalog);
            var result=improved ? Product.CatLevelSolver.SolveCore(input,CancellationToken.None)
                                : Product.LegacyLevelSolver.SolveCore(input,CancellationToken.None);
            Check(result.status==Product.SolveStatus.Solved,"Axis release chain failed");
            Check(result.moves.First(m=>m.eaten.Count>0).eaten[0].holeId==1,"Axis freed before another hole finished");
            Check(result.finalState.holes[0].movementType==0 && result.finalState.holes[0].movementTypeLimit==0,"Axis limit not consumed");
            Check(Product.CatLevelSolver.VerifyPlan(Product.IO.SolverJson.ReadLevel(axis.ToString(),catalog),result.moves).status==Product.SolveStatus.Solved,"Axis replay failed");
        }
        var copy=Product.IO.SolverJson.ReadLevel(axis.ToString(),catalog).Copy();
        Check(copy.holes[0].movementTypeLimit==1,"Copy lost movement counter");
        var api=Product.CatLevelSolver.SolveLevel(axis.ToString());
        Check(api.status==Product.SolverStatus.Solved && api.searchAlgorithm=="improved" && api.baselineExpanded==0 && api.expanded==api.improvedExpanded,"Dynamic mechanic dispatch/accounting failed");
        axis["holeInfos"][0]["movementTypeLimit"]=0;
        Check(Product.CatLevelSolver.SolveLevel(axis.ToString()).status==Product.SolverStatus.NoNextCatReachable,"Permanent strict axis unexpectedly freed");
        var ropes = new JObject { ["sizeX"]=3,["sizeY"]=3,
            ["holeInfos"]=new JArray(Hole(1,0,0),Hole(2,0,2)),
            ["catInfos"]=new JArray(Cat(1,2,0),Cat(2,1,2),Cat(2,2,2)),
            ["ropeInfos"]=new JArray(
                new JObject{["colorId"]=0,["count"]=2,["positions"]=new JArray(new JObject{["x"]=1,["y"]=0})},
                new JObject{["colorId"]=0,["count"]=2,["positions"]=new JArray(new JObject{["x"]=1,["y"]=1})}) };
        ropes["holeInfos"][0]["movementType"]=2;
        ropes["holeInfos"][1]["numOfCats"][0]=2;
        ropes["catInfos"][1]["scissorsColorId"]=0;
        ropes["catInfos"][2]["scissorsColorId"]=0;
        foreach(bool improved in new[]{false,true})
        {
            var input=Product.IO.SolverJson.ReadLevel(ropes.ToString(),catalog);
            Check(input.ignoredMechanics.Count==0 && input.Copy().covers.All(c=>c.rope && c.color==0),"Rope parser/copy failed");
            var result=improved ? Product.CatLevelSolver.SolveCore(input,CancellationToken.None)
                                : Product.LegacyLevelSolver.SolveCore(input,CancellationToken.None);
            Check(result.status==Product.SolveStatus.Solved,"Two-scissor rope chain failed");
            Check(result.finalState.covers.All(c=>c.remainingHits==0),"Matching ropes did not all release");
            Check(Product.CatLevelSolver.VerifyPlan(Product.IO.SolverJson.ReadLevel(ropes.ToString(),catalog),result.moves).status==Product.SolveStatus.Solved,"Rope replay failed");
        }
        ropes["catInfos"][2]["scissorsColorId"]=-1;
        Check(Product.CatLevelSolver.SolveLevel(ropes.ToString()).status==Product.SolverStatus.NoNextCatReachable,"Rope released with insufficient scissors");
        Console.WriteLine("PASS v3 rune signature, axis release, rope counts/color-zero, multiple ropes and insufficient scissors");
    }
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
}
