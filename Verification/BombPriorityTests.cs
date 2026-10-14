using System.Reflection;
using Product=CatDom.CoreSolver;
internal static class BombPriorityTests
{
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var board=new Product.BoardInput{width=7,height=3};
        for(int i=0;i<3;i++)board.holes.Add(new Product.HoleInput{id=i,color=i,remaining=2,position=new(i*2,0),footprint=new[]{new Product.Cell(0,0)},layerColors=new[]{i},layerCounts=new[]{2},bombRemaining=i==0?2:0});
        board.boxes.Add(new Product.BoxInput{id=0,position=new(6,2),mouth=new(5,2),direction=1,colors=new[]{1,0,0}});
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        var engine=Activator.CreateInstance(type,flags,null,new object[]{board,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        type.GetMethod("Prepare",flags).Invoke(engine,null);
        var candidate=typeof(Product.CatLevelSolver).GetNestedType("Candidate",BindingFlags.NonPublic);
        int Priority(int hole)
        {
            var move=Activator.CreateInstance(candidate,true);
            candidate.GetField("hole",flags).SetValue(move,hole);
            candidate.GetField("destination",flags).SetValue(move,hole*2);
            return (int)type.GetMethod("BombEventPriority",flags).Invoke(engine,new[]{move});
        }
        if(!(Priority(0)<Priority(1) && Priority(1)<Priority(2)))throw new Exception("Bomb/queue helper/unrelated priority incorrect.");
        bool focused=(bool)type.GetMethod("FindExpandEvent",flags).Invoke(engine,new object[]{false,false,0,-1,false});
        if(focused)throw new Exception("Bomb-focused parking must not commit a helper drop while the bomb queue is blocked.");
        var finishBoard=new Product.BoardInput{width=3,height=1};
        finishBoard.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,bombRemaining=2,position=new(0,0),footprint=new[]{new Product.Cell(0,0)},layerColors=new[]{0},layerCounts=new[]{1}});
        finishBoard.cats.Add(new Product.CatInput{id=0,color=0,position=new(2,0)});
        var finishEngine=Activator.CreateInstance(type,flags,null,new object[]{finishBoard,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        if(!(bool)type.GetMethod("FindExpandEvent",flags).Invoke(finishEngine,new object[]{false,false,0,2,false}))
            throw new Exception("A fixed destination must allow the bomb to finish and disappear.");
        var eventBoard=new Product.BoardInput{width=5,height=2};
        eventBoard.holes.Add(new Product.HoleInput{id=0,color=0,remaining=2,bombRemaining=2,position=new(0,0),footprint=new[]{new Product.Cell(0,0)},layerColors=new[]{0},layerCounts=new[]{2}});
        eventBoard.holes.Add(new Product.HoleInput{id=1,color=1,remaining=1,position=new(2,0),footprint=new[]{new Product.Cell(0,0)},layerColors=new[]{1},layerCounts=new[]{1}});
        eventBoard.boxes.Add(new Product.BoxInput{id=0,position=new(5,1),mouth=new(4,1),direction=1,colors=new[]{1,0,0}});
        var eventEngine=Activator.CreateInstance(type,flags,null,new object[]{eventBoard,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        if(!(bool)type.GetMethod("TryBombEventPlan",flags).Invoke(eventEngine,null))
            throw new Exception("Event planning must clear the helper prefix then finish the bomb before its deadline.");
        long Rank()=>(long)type.GetMethod("BombDragRank",flags).Invoke(engine,new object[]{0});
        board.holes[0].bombRemaining=6;
        long ampleBudget=Rank();board.boxes[0].colors=new[]{0,0};board.holes[0].bombRemaining=5;
        if(Rank()>=ampleBudget)throw new Exception("A helper completion opening needed bomb cats must improve rank when the bomb has ample budget.");
        board.boxes[0].colors=new[]{1,0,0};board.holes[0].bombRemaining=2;
        long initial=Rank();board.boxes[0].colors=new[]{0,0};
        if(Rank()>=initial)throw new Exception("Opening bomb queue must improve rank.");
        board.boxes[0].colors=new[]{0,1,0};
        long secondBlocked=Rank();
        board.boxes[0].colors=new[]{0,0};
        if(Rank()>=secondBlocked)throw new Exception("Opening the second required bomb cat must improve rank even when the first is already exposed.");
        board.boxes[0].colors=new[]{1,0,0};board.holes[0].bombRemaining=1;
        if(Rank()<=initial)throw new Exception("Spending bomb budget must worsen rank.");
        board.holes[0].bombRemaining=2;
        board.cats.Add(new Product.CatInput{id=0,color=0,numIced=3,position=new(6,1)});
        var icedEngine=Activator.CreateInstance(type,flags,null,new object[]{board,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        type.GetMethod("Prepare",flags).Invoke(icedEngine,null);
        long beforeIce=(long)type.GetMethod("BombDragRank",flags).Invoke(icedEngine,new object[]{0});
        board.cats[0].numIced--;
        long afterIce=(long)type.GetMethod("BombDragRank",flags).Invoke(icedEngine,new object[]{0});
        if(afterIce>=beforeIce)throw new Exception("Thawing a required bomb cat must improve rank.");
        var supplyBoard=new Product.BoardInput{width=5,height=5};
        supplyBoard.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,bombRemaining=1,position=new(1,0),footprint=new[]{new Product.Cell(1,0),new Product.Cell(0,1),new Product.Cell(1,1),new Product.Cell(2,1),new Product.Cell(1,2)},layerColors=new[]{0},layerCounts=new[]{1}});
        supplyBoard.holes.Add(new Product.HoleInput{id=1,color=1,remaining=1,position=new(0,4),footprint=new[]{new Product.Cell(0,0)},layerColors=new[]{1},layerCounts=new[]{1}});
        supplyBoard.cats.Add(new Product.CatInput{id=0,color=1,position=new(3,3)});
        supplyBoard.boxes.Add(new Product.BoxInput{id=0,position=new(5,2),mouth=new(4,2),direction=1,colors=new[]{0}});
        var supplyEngine=Activator.CreateInstance(type,flags,null,new object[]{supplyBoard,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        bool Fits()=>(bool)type.GetMethod("BombSourceSupplyPossible",flags).Invoke(supplyEngine,null);
        if(Fits())throw new Exception("Bomb source wrongly ignores forced finish at footprint blocker.");
        supplyBoard.holes[0].bombRemaining=2;
        if(!Fits())throw new Exception("Bomb source budget relaxation wrongly rejects clearable blocker.");
        type.GetMethod("Prepare",flags).Invoke(supplyEngine,null);
        var blockerMove=Activator.CreateInstance(candidate,true);
        candidate.GetField("hole",flags).SetValue(blockerMove,1);
        candidate.GetField("destination",flags).SetValue(blockerMove,20);
        if((int)type.GetMethod("BombEventPriority",flags).Invoke(supplyEngine,new[]{blockerMove})!=1)
            throw new Exception("Clearing an unavoidable bomb-source footprint blocker must be a helpful drop.");
        Console.WriteLine("PASS bomb consumption, queue-helpful drops and budget pressure priorities");
    }
}

