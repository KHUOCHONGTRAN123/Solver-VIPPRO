using System.Reflection;
using Newtonsoft.Json;
using Product=CatDom.CoreSolver;

internal static class PreparedPhaseCacheTests
{
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        var board=new Product.BoardInput{width=4,height=1};
        board.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,position=new(0,0),footprint=new[]{new Product.Cell(0,0)}});
        board.holes.Add(new Product.HoleInput{id=1,color=1,remaining=1,position=new(3,0),footprint=new[]{new Product.Cell(0,0)}});
        board.cats.Add(new Product.CatInput{id=0,color=0,position=new(1,0)});
        board.cats.Add(new Product.CatInput{id=1,color=1,position=new(2,0)});
        board.covers.Add(new Product.CoverInput{id=0,rope=true,remainingHits=1,cells=new[]{new Product.Cell(2,0)}});
        board.fences.Add(new Product.FenceEdgeInput{position=new(1,0),direction=1,color=0});
        board.holes[1].isOpenClose=true;
        board.holes[1].bombRemaining=3;
        board.holes[1].countdownMoves=4;
        object New(Product.BoardInput b)=>Activator.CreateInstance(type,flags,null,new object[]{b,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        var engine=New(board);
        if(!(bool)type.GetField("preparedCacheEnabled",flags).GetValue(engine))throw new Exception("Phase cache fixture was not enabled.");
        var prepare=type.GetMethod("Prepare",flags);
        foreach(int phase in new[]{0,1,2,3,4,5,0,2,4,1})
        {
            board.covers[0].remainingHits=phase==0?1:0;
            board.fences[0].blocked=phase<2;
            board.holes[0].numIced=phase>=4?1:0;
            board.holes[1].closed=phase%2==0;
            board.holes[1].bombRemaining=3-phase%3;
            board.holes[1].countdownMoves=4-phase%4;
            var alive=(bool[])type.GetField("catsAlive",flags).GetValue(engine);alive[0]=phase<3;
            int[] positions=phase==5?new[]{1,3}:new[]{0,3};
            var positionType=type.GetField("current",flags).FieldType;
            type.GetField("current",flags).SetValue(engine,Activator.CreateInstance(positionType,flags,null,new object[]{positions},null));
            prepare.Invoke(engine,null);
            var fresh=New(board.Copy());
            type.GetField("preparedCacheEnabled",flags).SetValue(fresh,false);
            ((bool[])type.GetField("catsAlive",flags).GetValue(fresh))[0]=alive[0];
            type.GetField("current",flags).SetValue(fresh,Activator.CreateInstance(positionType,flags,null,new object[]{positions},null));
            prepare.Invoke(fresh,null);
            foreach(string field in new[]{"valid","goals","distances","goalAnchors","routeFixedMask"})
                if(JsonConvert.SerializeObject(type.GetField(field,flags).GetValue(engine))!=JsonConvert.SerializeObject(type.GetField(field,flags).GetValue(fresh)))
                    throw new Exception("Prepared cache differs from fresh computation: "+field+" phase="+phase);
        }
        Console.WriteLine("PASS prepared phase cache vs fresh tables: rope/fence/cat/ice, positions and rollback");
        var dynamicBoard=new Product.BoardInput{width=4,height=2};
        dynamicBoard.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,isPlug=true,plugColorId=0,
            position=new(0,0),footprint=new[]{new Product.Cell(0,0)}});
        dynamicBoard.holes.Add(new Product.HoleInput{id=1,color=1,remaining=1,position=new(3,1),footprint=new[]{new Product.Cell(0,0)}});
        dynamicBoard.plugs.Add(new Product.PlugInput{id=0,color=0,position=new(3,0)});
        dynamicBoard.covers.Add(new Product.CoverInput{id=0,rope=true,remainingHits=1,cells=new[]{new Product.Cell(1,0)}});
        dynamicBoard.spawners.Add(new Product.SpawnerInput{position=new(2,0),direction=0});
        var dynamicEngine=New(dynamicBoard);
        foreach(var positions in new[]{new[]{0,7},new[]{1,7},new[]{2,7},new[]{0,2},new[]{0,7},new[]{1,7},new[]{2,7}})
        foreach(bool alive in new[]{true,false,true})
        {
            dynamicBoard.plugs[0].alive=alive;
            var positionType=type.GetField("current",flags).FieldType;
            type.GetField("current",flags).SetValue(dynamicEngine,Activator.CreateInstance(positionType,flags,null,new object[]{positions},null));
            prepare.Invoke(dynamicEngine,null);
            var fresh=New(dynamicBoard.Copy());
            type.GetField("preparedCacheEnabled",flags).SetValue(fresh,false);
            type.GetField("current",flags).SetValue(fresh,Activator.CreateInstance(positionType,flags,null,new object[]{positions},null));
            prepare.Invoke(fresh,null);
            foreach(string field in new[]{"valid","goals","distances","goalAnchors","routeFixedMask"})
                if(JsonConvert.SerializeObject(type.GetField(field,flags).GetValue(dynamicEngine))!=JsonConvert.SerializeObject(type.GetField(field,flags).GetValue(fresh)))
                    throw new Exception("Plug/spawner phase cache differs from fresh: "+field);
        }
        Console.WriteLine("PASS plug/spawner phase cache vs fresh: covered anchor, socket state, machine-cell occupancy and rollback");
    }
}
