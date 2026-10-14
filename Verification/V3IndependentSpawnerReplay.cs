using Newtonsoft.Json.Linq;

internal sealed partial class V3IndependentReplay
{
    private sealed class ReplaySpawner
    {
        internal int[] Queue;
        internal int Offset;
    }
    private readonly List<ReplaySpawner> spawners=new();
    private void ReadIndependentSpawners(JObject root,JObject catalog)
    {
        var shapes=Items(catalog["shapes"]).ToDictionary(s=>Number(s,"holeType"),s=>Items(s["footprint"]).Select(Position).ToArray());
        foreach(var entry in Items(root["holeSpawners"]))
        {
            Fields((JObject)entry,"spawner","position","direction","holeInfos");
            int direction=Number(entry,"direction");
            if(direction<0 || direction>3)throw new Exception("Independent replay invalid spawner direction.");
            var origin=Position(entry["position"]);var queue=new List<int>();
            foreach(var token in Items(entry["holeInfos"]))
            {
                var item=(JObject)token.DeepClone();var shape=shapes[Number(item,"holeType")];
                var anchor=direction switch
                {
                    0=>origin+new Point(0,-1-shape.Max(p=>p.Y)),
                    1=>origin+new Point(1,0),
                    2=>origin+new Point(0,1),
                    _=>origin+new Point(-1-shape.Max(p=>p.X),0)
                };
                item["position"]=new JObject{{"x",anchor.X},{"y",anchor.Y}};
                // Parse with this coordinate model, without product geometry.
                var child=new V3IndependentReplay();
                child.Read(new JObject{{"sizeX",width},{"sizeY",height},{"holeInfos",new JArray(item)}},catalog,false);
                var hole=child.holes.Single();hole.Pending=true;hole.Finished=true;
                queue.Add(holes.Count);holes.Add(hole);
            }
            spawners.Add(new ReplaySpawner{Queue=queue.ToArray()});
        }
    }
    private bool ReleaseIndependentSpawners()
    {
        bool born=false;
        foreach(var spawner in spawners)
        {
            if(spawner.Offset>=spawner.Queue.Length)continue;
            var hole=holes[spawner.Queue[spawner.Offset]];
            bool blocked=hole.Cells.Any(cell=>!Inside(cell) || walls.Contains(cell) ||
                cats.Any(c=>c.Alive && c.Position==cell) || plugs.Any(p=>p.Alive && p.Position==cell) ||
                boxes.Any(b=>b.Position==cell) || holes.Any(h=>!h.Pending && !h.Finished && h.Cells.Contains(cell)) ||
                covers.Any(c=>!c.Pending && !c.Secret && c.Hits>0 && (c.Rope || c.Barricade) &&
                    (c.Barricade ? c.Cells.Take(c.Hits).Contains(cell) : c.Cells.Contains(cell))));
            if(blocked)continue;
            hole.Pending=false;hole.Finished=false;spawner.Offset++;born=true;
        }
        return born;
    }
}
