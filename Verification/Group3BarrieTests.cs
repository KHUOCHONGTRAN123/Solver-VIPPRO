using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class Group3BarrieTests
{
    internal static void Run(string root)
    {
        string catalog=File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json"));
        const string json="""
        {"sizeX":4,"sizeY":2,"holeInfos":[
        {"position":{"x":0,"y":0},"holeType":10,"colorIds":[1],"numOfCats":[1]},
        {"position":{"x":0,"y":1},"holeType":10,"colorIds":[2],"numOfCats":[1]}],
        "catInfos":[{"position":{"x":3,"y":0},"colorId":1},{"position":{"x":3,"y":1},"colorId":2}],
        "barrieInfos":[{"id":2,"positions":[{"x":2,"y":0}],"switchPositions":[{"x":1,"y":1}]}]}
        """;
        var result=Product.CatLevelSolver.SolveLevel(json);
        if(result.status!=Product.SolverStatus.Solved || result.searchAlgorithm!="improved" || result.baselineExpanded!=0)
            throw new Exception("Barrie API must use supported full-state engine and solve fixture.");
        V3IndependentReplay.Verify(json,catalog,JArray.FromObject(result.moves));
        void Reject(JArray plan)
        {
            bool rejected=false;try{V3IndependentReplay.Verify(json,catalog,plan);}catch(Exception){rejected=true;}
            if(!rejected)throw new Exception("Independent barrie replay accepted invalid drag.");
        }
        JObject Move(int hole,params (int x,int y)[] cells)=>new JObject{["holeId"]=hole,["start"]=new JObject{["x"]=cells[0].x,["y"]=cells[0].y},
            ["path"]=new JArray(cells.Select(c=>new JObject{["x"]=c.x,["y"]=c.y}))};
        Reject(new JArray(Move(0,(0,0),(1,0),(2,0),(3,0))));
        Reject(new JArray(Move(0,(0,0),(1,0),(1,1))));
        var doubled=JObject.Parse(json);((JArray)doubled["barrieInfos"]).Add(new JObject{["id"]=2,["positions"]=new JArray(new JObject{["x"]=2,["y"]=0}),["switchPositions"]=new JArray()});
        var second=Product.CatLevelSolver.SolveLevel(doubled.ToString());
        if(second.status!=Product.SolverStatus.Solved)throw new Exception("Duplicate barrie group merge fixture failed.");
        V3IndependentReplay.Verify(doubled.ToString(),catalog,JArray.FromObject(second.moves));
        Console.WriteLine("PASS barrie API routing, switch/release collision, duplicate ID merge and independent replay");
    }
}
