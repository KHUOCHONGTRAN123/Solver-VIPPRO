using Product = CatDom.CoreSolver;

internal static class ExpandTests
{
    internal static void Run()
    {
        var hole=new Product.HoleInput{footprint=new[]{new Product.Cell(0,0),new Product.Cell(1,0)},expandCell=new Product.Cell(1,0)};
        var resting=Product.ExpandGeometry.Footprint(hole,false);
        if(resting.Length!=1 || !resting[0].Equals(new Product.Cell(1,0)))
            throw new Exception("Resting expand footprint must be its selected local cell.");
        if(!ReferenceEquals(Product.ExpandGeometry.Footprint(hole,true),hole.footprint))
            throw new Exception("Picked expand footprint must use complete shape.");
        hole.expandCell=new Product.Cell(-1,0);
        if(Product.ExpandGeometry.Enabled(hole) || !ReferenceEquals(Product.ExpandGeometry.Footprint(hole,false),hole.footprint))
            throw new Exception("Negative expand sentinel must keep ordinary footprint.");
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
        hole.expandCell=new Product.Cell(1,0);hole.remaining=1;
        var board=new Product.BoardInput{width=3,height=1};board.holes.Add(hole);
        board.holes.Add(new Product.HoleInput{id=1,remaining=1,footprint=new[]{new Product.Cell(0,0)}});
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",System.Reflection.BindingFlags.NonPublic);
        var engine=Activator.CreateInstance(type,flags,null,new object[]{board,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        type.GetMethod("Prepare",flags).Invoke(engine,null);
        var checkpoint=type.GetMethod("SaveCheckpoint",flags).Invoke(engine,null);
        var masks=(Product.CellMask[][])type.GetField("masks",flags).GetValue(engine);
        if(!masks[0][0].Equals(Product.CellMask.One<<1))throw new Exception("Engine collapsed mask mismatch.");
        type.GetMethod("SetExpandGroup",flags).Invoke(engine,new object[]{0,true});
        if(!masks[0][0].Equals(Product.CellMask.One | Product.CellMask.One<<1))throw new Exception("Engine expanded mask mismatch.");
        var state=type.GetField("current",flags).GetValue(engine);
        var other=(Product.CellMask)type.GetMethod("OccupancyExcept",flags).Invoke(engine,new object[]{state,new[]{0}});
        if(!other.Equals(Product.CellMask.One))throw new Exception("Expanded overlap must retain other-hole occupancy.");
        checkpoint.GetType().GetMethod("Restore",flags).Invoke(checkpoint,new[]{engine});
        if(hole.expandPicked || !masks[0][0].Equals(Product.CellMask.One<<1))throw new Exception("Expand phase restore mismatch.");
        Product.BoardInput Fixture(int catX,int countdown=0)
        {
            var b=new Product.BoardInput{width=4,height=1};
            b.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,position=new Product.Cell(0,0),footprint=new[]{new Product.Cell(0,0),new Product.Cell(1,0)},expandCell=new Product.Cell(1,0),layerColors=new[]{0},layerCounts=new[]{1},countdownMoves=countdown});
            b.cats.Add(new Product.CatInput{id=0,color=0,position=new Product.Cell(catX,0)});
            return b;
        }
        var original=Fixture(0,1);
        var pickup=Product.CatLevelSolver.SolveCore(original.Copy(),CancellationToken.None);
        if(pickup.status!=Product.SolveStatus.Solved || pickup.moves.Count!=1 || pickup.moves[0].path.Count!=1)
            throw new Exception("Expand pickup-only completion failed.");
        if(Product.CatLevelSolver.VerifyPlan(original,pickup.moves).status!=Product.SolveStatus.Solved)
            throw new Exception("Pickup-only replay failed.");
        original=Fixture(3);
        var dragged=Product.CatLevelSolver.SolveCore(original.Copy(),CancellationToken.None);
        if(dragged.status!=Product.SolveStatus.Solved || dragged.moves.Count==0 || dragged.moves[0].path.Count<2)
            throw new Exception("Picked expand movement failed.");
        if(Product.CatLevelSolver.VerifyPlan(original,dragged.moves).status!=Product.SolveStatus.Solved)
            throw new Exception("Expand movement replay failed.");
        var linked=Fixture(0);
        linked.holes[0].bombRemaining=1;
        linked.holes.Add(new Product.HoleInput{id=1,color=1,remaining=1,position=new Product.Cell(2,0),footprint=new[]{new Product.Cell(0,0),new Product.Cell(1,0)},expandCell=new Product.Cell(1,0),layerColors=new[]{1},layerCounts=new[]{1}});
        linked.cats.Add(new Product.CatInput{id=1,color=1,position=new Product.Cell(2,0)});
        linked.links.Add(new Product.LinkInput{holeId1=0,holeId2=1});
        var linkedResult=Product.CatLevelSolver.SolveCore(linked.Copy(),CancellationToken.None);
        if(linkedResult.status!=Product.SolveStatus.Solved || linkedResult.moves.Count!=1 || linkedResult.moves[0].eaten.Count!=2)
            throw new Exception("Linked expand pickup failed: "+linkedResult.status+" / "+linkedResult.message+" / moves="+linkedResult.moves.Count);
        bool rejected=false;
        try
        {
            Product.CatLevelSolver.VerifyPlan(linked,new List<Product.EngineMove>{new Product.EngineMove{holeId=1,start=new Product.Cell(2,0),path=new(){new Product.Cell(2,0)}}});
        }
        catch(ArgumentException){rejected=true;}
        if(!rejected)throw new Exception("Wrong linked pickup order must trigger bomb loss.");
        var edge=new Product.BoardInput{width=4,height=1};
        edge.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,position=new Product.Cell(2,0),expandCell=new Product.Cell(0,0),footprint=new[]{new Product.Cell(0,0),new Product.Cell(1,0),new Product.Cell(2,0)},layerColors=new[]{0},layerCounts=new[]{1}});
        edge.cats.Add(new Product.CatInput{id=0,color=0,position=new Product.Cell(3,0)});
        var edgeResult=Product.CatLevelSolver.SolveCore(edge.Copy(),CancellationToken.None);
        if(edgeResult.status!=Product.SolveStatus.Solved || edgeResult.moves.Count!=1 || edgeResult.moves[0].path.Count!=1)
            throw new Exception("Partially outside pickup must consume inside cells.");
        edge.cats[0].position=new Product.Cell(0,0);
        rejected=false;
        try {Product.CatLevelSolver.VerifyPlan(edge,new(){new Product.EngineMove{holeId=0,start=new(2,0),path=new(){new(2,0),new(3,0)}}});}
        catch(ArgumentException){rejected=true;}
        if(!rejected)throw new Exception("Expanded movement must keep complete shape inside board.");
        const string json="""
        {"sizeX":2,"sizeY":2,"holeInfos":[{"position":{"x":0,"y":0},"holeType":8,"colorIds":[0],"numOfCats":[1],"posExpand":{"x":0,"y":1}}],"catInfos":[{"position":{"x":0,"y":0},"colorId":0}]}
        """;
        var api=Product.CatLevelSolver.SolveLevel(json);
        if(api.status!=Product.SolverStatus.Solved || api.searchAlgorithm!="improved" || api.baselineExpanded!=0 || api.moves.Count!=1 || api.moves[0].path.Count!=1)
            throw new Exception("Expand JSON/API pickup-only dispatch failed.");
        var cachedBoard=new Product.BoardInput{width=4,height=2};
        cachedBoard.obstacles.Add(new Product.Cell(0,0));
        cachedBoard.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,position=new(0,0),footprint=new[]{new Product.Cell(0,0),new Product.Cell(1,0)},expandCell=new(1,0),layerColors=new[]{0},layerCounts=new[]{1}});
        cachedBoard.cats.Add(new Product.CatInput{id=0,color=0,position=new(3,1)});
        var cachedEngine=Activator.CreateInstance(type,flags,null,new object[]{cachedBoard,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        type.GetField("fullDragSearchActive",flags).SetValue(cachedEngine,true);
        var prepare=type.GetMethod("Prepare",flags);
        prepare.Invoke(cachedEngine,null);
        var collapsedValid=(bool[][])type.GetField("valid",flags).GetValue(cachedEngine);
        var key=type.GetMethod("FullStateKey",flags);
        string collapsedKey=(string)key.Invoke(cachedEngine,new object[]{false});
        var saved=type.GetMethod("SaveCheckpoint",flags).Invoke(cachedEngine,null);
        type.GetMethod("SetExpandGroup",flags).Invoke(cachedEngine,new object[]{0,true});
        prepare.Invoke(cachedEngine,null);
        var pickedValid=(bool[][])type.GetField("valid",flags).GetValue(cachedEngine);
        string pickedKey=(string)key.Invoke(cachedEngine,new object[]{false});
        if(ReferenceEquals(collapsedValid,pickedValid) || collapsedKey==pickedKey || !collapsedValid[0][0] || pickedValid[0][0])
            throw new Exception("Expand cache reused collapsed geometry/key for pickup over an obstacle.");
        saved.GetType().GetMethod("Restore",flags).Invoke(saved,new[]{cachedEngine});
        prepare.Invoke(cachedEngine,null);
        if(!ReferenceEquals(collapsedValid,type.GetField("valid",flags).GetValue(cachedEngine)) || collapsedKey!=(string)key.Invoke(cachedEngine,new object[]{false}))
            throw new Exception("Expand geometry/key rollback failed.");
        Console.WriteLine("PASS expand footprint modes, pickup/release replay and cached geometry rollback");
    }
}

