using System.Reflection;
using Product=CatDom.CoreSolver;

internal static class PhaseStateKeyTests
{
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        var b=new Product.BoardInput{width=4,height=1};
        b.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,isPlug=true,plugColorId=0,countdownMoves=3,
            layerColors=new[]{0,1},layerCounts=new[]{1,1},position=new(0,0),footprint=new[]{new Product.Cell(0,0)}});
        b.holes.Add(new Product.HoleInput{id=1,color=2,remaining=1,pendingSpawn=true,
            layerColors=new[]{2},layerCounts=new[]{1},position=new(1,0),footprint=new[]{new Product.Cell(0,0)}});
        b.plugs.Add(new Product.PlugInput{id=0,color=0,position=new(1,0)});
        b.cats.Add(new Product.CatInput{id=0,color=0,position=new(2,0)});
        b.cats.Add(new Product.CatInput{id=1,color=1,position=new(3,0)});
        b.spawners.Add(new Product.SpawnerInput{position=new(2,0),direction=3,holeIds=new[]{1}});
        var e=Activator.CreateInstance(type,flags,null,new object[]{b,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        var active=type.GetField("fullDragSearchActive",flags);active.SetValue(e,true);
        object Invoke(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(e,args);
        string Key(bool positions)=>(string)Invoke("FullStateKey",positions);
        string Check(string phase)
        {
            string cached=Key(false);active.SetValue(e,false);
            string fresh=Key(false);active.SetValue(e,true);
            if(cached!=fresh)throw new Exception("Stale phase key: "+phase);
            if(!ReferenceEquals(cached,Key(false)))throw new Exception("Phase key was not reused: "+phase);
            return cached;
        }
        object Current()=>type.GetField("current",flags).GetValue(e);
        void Position(int anchor)
        {
            var positionType=type.GetField("current",flags).FieldType;
            type.GetField("current",flags).SetValue(e,Activator.CreateInstance(positionType,flags,null,new object[]{new[]{anchor,-1}},null));
        }
        string initial=Check("initial"),initialFull=Key(true);
        var checkpoint=Invoke("SaveCheckpoint");
        var start=Current();Position(1);
        if(Check("position only")!=initial || Key(true)==initialFull)throw new Exception("Position/phase distinction lost.");
        var eaten=Invoke("Consume",0,false);
        if(Check("plug removal")==initial)throw new Exception("Plug removal did not change phase.");
        Invoke("ResolveCountdownDrag",0,start,eaten);Check("plug drag countdown");
        start=Current();Position(2);eaten=Invoke("Consume",0,false);Check("cat consumption and layer change");
        Invoke("ResolveCountdownDrag",0,start,eaten);Check("cat drag countdown");
        start=Current();Position(3);eaten=Invoke("Consume",0,false);Check("hole finish");
        Invoke("ResolveCountdownDrag",0,start,eaten);Check("final drag countdown");
        Invoke("ResolveSpawnerRelease");Check("spawn queue release");
        if(b.holes[1].pendingSpawn || b.spawners[0].consumed!=1)throw new Exception("Phase key test did not exercise spawn.");
        checkpoint.GetType().GetMethod("Restore",flags).Invoke(checkpoint,new[]{e});
        if(Check("rollback")!=initial || Key(true)!=initialFull)throw new Exception("Phase key identity lost on rollback.");
        Console.WriteLine("PASS exact phase keys vs fresh: parking, plug, cats/layers, countdown, spawn and rollback");
    }
}
