using Product = CatDom.CoreSolver;

internal static class PlugTests
{
    internal static void Run()
    {
        Product.BoardInput Board(int plugColor, bool socket=true, int countdown=0)
        {
            var b=new Product.BoardInput{width=4,height=1};
            b.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,
                layerColors=new[]{0},layerCounts=new[]{1},position=new Product.Cell(0,0),
                footprint=new[]{new Product.Cell(0,0)},isPlug=socket,plugColorId=2,countdownMoves=countdown});
            b.plugs.Add(new Product.PlugInput{id=0,color=plugColor,position=new Product.Cell(1,0)});
            b.cats.Add(new Product.CatInput{id=0,color=0,position=new Product.Cell(3,0)});
            return b;
        }
        var b=Board(2);
        var r=Product.CatLevelSolver.SolveCore(b.Copy(),CancellationToken.None);
        if(r.status!=Product.SolveStatus.Solved || r.finalState.remainingPlugIds.Count!=0 || r.finalState.holes[0].isPlug)
            throw new Exception("Matching plug did not open socket and solve.");
        if(r.moves.SelectMany(m=>m.eaten).Count()!=1)
            throw new Exception("Plug incorrectly counted as a Cat completion.");
        var replay=Product.CatLevelSolver.VerifyPlan(b,r.moves);
        if(replay.status!=Product.SolveStatus.Solved)throw new Exception("Plug replay failed.");
        foreach(var blocked in new[]{Board(3),Board(2,false)})
            if(Product.CatLevelSolver.SolveCore(blocked,CancellationToken.None).status==Product.SolveStatus.Solved)
                throw new Exception("Wrong-color or ordinary hole passed a plug.");
        var singleDrag=Product.CatLevelSolver.SolveCore(Board(2,true,1),CancellationToken.None);
        if(singleDrag.status!=Product.SolveStatus.Solved || singleDrag.moves.Count!=1)
            throw new Exception("Plug unlock must allow continuation within one countdown drag.");
        const string json="""
        {"sizeX":4,"sizeY":1,"holeInfos":[{"position":{"x":0,"y":0},"holeType":10,"colorIds":[0],"numOfCats":[1],"isPlug":true,"plugColorId":2}],"catInfos":[{"position":{"x":3,"y":0},"colorId":0,"bonusTime":20,"timeLimited":15}],"plugs":[{"position":{"x":1,"y":0},"colorId":2}]}
        """;
        var api=Product.CatLevelSolver.SolveLevel(json);
        if(api.status!=Product.SolverStatus.Solved || api.searchAlgorithm!="improved" || api.baselineExpanded!=0)
            throw new Exception("Plug JSON/API dispatch or ignored Cat timers failed.");
        Console.WriteLine("PASS plug color blocking, socket release, capacity, replay and one-drag countdown");
    }
}
