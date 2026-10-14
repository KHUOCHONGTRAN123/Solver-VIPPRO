using System.Reflection;
using Product=CatDom.CoreSolver;

internal static class IdleConsumptionTests
{
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        int checkedStates=0;
        foreach(bool linked in new[]{false,true})
        {
            var b=new Product.BoardInput{width=6,height=2};
            b.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,isPlug=true,plugColorId=2,position=new(0,0),footprint=new[]{new Product.Cell(0,0)}});
            b.holes.Add(new Product.HoleInput{id=1,color=1,remaining=1,position=new(0,1),footprint=new[]{new Product.Cell(0,0)}});
            b.cats.Add(new Product.CatInput{id=0,color=0,position=new(5,0)});
            b.cats.Add(new Product.CatInput{id=1,color=1,position=new(5,1)});
            b.plugs.Add(new Product.PlugInput{id=0,color=2,position=new(2,0)});
            if(linked)b.links.Add(new Product.LinkInput{holeId1=0,holeId2=1});
            var e=Activator.CreateInstance(type,flags,null,new object[]{b,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
            object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(e,args);
            Call("Consume",-1,false);Call("Prepare");
            var current=type.GetField("current",flags);
            var moves=(System.Collections.IEnumerable)Call("Generate",current.GetValue(e));
            var saved=Call("SaveCheckpoint");
            foreach(var candidate in moves)
            {
                var ct=candidate.GetType();if((bool)ct.GetField("terminal",flags).GetValue(candidate))continue;
                saved.GetType().GetMethod("Restore",flags).Invoke(saved,new[]{e});
                current.SetValue(e,ct.GetField("state",flags).GetValue(candidate));
                string before=(string)Call("FullStateKey",false);
                var eaten=(List<Product.EatEvent>)Call("Consume",(int)ct.GetField("hole",flags).GetValue(candidate),false);
                if(eaten.Count!=0 || before!=(string)Call("FullStateKey",false))throw new Exception("Nonterminal parking unexpectedly changed consumption phase.");
                checkedStates++;
            }
        }
        if(checkedStates<4)throw new Exception("Idle consumption oracle exercised too few states.");
        Console.WriteLine($"PASS idle consumption oracle: {checkedStates} generated parking states, linked/unlinked plug and cats");
    }
}
