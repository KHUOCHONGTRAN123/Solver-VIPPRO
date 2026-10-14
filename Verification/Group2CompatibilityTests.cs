using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class Group2CompatibilityTests
{
    internal static void Run(string root)
    {
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var data=JObject.Parse("""
        {"sizeX":3,"sizeY":2,"countdownMoveLimit":1,
         "holeInfos":[{"holeType":10,"position":{"x":0,"y":0},"colorIds":[1],"numOfCats":[1]},
                      {"holeType":10,"position":{"x":0,"y":1},"colorIds":[2],"numOfCats":[1]}],
         "catInfos":[{"position":{"x":1,"y":0},"colorId":1},{"position":{"x":1,"y":1},"colorId":2}]}
        """);
        Product.BoardInput Read()=>Product.IO.SolverJson.ReadLevel(data.ToString(),catalog);
        var input=Read();
        if(input.holes[0].countdownMoves!=1 || input.holes[1].countdownMoves!=0 || input.ignoredMechanics.Count!=0)
            throw new Exception("Legacy level countdown must attach to first hole.");
        var result=Product.CatLevelSolver.SolveLevel(data.ToString());
        if(result.status!=Product.SolverStatus.Solved)throw new Exception("Legacy countdown solve failed.");
        data["holeInfos"][1]["countdownMoveLimit"]=2;
        input=Read();
        if(input.holes[0].countdownMoves!=0 || input.holes[1].countdownMoves!=2)
            throw new Exception("Level countdown must not override explicit hole countdown.");
        var spawner=JObject.Parse("""
        {"sizeX":3,"sizeY":1,
         "holeInfos":[{"holeType":10,"position":{"x":0,"y":0},"colorIds":[1000],"numOfCats":[1],"movementType":1,"countdownMoveLimit":2}],
         "catInfos":[{"position":{"x":2,"y":0},"colorId":1}],
         "holeSpawners":[{"position":{"x":1,"y":-1},"direction":2,"holeInfos":[{"holeType":10,"position":null,"colorIds":[1],"numOfCats":[1]}]}]}
        """);
        var spawnInput=Product.IO.SolverJson.ReadLevel(spawner.ToString(),catalog);
        var spawned=Product.CatLevelSolver.SolveCore(spawnInput.Copy(),CancellationToken.None);
        if(spawned.status!=Product.SolveStatus.Solved || spawned.moves.Count==0 || spawned.moves[0].path.Count!=1)
            throw new Exception("Spawner must support release without changing position.");
        var replay=Product.CatLevelSolver.VerifyPlan(spawnInput,spawned.moves);
        if(replay.status!=Product.SolveStatus.Solved || replay.finalState.holes[0].countdownMoves!=2)
            throw new Exception("Stationary release replay/countdown charging failed.");
        Console.WriteLine("PASS group2 level countdown fallback and explicit-hole precedence");
        Console.WriteLine("PASS group2 stationary spawner release and unchanged countdown");
    }
}
