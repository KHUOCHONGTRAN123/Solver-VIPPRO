using Product = CatDom.CoreSolver;

internal static class SpawnerTests
{
    internal static void Run()
    {
        var shape=new[]{new Product.Cell(1,0),new Product.Cell(2,2)};
        var position=new Product.Cell(5,6);
        var expected=new[]{new Product.Cell(5,3),new Product.Cell(6,6),new Product.Cell(5,7),new Product.Cell(2,6)};
        for(int direction=0;direction<4;direction++)
            if(!Product.SpawnerGeometry.Anchor(position,direction,shape).Equals(expected[direction]))
                throw new Exception("Spawner direction/maximum-coordinate anchor mismatch.");
        var board=new Product.BoardInput{width=2,height=1};
        board.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,position=new Product.Cell(0,0),
            footprint=new[]{new Product.Cell(0,0)},layerColors=new[]{0},layerCounts=new[]{1},pendingSpawn=true});
        board.spawners.Add(new Product.SpawnerInput{position=new Product.Cell(-1,0),direction=1,holeIds=new[]{0}});
        var copy=board.Copy();
        copy.spawners[0].holeIds[0]=99;
        if(board.spawners[0].holeIds[0]!=0 || !copy.holes[0].pendingSpawn)
            throw new Exception("Spawner queue deep copy failed.");
        var result=Product.CatLevelSolver.SolveCore(board.Copy(),CancellationToken.None);
        if(result.status==Product.SolveStatus.Solved || result.finalState.holes[0].finished || !result.finalState.holes[0].pendingSpawn)
            throw new Exception("Pending spawn must differ from finished and prevent a false win.");
        var sequence=new Product.BoardInput{width=4,height=1};
        sequence.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,position=new Product.Cell(0,0),footprint=new[]{new Product.Cell(0,0)},layerColors=new[]{0},layerCounts=new[]{1}});
        sequence.holes.Add(new Product.HoleInput{id=1,color=1,remaining=1,position=new Product.Cell(2,0),footprint=new[]{new Product.Cell(0,0)},layerColors=new[]{1},layerCounts=new[]{1},pendingSpawn=true});
        sequence.spawners.Add(new Product.SpawnerInput{position=new Product.Cell(2,-1),direction=2,holeIds=new[]{1}});
        sequence.cats.Add(new Product.CatInput{id=0,color=0,position=new Product.Cell(1,0)});
        sequence.cats.Add(new Product.CatInput{id=1,color=1,position=new Product.Cell(3,0)});
        var original=sequence.Copy();
        var solved=Product.CatLevelSolver.SolveCore(sequence,CancellationToken.None);
        if(solved.status!=Product.SolveStatus.Solved || solved.finalState.spawnerOffsets[0]!=1 || solved.moves.Count!=2)
            throw new Exception("Released spawn did not solve queued hole.");
        if(Product.CatLevelSolver.VerifyPlan(original,solved.moves).status!=Product.SolveStatus.Solved)
            throw new Exception("Spawn settlement replay failed.");
        var blocked=original.Copy();
        blocked.covers.Add(new Product.CoverInput{id=0,finishHoleCounter=true,remainingHits=2,cells=new[]{new Product.Cell(2,0)}});
        if(Product.CatLevelSolver.SolveCore(blocked,CancellationToken.None).status==Product.SolveStatus.Solved)
            throw new Exception("Spawner ignored a live curtain area.");
        blocked=original.Copy();
        blocked.holes[1].position=new Product.Cell(3,0);
        if(Product.CatLevelSolver.SolveCore(blocked,CancellationToken.None).status==Product.SolveStatus.Solved)
            throw new Exception("Spawner spawned over a live Cat.");
        const string json="""
        {"sizeX":4,"sizeY":1,"holeInfos":[{"position":{"x":0,"y":0},"holeType":10,"colorIds":[0],"numOfCats":[1]}],"catInfos":[{"position":{"x":1,"y":0},"colorId":0},{"position":{"x":3,"y":0},"colorId":1}],"holeSpawners":[{"direction":2,"position":{"x":2,"y":-1},"holeInfos":[{"position":null,"holeType":10,"colorIds":[1],"numOfCats":[1]}]}]}
        """;
        var api=Product.CatLevelSolver.SolveLevel(json);
        if(api.status!=Product.SolverStatus.Solved || api.searchAlgorithm!="improved" || api.baselineExpanded!=0)
            throw new Exception("Queued JSON null position normalization/API dispatch failed.");
        Console.WriteLine("PASS spawner anchor directions, queue deep copy and pending win distinction");
    }
}
