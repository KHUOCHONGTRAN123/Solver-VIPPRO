using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;
internal static class V3IndependentCoverTests
{
    internal static void Run(string root)
    {
        string catalog=File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json"));
        string fixture="""
        {"sizeX":4,"sizeY":3,"holeInfos":[{"holeType":10,"position":{"x":0,"y":0},"movementType":2,"colorIds":[1],"numOfCats":[1]},{"holeType":10,"position":{"x":0,"y":2},"colorIds":[100],"numOfCats":[2]}],"catInfos":[{"position":{"x":3,"y":0},"colorId":1},{"position":{"x":1,"y":2},"colorId":100},{"position":{"x":2,"y":2},"colorId":100}]}
        """;
        foreach(bool secret in new[]{false,true})
        {
            var input=JObject.Parse(fixture);
            string field=secret?"secretLockboxInfos":"barricades";
            input[field]=secret?JArray.Parse("[{\"position\":{\"x\":1,\"y\":0},\"sizeX\":2,\"sizeY\":1,\"colorIds\":[0,0],\"numCats\":[1,1]}]"):JArray.Parse("[{\"pos\":{\"x\":1,\"y\":0},\"direction\":1,\"range\":1}]");
            var result=Product.CatLevelSolver.SolveLevel(input.ToString());
            if(result.status!=Product.SolverStatus.Solved)throw new Exception("Cover oracle fixture did not solve.");
            var plan=JArray.FromObject(result.moves);
            V3IndependentReplay.Verify(input.ToString(),catalog,plan);
            if(secret)input[field][0]["numCats"]=new JArray(2,2);
            else input[field][0]["range"]=0;
            bool rejected=false;
            try{V3IndependentReplay.Verify(input.ToString(),catalog,plan);}catch(Exception){rejected=true;}
            if(!rejected)throw new Exception("Independent cover oracle accepted still-closed cover.");
        }
        Console.WriteLine("PASS independent barricade/secret replay and blocked-cover mutations");
    }
}
