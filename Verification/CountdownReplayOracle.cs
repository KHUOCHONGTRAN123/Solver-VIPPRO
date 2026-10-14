using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

// Independent of product parser/state/search. Checks the drag-boundary contract
// from CountdownHole.BeginDrag/ResolveDrag/TriggerLose using original JSON.
// This is one acceptance invariant, not a complete collision/game verifier.
internal static class CountdownReplayOracle
{
    internal static int[] Verify(string json, IReadOnlyList<Product.EngineMove> moves,
        IReadOnlyList<Product.EatEvent> initialEaten = null)
    {
        var level=JObject.Parse(json);
        var holes=(JArray)level["holeInfos"] ?? new JArray();
        int count=holes.Count;
        var counters=new int[count];
        var active=new bool[count];
        var pending=new int[count];
        var groups=Enumerable.Range(0,count).ToArray();
        for(int i=0;i<count;i++)
        {
            counters[i]=Math.Max(0,(int?)holes[i]["countdownMoveLimit"]??0);
            active[i]=counters[i]>0;
            pending[i]=((JArray)holes[i]["numOfCats"]??new JArray()).Sum(t=>(int)t);
            if(pending[i]<=0)throw new ArgumentException("Oracle requires positive hole capacities");
        }
        foreach(var link in (JArray)level["linkInfos"]??new JArray())
        {
            int a=(int)link["idHole1"],b=(int)link["idHole2"];
            if(a<0||b<0||a>=count||b>=count)throw new ArgumentException("Invalid link in countdown oracle");
            int old=groups[b],replacement=groups[a];
            for(int i=0;i<count;i++)if(groups[i]==old)groups[i]=replacement;
        }
        void Eat(Product.EatEvent eat)
        {
            if(eat.holeId<0||eat.holeId>=count || --pending[eat.holeId]<0)
                throw new ArgumentException("Unknown/excess consumption in countdown oracle");
        }
        foreach(var eat in initialEaten??Array.Empty<Product.EatEvent>())Eat(eat);
        foreach(var move in moves)
        {
            if(move.holeId<0||move.holeId>=count||pending[move.holeId]<=0||move.path.Count==0)
                throw new ArgumentException("Invalid drag in countdown oracle");
            var participating=new bool[count];
            for(int i=0;i<count;i++)participating[i]=groups[i]==groups[move.holeId] && pending[i]>0;
            var ate=new bool[count];
            foreach(var eat in move.eaten)
            {
                Eat(eat);
                // Hole.DropCat sets isDropCat; DropBoxCat/DropTower do not.
                if(eat.catId>=0)ate[eat.holeId]=true;
            }
            bool changedPosition=!move.path[0].Equals(move.path[^1]);
            for(int i=0;i<count;i++)
            {
                if(!participating[i]||!active[i]||(!changedPosition&&!ate[i]))continue;
                counters[i]=Math.Max(0,counters[i]-1);
                if(counters[i]==0&&pending[i]>0)
                    throw new ArgumentException($"Countdown exhausted before hole {i} finished");
            }
        }
        return counters;
    }
}
