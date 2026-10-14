using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class HoleBombReplayOracle
{
    // Independent event-order invariant from Hole.OnActiveBomb/FinishHole.
    internal static void Verify(string json,IEnumerable<Product.EatEvent> events)
    {
        var holes=(JArray)JObject.Parse(json)["holeInfos"]??new JArray();
        var capacities=holes.Select(h=>((JArray)h["numOfCats"]??new JArray()).Sum(c=>(int)c)).ToArray();
        var bombs=holes.Select(h=>Math.Max(0,(int?)h["timeLimited"]??0)).ToArray();
        foreach(var eat in events)
        {
            if(eat.holeId<0||eat.holeId>=holes.Count||--capacities[eat.holeId]<0)
                throw new ArgumentException("Unknown/excess event in hole bomb oracle");
            if(capacities[eat.holeId]!=0)continue;
            for(int i=0;i<bombs.Length;i++)
                if(i!=eat.holeId&&capacities[i]>0&&bombs[i]>0&&--bombs[i]==0)
                    throw new ArgumentException($"Hole bomb {i} exhausted before completion");
        }
    }
}
