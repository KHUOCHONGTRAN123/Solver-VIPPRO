using Newtonsoft.Json.Linq;

internal sealed partial class V3IndependentReplay
{
    private sealed class ReplayCover {internal Point[] Cells;internal int Hits,Color;internal bool Rope,Pending,Barricade,Secret;}
    private sealed class ReplayFence {internal Point Cell;internal int Direction,Color;internal bool Blocked=true;}
    private readonly List<ReplayCover> covers=new();
    private readonly List<ReplayFence> fences=new();
    private bool Covered(Point point)=>covers.Any(c=>!c.Pending && c.Hits>0 && (c.Barricade ? c.Cells.Take(c.Hits).Contains(point) : c.Cells.Contains(point)));
    private void ReadElevatorCompanions(JObject root)
    {
        int pickaxes=cats.Concat(boxes.SelectMany(b=>b.Queue)).Count(c=>c.Pickaxe>0);
        foreach(var item in Items(root["coverBoxInfos"]))
        {
            Fields((JObject)item,"cover","positions");
            covers.Add(new ReplayCover{Cells=Items(item["positions"]).Select(Position).ToArray(),Hits=pickaxes});
        }
        foreach(var item in Items(root["ropeInfos"]))
        {
            Fields((JObject)item,"rope","positions","count","colorId");
            covers.Add(new ReplayCover{Cells=Items(item["positions"]).Select(Position).ToArray(),Hits=Math.Max(0,Number(item,"count",3)),Color=Number(item,"colorId"),Rope=true});
        }
        foreach(var item in Items(root["barricades"]))
        {
            Fields((JObject)item,"barricade","pos","direction","range");
            var origin=Position(item["pos"]);int direction=Number(item,"direction"),range=Number(item,"range");
            if(direction<0 || direction>3 || range<0 || range>width+height)throw new Exception("Independent barricade invalid geometry.");
            Point delta=new[]{new Point(0,1),new Point(1,0),new Point(0,-1),new Point(-1,0)}[direction];
            covers.Add(new ReplayCover{Barricade=true,Hits=range+1,Cells=Enumerable.Range(0,range+1).Select(i=>new Point(origin.X+delta.X*i,origin.Y+delta.Y*i)).ToArray()});
        }
        foreach(var item in Items(root["secretLockboxInfos"]))
        {
            Fields((JObject)item,"secretLockbox","position","sizeX","sizeY","colorIds","numCats");
            var origin=Position(item["position"]);int sx=Math.Max(1,Number(item,"sizeX",3)),sy=Math.Max(1,Number(item,"sizeY",3));
            var cells=new List<Point>();
            for(int x=Math.Max(0,origin.X);x<Math.Min(width,(long)origin.X+sx);x++)
                for(int y=Math.Max(0,origin.Y);y<Math.Min(height,(long)origin.Y+sy);y++)cells.Add(new Point(x,y));
            var colors=Items(item["colorIds"]).Select(t=>(int)t).ToArray();var counts=Items(item["numCats"]).Select(t=>(int)t).ToArray();
            var requirements=new Dictionary<int,int>();
            for(int i=0;i<Math.Min(colors.Length,counts.Length);i++)if(counts[i]>0)
            {requirements.TryGetValue(colors[i],out int previous);requirements[colors[i]]=checked(previous+counts[i]);}
            if(requirements.Count==0)requirements[-1]=1;
            foreach(var r in requirements)covers.Add(new ReplayCover{Secret=true,Color=r.Key,Hits=r.Value,Cells=cells.ToArray()});
        }
        foreach(var item in Items(root["fenceInfos"]))
        {
            Fields((JObject)item,"fence","positions","directions","colorId");
            var positions=Items(item["positions"]).Select(Position).ToArray();var directions=Items(item["directions"]).Select(t=>(int)t).ToArray();
            if(positions.Length!=directions.Length)throw new Exception("Independent fence invalid registrations.");
            for(int i=0;i<positions.Length;i++)fences.Add(new ReplayFence{Cell=positions[i],Direction=directions[i],Color=Number(item,"colorId")});
        }
    }
    private void FinishCompanionCat(Cat cat)
    {
        foreach(var cover in covers)
        {
            if(!cover.Pending && cover.Barricade && cover.Hits>1){cover.Hits--;if(cover.Hits==1)cover.Hits=0;}
            if(!cover.Pending && cover.Secret && cover.Color>=0 && cover.Color==cat.Color%10 && cover.Hits>0)cover.Hits--;
        }
        if(cat.Scissors>=0)foreach(var cover in covers)if(!cover.Pending && cover.Rope && cover.Color==cat.Scissors && cover.Hits>0)cover.Hits--;
        if(cat.Pickaxe>=0){var cover=covers.FirstOrDefault(c=>!c.Pending && !c.Rope && !c.Secret && !c.Barricade && c.Hits>0);if(cover!=null)cover.Hits--;}
    }
    private bool FenceBlocked(Point from,Point to)
    {
        if(Math.Abs(from.X-to.X)+Math.Abs(from.Y-to.Y)!=1)return false;
        int direction=Direction(from,to);
        return fences.Any(f=>f.Blocked && (f.Cell==from && f.Direction==direction || f.Cell==to && f.Direction==(direction+2)%4));
    }
    private void ReleaseIndependentFence(int color)
    {
        var raw=fences.Where(f=>f.Color==color).Select(f=>(f.Cell,f.Direction)).ToHashSet();
        foreach(var f in fences)if(raw.Contains((f.Cell,f.Direction)))f.Blocked=false;
    }
    private bool IndependentFenceMove(Hole hole,Point delta)
    {
        var cells=hole.Cells.ToArray();
        if(cells.Any(p=>FenceBlocked(p,p+delta)))return false;
        for(int i=0;i<cells.Length;i++)for(int j=i+1;j<cells.Length;j++)if(FenceBlocked(cells[i]+delta,cells[j]+delta))return false;
        return true;
    }
}
