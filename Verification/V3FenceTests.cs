using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class V3FenceTests
{
    internal static void Run(string root)
    {
        JObject Hole(int color,int x,int y)=>JObject.FromObject(new{colorIds=new[]{color},numOfCats=new[]{1},holeType=10,position=new{x,y}});
        JObject Cat(int color,int x,int y)=>JObject.FromObject(new{colorId=color,position=new{x,y}});
        JObject Fence(int color,int x,int y,int direction)=>JObject.FromObject(new{colorId=color,positions=new[]{new{x,y}},directions=new[]{direction}});
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var axe=new JObject{["sizeX"]=3,["sizeY"]=3,
            ["holeInfos"]=new JArray(Hole(1,0,0),Hole(0,0,2)),
            ["catInfos"]=new JArray(Cat(1,2,0),Cat(0,1,2)),
            ["fenceInfos"]=new JArray(Fence(0,1,0,3))};
        axe["holeInfos"][0]["movementType"]=2;
        axe["holeInfos"][1]["axe"]=true;
        foreach(bool improved in new[]{false,true})
        {
            var input=Product.IO.SolverJson.ReadLevel(axe.ToString(),catalog);
            Check(input.ignoredMechanics.Count==0 && input.Copy().holes[1].axe,"Axe parser/copy failed");
            var result=improved?Product.CatLevelSolver.SolveCore(input,CancellationToken.None):Product.LegacyLevelSolver.SolveCore(input,CancellationToken.None);
            Check(result.status==Product.SolveStatus.Solved,"Axe could not unlock fence corridor");
            Check(result.moves.First(m=>m.eaten.Count>0).holeId==1,"Fence crossed before axe hole completed");
            Check(result.finalState.fenceEdgesBlocked.All(b=>!b),"Fence state was not released");
            Check(Product.CatLevelSolver.VerifyPlan(Product.IO.SolverJson.ReadLevel(axe.ToString(),catalog),result.moves).status==Product.SolveStatus.Solved,"Fence replay failed");
        }
        var illegal=new Product.EngineMove{holeId=0,start=new(0,0),path=new(){new(0,0),new(1,0)}};
        bool rejected=false;
        try{Product.CatLevelSolver.VerifyPlan(Product.IO.SolverJson.ReadLevel(axe.ToString(),catalog),new(){illegal});}
        catch(ArgumentException){rejected=true;}
        Check(rejected,"Reverse registration did not block replay crossing");
        axe["holeInfos"][1]["axe"]=false;
        Check(Product.CatLevelSolver.SolveLevel(axe.ToString()).status==Product.SolverStatus.NoNextCatReachable,"Fence released without axe");

        // Parallel translation never crosses the edge, but would place a
        // two-cell footprint astride it. This move must still be rejected.
        var edges=new List<Product.FenceEdgeInput>{new(){position=new(0,1),direction=1,color=0},new(){position=new(0,1),direction=1,color=2},new(){position=new(1,1),direction=3,color=3}};
        var topology=new Product.FenceTopology(edges);
        Check(!topology.AllowsMove(new[]{new Product.Cell(0,0),new Product.Cell(1,0)},new(0,0),new(0,1)),"Internal destination fence missed");
        topology.ReleaseColor(0);
        Check(!edges[0].blocked && !edges[1].blocked && edges[2].blocked,"Shared raw registration release differs from runtime HashSet");
        Check(topology.Blocks(new(0,1),new(1,1)),"Opposite registration was incorrectly removed");
        topology.ReleaseColor(3);
        Check(topology.AllowsMove(new[]{new Product.Cell(0,0),new Product.Cell(1,0)},new(0,0),new(0,1)),"Released fence still blocks footprint");

        var box=new JObject{["sizeX"]=2,["sizeY"]=1,["holeInfos"]=new JArray(Hole(1,0,0)),
            ["catBoxInfos"]=new JArray(new JObject{["position"]=new JObject{["x"]=1,["y"]=0},["direction"]=1,["colorIds"]=new JArray(Cat(1,1,0))}),
            ["fenceInfos"]=new JArray(Fence(-1,1,0,3))};
        Check(Product.CatLevelSolver.SolveLevel(box.ToString()).status==Product.SolverStatus.NoNextCatReachable,"Box cat passed through fence");
        box.Remove("fenceInfos");
        Check(Product.CatLevelSolver.SolveLevel(box.ToString()).status==Product.SolverStatus.Solved,"Unfenced initial box drop failed");
        // Independent edge oracle: compare geometric undirected segments with
        // registrations at either endpoint across every translation on 3x3.
        int compared=0;
        var deltas=new[]{new Product.Cell(0,1),new Product.Cell(1,0),new Product.Cell(0,-1),new Product.Cell(-1,0)};
        foreach(var shape in new[]{new[]{new Product.Cell(0,0)},new[]{new Product.Cell(0,0),new Product.Cell(1,0)},new[]{new Product.Cell(0,0),new Product.Cell(0,1),new Product.Cell(1,1)}})
        for(int cell=0;cell<9;cell++)
        for(int direction=0;direction<4;direction++)
        for(int origin=0;origin<9;origin++)
        foreach(var delta in deltas)
        {
            var edgeA=new Product.Cell(cell%3,cell/3);
            var edgeB=new Product.Cell(edgeA.x+deltas[direction].x,edgeA.y+deltas[direction].y);
            var from=new Product.Cell(origin%3,origin/3);
            var to=new Product.Cell(from.x+delta.x,from.y+delta.y);
            bool SameEdge(Product.Cell a,Product.Cell b)=>a.Equals(edgeA)&&b.Equals(edgeB)||a.Equals(edgeB)&&b.Equals(edgeA);
            bool expected=true;
            var destinations=shape.Select(o=>new Product.Cell(to.x+o.x,to.y+o.y)).ToArray();
            foreach(var offset in shape)
                if(SameEdge(new(from.x+offset.x,from.y+offset.y),new(to.x+offset.x,to.y+offset.y)))expected=false;
            for(int i=0;i<destinations.Length;i++)
                for(int j=i+1;j<destinations.Length;j++)
                    if(SameEdge(destinations[i],destinations[j]))expected=false;
            var actual=new Product.FenceTopology(new(){new(){position=edgeA,direction=direction}})
                .AllowsMove(shape,from,to);
            Check(actual==expected,"Fence geometry oracle disagreed");
            compared++;
        }
        Console.WriteLine($"PASS {compared} fence translations against independent segment oracle");
        Console.WriteLine("PASS fence/axe corridor, opposite/shared registrations, internal footprint and box adjacency");
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
