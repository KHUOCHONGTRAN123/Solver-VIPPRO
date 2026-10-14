using Newtonsoft.Json.Linq;

internal static class V3IndependentSpawnerTests
{
    internal static void Run(string root)
    {
        string catalog=File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json"));
        var origins=new[]{(0,1),(-1,0),(0,-1),(1,0)};
        const string fixture="""
        {"sizeX":3,"sizeY":4,"holeInfos":[{"position":{"x":2,"y":2},"holeType":10,"colorIds":[0],"numOfCats":[1]}],"catInfos":[{"position":{"x":2,"y":3},"colorId":0},{"position":{"x":1,"y":0},"colorId":1}],"holeSpawners":[{"direction":0,"position":{"x":0,"y":1},"holeInfos":[{"position":null,"holeType":10,"colorIds":[1],"numOfCats":[1]}]}]}
        """;
        var plan=JArray.Parse("""
        [{"holeId":0,"start":{"x":2,"y":2},"path":[{"x":2,"y":2},{"x":2,"y":3}]},{"holeId":1,"start":{"x":0,"y":0},"path":[{"x":0,"y":0},{"x":1,"y":0}]}]
        """);
        for(int direction=0;direction<4;direction++)
        {
            var input=JObject.Parse(fixture);var entry=input["holeSpawners"][0];
            entry["direction"]=direction;entry["position"]=new JObject{{"x",origins[direction].Item1},{"y",origins[direction].Item2}};
            V3IndependentReplay.Verify(input.ToString(),catalog,plan);
            input["catInfos"][1]["position"]=new JObject{{"x",0},{"y",0}};
            bool rejected=false;
            try{V3IndependentReplay.Verify(input.ToString(),catalog,plan);}catch(Exception){rejected=true;}
            if(!rejected)throw new Exception("Independent spawner replay accepted a birth over a live cat.");
        }
        Console.WriteLine("PASS independent spawner release, four anchor directions and blocked-birth mutations");
    }
}
