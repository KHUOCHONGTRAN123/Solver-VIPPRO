using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class V3CurtainTests
{
    internal static void Run(string root)
    {
        JObject Hole(int color,int x,int y)=>JObject.FromObject(new{colorIds=new[]{color},numOfCats=new[]{1},holeType=10,position=new{x,y}});
        JObject Cat(int color,int x,int y)=>JObject.FromObject(new{colorId=color,position=new{x,y}});
        Product.EngineMove Move(int id,int x,int y,int nx,int ny)=>new(){holeId=id,start=new(x,y),path=new(){new(x,y),new(nx,ny)}};
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var curtain=new JObject{["sizeX"]=3,["sizeY"]=3,["holeInfos"]=new JArray(Hole(1,0,0),Hole(2,0,2)),
            ["catInfos"]=new JArray(Cat(1,2,0),Cat(2,1,2),Cat(3,2,2))};
        curtain["holeInfos"][0]["isOpenClose"]=true;
        curtain["holeInfos"][1]["isOpenClose"]=true;
        curtain["holeInfos"][1]["isOpenCloseInitiallyClosed"]=false;
        curtain["holeInfos"][1]["colorIds"]=new JArray(2,3);
        curtain["holeInfos"][1]["numOfCats"]=new JArray(1,1);
        Product.BoardInput Read(JObject level)=>Product.IO.SolverJson.ReadLevel(level.ToString(),catalog);
        var partial=Product.CatLevelSolver.VerifyPlan(Read(curtain),new(){Move(1,0,2,1,2)});
        Check(partial.finalState.holes[0].closed && !partial.finalState.holes[1].closed,"Curtain toggled on first tunnel layer");
        var opened=Product.CatLevelSolver.VerifyPlan(Read(curtain),new(){Move(1,0,2,1,2),Move(1,1,2,2,2)});
        Check(!opened.finalState.holes[0].closed,"Curtain did not toggle on complete hole");
        Check(Read(curtain).Copy().holes[0].closed,"Curtain copy lost closed state");
        var parked=Product.CatLevelSolver.VerifyPlan(Read(curtain),new(){Move(0,0,0,1,0)});
        Check(parked.finalState.holes[0].position.Equals(new Product.Cell(1,0)),"Closed curtain cannot park in empty cell");
        foreach(bool improved in new[]{false,true})
        {
            var result=improved?Product.CatLevelSolver.SolveCore(Read(curtain),CancellationToken.None):Product.LegacyLevelSolver.SolveCore(Read(curtain),CancellationToken.None);
            Check(result.status==Product.SolveStatus.Solved,"Curtain layered dependency failed");
            Check(Product.CatLevelSolver.VerifyPlan(Read(curtain),result.moves).status==Product.SolveStatus.Solved,"Curtain replay failed");
        }
        var area=new JObject{["sizeX"]=4,["sizeY"]=3,["holeInfos"]=new JArray(Hole(1,0,0),Hole(2,0,2),Hole(3,3,2)),
            ["catInfos"]=new JArray(Cat(1,1,0),Cat(2,1,2),Cat(3,2,2)),
            ["curtainAreaInfos"]=new JArray(new JObject{["position"]=new JObject{["x"]=0,["y"]=0},["sizeX"]=2,["sizeY"]=1,["requiredFinishHoleCount"]=2})};
        area["catInfos"][1]["pickaxeColorId"]=0;
        var one=Product.CatLevelSolver.VerifyPlan(Read(area),new(){Move(1,0,2,1,2)});
        Check(one.finalState.covers[0].remainingHits==1,"Pickaxe reduced curtain area counter");
        bool rejected=false;
        try{Product.CatLevelSolver.VerifyPlan(Read(area),new(){Move(1,0,2,1,2),Move(0,0,0,1,0)});}
        catch(ArgumentException){rejected=true;}
        Check(rejected,"Area allowed drag before required completions");
        foreach(bool improved in new[]{false,true})
        {
            var input=Read(area);
            Check(input.ignoredMechanics.Count==0 && input.Copy().covers[0].finishHoleCounter,"Area parser/copy failed");
            var result=improved?Product.CatLevelSolver.SolveCore(input,CancellationToken.None):Product.LegacyLevelSolver.SolveCore(input,CancellationToken.None);
            Check(result.status==Product.SolveStatus.Solved && result.finalState.covers[0].remainingHits==0,"Area completion chain failed");
            Check(Product.CatLevelSolver.VerifyPlan(Read(area),result.moves).status==Product.SolveStatus.Solved,"Area replay failed");
        }
        var priorityLevel=new JObject{["sizeX"]=5,["sizeY"]=2,
            ["holeInfos"]=new JArray(Hole(1,0,0),Hole(2,2,0),Hole(3,4,1)),
            ["catInfos"]=new JArray(Cat(1,1,0),Cat(1,4,0),Cat(2,2,1),Cat(3,3,1))};
        priorityLevel["holeInfos"][0]["numOfCats"]=new JArray(2);
        priorityLevel["holeInfos"][0]["isOpenClose"]=true;
        priorityLevel["holeInfos"][0]["isOpenCloseInitiallyClosed"]=false;
        var prioritized=Product.CatLevelSolver.SolveCore(Read(priorityLevel),CancellationToken.None);
        Check(prioritized.status==Product.SolveStatus.Solved && prioritized.moves.Count>0 && prioritized.moves[0].holeId==0,
            "Available curtain consumption was skipped in favor of ordinary completion");
        V3IndependentReplay.Verify(priorityLevel.ToString(),File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")),JArray.FromObject(prioritized.moves));
        Console.WriteLine("PASS curtain mechanics, consumption before closing, necessary helper completion and independent replay");
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
