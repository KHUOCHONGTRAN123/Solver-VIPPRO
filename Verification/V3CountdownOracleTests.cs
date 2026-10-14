using Product=CatDom.CoreSolver;

internal static class V3CountdownOracleTests
{
    internal static void Run()
    {
        const string level="""
        {"holeInfos":[{"countdownMoveLimit":1,"numOfCats":[1,1]},
        {"countdownMoveLimit":2,"numOfCats":[1]}],"linkInfos":[{"idHole1":0,"idHole2":1}]}
        """;
        Product.EngineMove Move(params Product.Cell[] path)=>new(){holeId=0,path=path.ToList()};
        bool Rejected(Product.EngineMove move)
        {
            try{CountdownReplayOracle.Verify(level,new[]{move});return false;}
            catch(ArgumentException){return true;}
        }
        var still=Move(new(0,0),new(1,0),new(0,0));
        if(CountdownReplayOracle.Verify(level,new[]{still})[0]!=1)throw new Exception("Round trip without eating consumed countdown");
        var moved=Move(new(0,0),new(1,0));
        if(!Rejected(moved))throw new Exception("Unfinished zero counter allowed");
        var layer=Move(new(0,0),new(0,0));
        layer.eaten.Add(new(){holeId=0,color=0,catId=0});
        if(!Rejected(layer))throw new Exception("Layer promotion counted as finished hole");
        var finished=Move(new(0,0),new(1,0),new(2,0),new(3,0));
        finished.eaten.AddRange(new[]{new Product.EatEvent{holeId=0},new Product.EatEvent{holeId=0}});
        var counters=CountdownReplayOracle.Verify(level,new[]{finished});
        if(counters[0]!=0||counters[1]!=1)throw new Exception("Long drag charged per step or linked partner skipped");
        // In-place consumption charges only the linked member that actually ate.
        var inPlace=Move(new Product.Cell(0,0));
        inPlace.eaten.Add(new(){holeId=1,catId=0});
        counters=CountdownReplayOracle.Verify(level,new[]{inPlace});
        if(counters[0]!=1||counters[1]!=1)throw new Exception("In-place linked consumption charged wrong member");
        var boxLayer=Move(new(0,0),new(1,0),new(0,0));
        boxLayer.eaten.Add(new(){holeId=0,boxId=0,boxIndex=0});
        if(CountdownReplayOracle.Verify(level,new[]{boxLayer})[0]!=1)throw new Exception("Box-only roundtrip charged as DropCat");
        const string solvable="""
        {"sizeX":3,"sizeY":1,"holeInfos":[{"holeType":10,"position":{"x":0,"y":0},"colorIds":[1],"numOfCats":[1],"countdownMoveLimit":1}],
        "catInfos":[{"position":{"x":2,"y":0},"colorId":1}]}
        """;
        var result=Product.CatLevelSolver.SolveLevel(solvable);
        if(result.status!=Product.SolverStatus.Solved || !result.searchAlgorithm.StartsWith("countdown-relaxed+"))throw new Exception("Relaxed one-drag countdown solve failed");
        CountdownReplayOracle.Verify(solvable,result.moves.Select(m=>new Product.EngineMove{holeId=m.holeId,start=m.start,path=m.path,eaten=m.eaten}).ToArray(),result.diagnosticInitialEaten);
        const string impossible="""
        {"sizeX":3,"sizeY":1,"holeInfos":[{"holeType":10,"position":{"x":0,"y":0},"colorIds":[1],"numOfCats":[2],"countdownMoveLimit":1}],
        "catInfos":[{"position":{"x":1,"y":0},"colorId":1},{"position":{"x":2,"y":0},"colorId":1}]}
        """;
        var constrained=Product.CatLevelSolver.SolveLevel(impossible);
        if(constrained.status!=Product.SolverStatus.CountdownExceeded || constrained.moves.Count==0 || constrained.searchAlgorithm.Contains("fallback"))
            throw new Exception("Exceeded relaxed plan was not reported without retry");
        bool exceededRejected=false;
        try{CountdownReplayOracle.Verify(impossible,constrained.moves.Select(m=>new Product.EngineMove{holeId=m.holeId,start=m.start,path=m.path,eaten=m.eaten}).ToArray(),constrained.diagnosticInitialEaten);}
        catch(ArgumentException){exceededRejected=true;}
        if(!exceededRejected)throw new Exception("Reported countdown excess was not independently confirmed");
        Console.WriteLine("PASS independent countdown oracle: drag boundary, roundtrip, linked members, tunnel and finish-at-zero");
    }
}
