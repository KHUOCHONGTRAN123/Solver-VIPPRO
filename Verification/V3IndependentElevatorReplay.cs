using Newtonsoft.Json.Linq;

internal sealed partial class V3IndependentReplay
{
    private sealed class ElevatorReplay
    {
        internal Point[] Area;
        internal List<Birth> Births=new();
    }
    private sealed class Birth
    {
        internal int Kind,Index,A,B,State;
        internal Point[] Cells=Array.Empty<Point>();
    }
    private readonly List<ElevatorReplay> elevators=new();
    private readonly List<(int A,int B)> baseLinks=new();
    private int[] activeDrag;

    private void ReadElevators(JObject root,JObject catalog)
    {
        foreach(var link in Items(root["linkInfos"]))baseLinks.Add((Number(link,"idHole1"),Number(link,"idHole2")));
        foreach(var entry in Items(root["elevatorInfos"]))
        {
            Fields((JObject)entry,"elevator","position","levelInfo");
            var origin=Position(entry["position"]);
            var child=(JObject)entry["levelInfo"].DeepClone();
            // Keep unsupported child rules explicit rather than validating a
            // plan with an incomplete coordinate model.
            foreach(string field in new[]{"elevatorInfos","holeSpawners","barrieInfos","obstacleInfos","brokenObstacleInfos","colorPaths","catBoxInfos","towerInfos"})
                if(Active(child[field],field))throw new NotSupportedException("Independent elevator missing rule: "+field);
            int sx=Math.Max(1,Number(child,"sizeX",1)),sy=Math.Max(1,Number(child,"sizeY",1));
            var elevator=new ElevatorReplay{Area=Enumerable.Range(0,sx).SelectMany(x=>Enumerable.Range(0,sy).Select(y=>origin+new Point(x,y))).ToArray()};
            foreach(string field in new[]{"holeInfos","catInfos","plugs"})
                foreach(var item in Items(child[field]))
                {
                    var pos=Position(item["position"])+origin;
                    item["position"]=new JObject{{"x",pos.X},{"y",pos.Y}};
                }
            foreach(var cover in Items(child["coverBoxInfos"]))
                foreach(var p in Items(cover["positions"]))
                {p["x"]=(int)p["x"]+origin.X;p["y"]=(int)p["y"]+origin.Y;}
            child["sizeX"]=width;child["sizeY"]=height;
            var nested=new V3IndependentReplay();nested.Read(child,catalog,false);
            int hb=holes.Count,cb=cats.Count,pb=plugs.Count;
            foreach(var cat in nested.cats){cat.Pending=true;cat.Alive=false;cats.Add(cat);elevator.Births.Add(new Birth{Kind=0,Index=cb++,Cells=new[]{cat.Position}});}
            for(int i=0;i<nested.holes.Count;i++)
            {
                var hole=nested.holes[i];if(hole.Rune>=0)hole.Rune+=hb;
                hole.Pending=true;hole.Finished=true;holes.Add(hole);
                elevator.Births.Add(new Birth{Kind=1,Index=hb+i,Cells=hole.Cells.ToArray()});
            }
            foreach(var link in Items(child["linkInfos"]))elevator.Births.Add(new Birth{Kind=2,A=hb+Number(link,"idHole1"),B=hb+Number(link,"idHole2")});
            foreach(var plug in nested.plugs){plug.Pending=true;plug.Alive=false;plugs.Add(plug);elevator.Births.Add(new Birth{Kind=3,Index=pb++,Cells=new[]{plug.Position}});}
            foreach(var cover in nested.covers){cover.Pending=true;elevator.Births.Add(new Birth{Kind=4,Index=covers.Count,Cells=cover.Cells});covers.Add(cover);}
            elevators.Add(elevator);
        }
        RebuildIndependentLinks();
    }
    private void RebuildIndependentLinks()
    {
        int[] components=Enumerable.Range(0,holes.Count).ToArray();
        void Merge(int a,int b){int old=components[b],target=components[a];for(int i=0;i<components.Length;i++)if(components[i]==old)components[i]=target;}
        foreach(var link in baseLinks)Merge(link.A,link.B);
        foreach(var elevator in elevators)foreach(var birth in elevator.Births)if(birth.Kind==2 && birth.State==1)Merge(birth.A,birth.B);
        groups=components.Select(c=>Enumerable.Range(0,holes.Count).Where(i=>components[i]==c).ToArray()).ToArray();
    }
    private bool RefreshIndependentElevators(int[] dragged=null)
    {
        bool changed=false;
        bool Occupied(Point p)=>walls.Contains(p) || holes.Any(h=>!h.Finished && h.Cells.Contains(p)) || cats.Any(c=>c.Alive && c.Position==p) || plugs.Any(c=>c.Alive && c.Position==p) || barries.Any(b=>b.Blocked.Contains(p));
        bool Dragging(Point p)=>dragged!=null && dragged.Any(i=>!holes[i].Finished && holes[i].Cells.Contains(p));
        for(int e=elevators.Count-1;e>=0;e--)
        {
            var elevator=elevators[e];
            if(elevator.Area.Any(p=>Occupied(p)||Dragging(p)||Covered(p)))continue;
            foreach(var birth in elevator.Births)
            {
                if(birth.State!=0)continue;
                if(birth.Kind==2)
                {
                    if(holes[birth.A].Pending || holes[birth.B].Pending)continue;
                    birth.State=1;changed=true;continue;
                }
                if(birth.Cells.Any(p=>!Inside(p)))
                {
                    birth.State=2;
                    if(birth.Kind==0)cats[birth.Index].Pending=false;
                    if(birth.Kind==1)holes[birth.Index].Pending=false;
                    if(birth.Kind==3)plugs[birth.Index].Pending=false;
                    if(birth.Kind==4)covers[birth.Index].Pending=false;
                    changed=true;continue;
                }
                if(birth.Cells.Any(p=>Dragging(p) || birth.Kind!=4 && (Occupied(p)||Covered(p))))continue;
                birth.State=1;changed=true;
                if(birth.Kind==0){cats[birth.Index].Pending=false;cats[birth.Index].Alive=true;}
                if(birth.Kind==1){holes[birth.Index].Pending=false;holes[birth.Index].Finished=false;}
                if(birth.Kind==3){plugs[birth.Index].Pending=false;plugs[birth.Index].Alive=true;}
                if(birth.Kind==4)covers[birth.Index].Pending=false;
            }
        }
        if(changed)RebuildIndependentLinks();
        return changed;
    }
}
