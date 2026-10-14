using Newtonsoft.Json.Linq;

// Separate coordinate model. Does not use product input, masks, or replay.
// Unsupported rules must fail explicitly until their modules are implemented.
internal sealed partial class V3IndependentReplay
{
    private readonly record struct Point(int X,int Y)
    {
        public static Point operator +(Point a,Point b)=>new(a.X+b.X,a.Y+b.Y);
    }
    private sealed class Hole
    {
        internal Point Position,Expand;
        internal Point[] Shape;
        internal int[] Colors,Counts;
        internal int Layer,Axis,PlugColor,Type,Rune=-1,Lock=-1,AxisLimit;
        internal int Countdown,Bomb,BoardDrops,Ice,Hidden;
        internal Dictionary<Point,int> Gates=new();
        internal bool Socket,Picked,Finished,Curtain,Closed,Pending,Axe;
        internal bool Expandable=>Expand.X>=0 && Expand.Y>=0;
        internal int Color=>Colors[Layer];
        internal IEnumerable<Point> Local=>Expandable&&!Picked?new[]{Expand}:Shape;
        internal IEnumerable<Point> Cells=>Local.Select(p=>p+Position);
    }
    private sealed class Cat { internal Point Position;internal int Color,Ice,Key=-1,Mystery=-1,Scissors=-1,Pickaxe=-1;internal bool Alive=true,Pending; }
    private sealed class Box { internal Point Position,Mouth;internal Cat[] Queue;internal int Offset,Locked;internal bool Tower; }
    private readonly List<Hole> holes=new();
    private readonly List<Cat> cats=new(),plugs=new();
    private readonly List<Box> boxes=new();
    private sealed class Barrie { internal int Id;internal Point[] Cells,Switches;internal HashSet<Point> Blocked=new(); }
    private readonly List<Barrie> barries=new();
    private readonly HashSet<Point> walls=new();
    private readonly List<(Point Position,int Color)> colorPaths=new();
    private int[][] groups;
    private int width,height;
    private static IEnumerable<JToken> Items(JToken token)=>token is JArray array?array:Enumerable.Empty<JToken>();
    private static int Number(JToken token,string key,int fallback=0)=>(int?)token[key]??fallback;
    private static Point Position(JToken token)=>new((int)token["x"],(int)token["y"]);
    private static bool Active(JToken token,string name)
    {
        if(token==null || token.Type==JTokenType.Null)return false;
        if(token is JContainer collection)return collection.HasValues;
        if(token.Type==JTokenType.Boolean)return (bool)token;
        if(token.Type==JTokenType.Integer || token.Type==JTokenType.Float)return (double)token!=-1 && ((double)token!=0 || !new[]{"numIced","hiddenCount","movementTypeLimit","countdownMoveLimit","timeLimited"}.Contains(name));
        return true;
    }
    private static void Fields(JObject value,string context,params string[] allowed)
    {
        foreach(var property in value.Properties())
            if(!allowed.Contains(property.Name) && Active(property.Value,property.Name))
                throw new NotSupportedException("Independent replay missing rule: "+context+"."+property.Name);
    }
    internal static void Verify(string json,string catalogJson,JArray moves)
    {
        var model=new V3IndependentReplay();
        model.Read(JObject.Parse(json),JObject.Parse(catalogJson));
        model.Replay(moves);
    }
    private void Read(JObject root,JObject catalog,bool validateInitial=true)
    {
        Fields(root,"level","sizeX","sizeY","holeInfos","catInfos","catBoxInfos","towerInfos","linkInfos","plugs","colorPaths","obstacleInfos","brokenObstacleInfos","time","countdownMoveLimit","difficulty","boxCatAsCell","barrieInfos","elevatorInfos","coverBoxInfos","ropeInfos","fenceInfos","barricades","secretLockboxInfos","holeSpawners");
        width=Number(root,"sizeX");height=Number(root,"sizeY");
        if(width<=0 || height<=0)throw new Exception("Independent replay invalid board size.");
        var shapes=Items(catalog["shapes"]).ToDictionary(s=>Number(s,"holeType"),s=>Items(s["footprint"]).Select(Position).ToArray());
        foreach(var item in Items(root["holeInfos"]))
        {
            Fields((JObject)item,"hole","position","holeType","colorIds","numOfCats","movementType","movementTypeLimit","runesealedUnlockHoleIndex","lockColorId","numIced","hiddenCount","posExpand","isPlug","plugColorId","countdownMoveLimit","timeLimited","gateInfos","isOpenClose","isOpenCloseInitiallyClosed","axe");
            var h=new Hole{Position=Position(item["position"]),Shape=shapes[Number(item,"holeType")],
                Colors=Items(item["colorIds"]).Select(c=>(int)c).ToArray(),Counts=Items(item["numOfCats"]).Select(c=>(int)c).ToArray(),
                Type=Number(item,"holeType"),Rune=Number(item,"runesealedUnlockHoleIndex",-1),Lock=Number(item,"lockColorId",-1),AxisLimit=Math.Max(0,Number(item,"movementTypeLimit")),
                Ice=Math.Max(0,Number(item,"numIced")),Hidden=Math.Max(0,Number(item,"hiddenCount")),
                Axis=Number(item,"movementType"),Expand=item["posExpand"]==null || item["posExpand"].Type==JTokenType.Null?new(-1,-1):Position(item["posExpand"]),
                Socket=(bool?)item["isPlug"]??false,PlugColor=Number(item,"plugColorId",-1),
                Countdown=Math.Max(0,Number(item,"countdownMoveLimit")),Bomb=Math.Max(0,Number(item,"timeLimited")),
                Axe=(bool?)item["axe"]??false,Curtain=(bool?)item["isOpenClose"]??false,
                Closed=((bool?)item["isOpenClose"]??false) && ((bool?)item["isOpenCloseInitiallyClosed"]??true)};
            if(item["gateInfos"] is JObject gates)
                foreach(var property in gates.Properties())
                {
                    var coordinate=property.Name.Trim('(',')').Split(',');
                    if(coordinate.Length!=2)throw new Exception("Independent replay invalid gate coordinate.");
                    int bits=(int)property.Value;
                    if(bits<0 || bits>15)throw new Exception("Independent replay invalid gate direction bits.");
                    h.Gates.Add(new Point(int.Parse(coordinate[0]),int.Parse(coordinate[1])),bits);
                }
            if(h.Colors.Length==0 || h.Colors.Length!=h.Counts.Length || h.Counts.Any(c=>c<=0) || h.Axis<0 || h.Axis>2)
                throw new Exception("Independent replay invalid hole definition.");
            holes.Add(h);
        }
        if(Number(root,"countdownMoveLimit")>0 && holes.Count>0 && holes.All(h=>h.Countdown<=0))
            holes[0].Countdown=Number(root,"countdownMoveLimit");
        if(holes.Any(h=>h.Rune>=holes.Count))throw new Exception("Independent replay invalid rune reference.");
        foreach(var item in Items(root["catInfos"]))
        {
            var cat=ReadCat(item);cat.Position=Position(item["position"]);cats.Add(cat);
        }
        foreach(string field in new[]{"catBoxInfos","towerInfos"})
            foreach(var item in Items(root[field]))
            {
                bool tower=field=="towerInfos";
                Fields((JObject)item,"box","position","colorIds","direction","requiredHolesToUnlock");
                var pos=Position(item["position"]);int direction=Number(item,"direction");
                if(direction<0 || direction>3)throw new Exception("Independent replay invalid box direction.");
                var mouth=direction switch{0=>pos+new Point(0,1),1=>pos+new Point(-1,0),2=>pos+new Point(0,-1),_=>pos+new Point(1,0)};
                boxes.Add(new Box{Position=pos,Mouth=mouth,Tower=tower,Locked=Number(item,"requiredHolesToUnlock"),Queue=Items(item["colorIds"]).Select(ReadCat).ToArray()});
                if(Inside(pos))walls.Add(pos);
            }
        foreach(var item in Items(root["plugs"]))
        {
            Fields((JObject)item,"plug","position","colorId");
            plugs.Add(new Cat{Position=Position(item["position"]),Color=Number(item,"colorId")});
        }
        foreach(string field in new[]{"obstacleInfos","brokenObstacleInfos"})
            foreach(var item in Items(root[field])){var point=Position(item);if(Inside(point))walls.Add(point);}
        foreach(var item in Items(root["colorPaths"]))
        {
            Fields((JObject)item,"colorPath","position","colorId");
            colorPaths.Add((Position(item["position"]),Number(item,"colorId")));
        }
        int[] group=Enumerable.Range(0,holes.Count).ToArray();
        foreach(var item in Items(root["linkInfos"]))
        {
            Fields((JObject)item,"link","idHole1","idHole2");
            int a=Number(item,"idHole1"),b=Number(item,"idHole2");
            if(a<0 || b<0 || a>=holes.Count || b>=holes.Count)throw new Exception("Independent replay invalid link.");
            int old=group[b],replacement=group[a];
            for(int i=0;i<group.Length;i++)if(group[i]==old)group[i]=replacement;
        }
        groups=group.Select(g=>Enumerable.Range(0,holes.Count).Where(i=>group[i]==g).ToArray()).ToArray();
        var occupied=new HashSet<Point>(walls);
        foreach(var hole in holes.Where(_=>validateInitial))
            foreach(var cell in hole.Cells)
                if(!Inside(cell) || !occupied.Add(cell))throw new Exception("Independent replay initial hole collision/bounds.");
        foreach(var item in Items(root["barrieInfos"]))
        {
            Fields((JObject)item,"barrie","id","positions","switchPositions");
            int id=Number(item,"id",-1);if(id<0 || id>9)continue;
            var barrierGroup=barries.FirstOrDefault(g=>g.Id==id);
            if(barrierGroup==null){barrierGroup=new Barrie{Id=id,Cells=Array.Empty<Point>(),Switches=Array.Empty<Point>()};barries.Add(barrierGroup);}
            barrierGroup.Cells=barrierGroup.Cells.Concat(Items(item["positions"]).Where(t=>t.Type!=JTokenType.Null).Select(Position).Where(Inside)).Distinct().ToArray();
            barrierGroup.Switches=barrierGroup.Switches.Concat(Items(item["switchPositions"]).Where(t=>t.Type!=JTokenType.Null).Select(Position).Where(Inside)).Distinct().ToArray();
        }
        RefreshBarries();
        ReadElevatorCompanions(root);
        ReadIndependentSpawners(root,catalog);
        ReadElevators(root,catalog);
    }
    private void RefreshBarries()
    {
        var occupied=holes.Where(h=>!h.Finished).SelectMany(h=>h.Cells).Concat(cats.Where(c=>c.Alive).Select(c=>c.Position))
            .Concat(plugs.Where(c=>c.Alive).Select(c=>c.Position)).Concat(walls).ToHashSet();
        foreach(var group in barries)
        {
            bool pressed=group.Switches.Length>0 && group.Switches.All(p=>holes.Any(h=>!h.Finished && h.Color>=0 && h.Color%10==group.Id && h.Cells.Contains(p)));
            group.Blocked=pressed?new HashSet<Point>():group.Cells.Where(p=>!occupied.Contains(p)).ToHashSet();
        }
    }
    private bool Inside(Point point)=>point.X>=0 && point.Y>=0 && point.X<width && point.Y<height;
    private static int Direction(Point from,Point to)=>to.Y>from.Y?0:to.X>from.X?1:to.Y<from.Y?2:3;
    private static bool GateBlocks(Hole hole,Point cell,int direction)
        =>hole.Layer==0 && hole.Gates.TryGetValue(new Point(cell.X-hole.Position.X,cell.Y-hole.Position.Y),out int bits) && (bits&(1<<direction))!=0;
    private static Cat ReadCat(JToken item)
    {
        if(item.Type==JTokenType.Integer)return new Cat{Color=(int)item};
        Fields((JObject)item,"cat","position","colorId","numIced","bonusTime","timeLimited","keyColorIds","pickaxeColorId","scissorsColorId","mysteryBoxId");
        return new Cat{Color=Number(item,"colorId"),Ice=Math.Max(0,Number(item,"numIced")),Key=Number(item,"keyColorIds",-1),Mystery=Number(item,"mysteryBoxId",-1),Scissors=Number(item,"scissorsColorId",-1),Pickaxe=Number(item,"pickaxeColorId",-1)};
    }
    private bool ConsumeOne(int index)
    {
        var h=holes[index];bool changed=false;
        while(!h.Finished && !h.Pending)
        {
            if(h.Closed || h.Rune>=0 || h.Lock>=0 || h.Ice>0 || h.Hidden>0 || h.Cells.Any(Covered))break;
            if(h.Socket)
            {
                var plug=h.Cells.SelectMany(cell=>plugs.Where(p=>p.Alive && p.Position==cell && p.Color==h.PlugColor)).FirstOrDefault();
                if(plug==null)break;
                plug.Alive=false;h.Socket=false;changed=true;RefreshIndependentElevators(activeDrag);continue;
            }
            var cat=h.Cells.SelectMany(cell=>cats.Where(c=>c.Alive && c.Ice==0 && c.Mystery<0 && c.Position==cell && c.Color==h.Color)).FirstOrDefault();
            bool boardDrop=cat!=null;
            if(cat==null)
            {
                foreach(var cell in h.Cells)
                {
                    var box=boxes.FirstOrDefault(b=>b.Locked<=0 && b.Offset<b.Queue.Length && b.Queue[b.Offset].Ice==0 && b.Queue[b.Offset].Mystery<0 && b.Queue[b.Offset].Color==h.Color &&
                        (b.Tower?Math.Abs(cell.X-b.Position.X)+Math.Abs(cell.Y-b.Position.Y)==1:cell==b.Mouth) && !FenceBlocked(cell,b.Position) && !GateBlocks(h,cell,Direction(cell,b.Position)));
                    if(box==null)continue;
                    cat=box.Queue[box.Offset++];break;
                }
            }
            if(cat==null)break;
            if(boardDrop)h.BoardDrops++;
            cat.Alive=false;changed=true;h.Counts[h.Layer]--;
            FinishCompanionCat(cat);
            // Cat.OnFinishOtherCat thaws cats per cat, not per hole finish.
            foreach(var otherCat in cats.Concat(boxes.SelectMany(b=>b.Queue.Skip(b.Offset))))
                if(otherCat.Alive)
                {
                    if(otherCat.Ice>0)otherCat.Ice--;
                    if(otherCat.Mystery==cat.Color%10)otherCat.Mystery=-1;
                }
            if(cat.Key>=0 && !cats.Concat(boxes.SelectMany(b=>b.Queue.Skip(b.Offset))).Any(c=>c.Alive && c.Key==cat.Key))
                foreach(var other in holes)if(other.Lock==cat.Key)other.Lock=-1;
            if(h.Counts[h.Layer]==0)
            {
                if(h.Layer+1<h.Counts.Length)h.Layer++;
                else
                {
                    h.Finished=true;
                    if(h.Axe)ReleaseIndependentFence(h.Colors[0]);
                    foreach(var other in holes)
                        if(!other.Finished && other.Bomb>0 && --other.Bomb==0)throw new Exception("Independent replay hole bomb exploded.");
                    foreach(var other in holes)
                        if(!other.Finished && other.Curtain)other.Closed=!other.Closed;
                    foreach(var other in holes)
                        if(!other.Finished)
                        {
                            if(other.Ice>0)other.Ice--;
                            if(other.Hidden>0)other.Hidden--;
                        }
                    // Hole.HasSameRunesealSignature uses the original first
                    // color modulo ten and hole type, or object identity.
                    foreach(var other in holes.Where(o=>!o.Pending))
                    {
                        if(other.Axis>0 && other.AxisLimit>0 && --other.AxisLimit==0)other.Axis=0;
                        if(other.Rune>=0)
                        {
                            var required=holes[other.Rune];
                            if(other.Rune==index || h.Type==required.Type && h.Colors[0]%10==required.Colors[0]%10)other.Rune=-1;
                        }
                    }
                    foreach(var box in boxes)if(box.Locked>0)box.Locked--;
                }
            }
            RefreshIndependentElevators(activeDrag);
        }
        return changed;
    }
    private void Settle(IEnumerable<int> order)
    {
        bool changed;
        do
        {
            var previous=spawners.Count>0?covers.Select(c=>c.Hits).ToArray():null;
            changed=false;foreach(int index in order)changed|=ConsumeOne(index);
            if(activeDrag==null)changed|=RefreshIndependentElevators();
            if(previous!=null && covers.Select((c,i)=>i<previous.Length && c.Hits<previous[i] &&
                (c.Barricade || !c.Secret && c.Hits==0)).Any(x=>x))changed|=ReleaseIndependentSpawners();
        } while(changed);
    }
    private void Replay(JArray moves)
    {
        RefreshIndependentElevators();
        Settle(Enumerable.Range(0,holes.Count));
        RefreshBarries();
        foreach(var move in moves)
        {
            int selected=Number(move,"holeId");
            if(selected<0 || selected>=holes.Count || holes[selected].Finished || holes[selected].Pending)throw new Exception("Independent replay invalid selected hole.");
            var h=holes[selected];var path=Items(move["path"]).Select(Position).ToArray();
            var group=groups[selected];
            activeDrag=group;
            if(group.Any(i=>!holes[i].Finished && (holes[i].Rune>=0 || holes[i].Lock>=0 || holes[i].Ice>0 || holes[i].Cells.Any(Covered))))throw new Exception("Independent replay frozen/rune/key drag before unlock.");
            if(path.Length==0 || Position(move["start"])!=h.Position || path[0]!=h.Position)
                throw new Exception("Independent replay path start mismatch.");
            bool expanding=group.Any(i=>holes[i].Expandable);
            if(path.Length==1 && !expanding)throw new Exception("Independent replay empty ordinary drag.");
            var before=group.ToDictionary(i=>i,i=>(holes[i].Finished,holes[i].Layer,holes[i].Position,holes[i].BoardDrops));
            foreach(int index in new[]{selected}.Concat(group.Where(i=>i!=selected)))
                if(!holes[index].Finished && holes[index].Expandable){holes[index].Picked=true;ConsumeOne(index);}
            if(path.Length>1 && group.Any(i=>!before[i].Finished && (holes[i].Finished || holes[i].Layer!=before[i].Layer)))
                throw new Exception("Independent replay continued after pickup finish/layer transition.");
            for(int step=1;step<path.Length;step++)
            {
                var delta=new Point(path[step].X-path[step-1].X,path[step].Y-path[step-1].Y);
                if(Math.Abs(delta.X)+Math.Abs(delta.Y)!=1)throw new Exception("Independent replay nonadjacent step.");
                var other=holes.Where((_,i)=>!group.Contains(i)).Where(o=>!o.Finished).SelectMany(o=>o.Cells).ToHashSet();
                foreach(int index in group)
                {
                    var member=holes[index];if(member.Finished)continue;
                    if(!IndependentFenceMove(member,delta))throw new Exception("Independent replay fence collision.");
                    if(member.Axis==1 && delta.X!=0 || member.Axis==2 && delta.Y!=0)throw new Exception("Independent replay axis violation.");
                    foreach(var cell in member.Local.Select(p=>p+member.Position+delta))
                    {
                        if(!Inside(cell) || walls.Contains(cell) || other.Contains(cell) || Covered(cell))throw new Exception("Independent replay collision/bounds.");
                        if(barries.Any(g=>g.Blocked.Contains(cell) || g.Switches.Contains(cell) && (member.Color<0 || member.Color%10!=g.Id)))throw new Exception("Independent replay barrie/switch collision.");
                        if(colorPaths.Any(p=>p.Position==cell && (member.Color<0 || member.Color>=1000 || member.Color%10!=p.Color)))throw new Exception("Independent replay color path violation.");
                        if(cats.Any(c=>c.Alive && c.Position==cell && (member.Closed || member.Socket || member.Hidden>0 || c.Ice>0 || c.Mystery>=0 || c.Color!=member.Color)))throw new Exception("Independent replay wrong Cat entry.");
                        var destination=new Hole{Position=member.Position+delta,Layer=member.Layer,Gates=member.Gates};
                        if(cats.Any(c=>c.Alive && c.Position==cell) && GateBlocks(destination,cell,Direction(path[step-1],path[step])))throw new Exception("Independent replay Cat gate entry violation.");
                        if(plugs.Any(p=>p.Alive && p.Position==cell && (!member.Socket || member.Hidden>0 || member.Closed || p.Color!=member.PlugColor)))throw new Exception("Independent replay wrong plug entry.");
                    }
                }
                var layers=group.ToDictionary(i=>i,i=>holes[i].Layer);
                foreach(int index in group)if(!holes[index].Finished)holes[index].Position+=delta;
                Settle(group.Where(i=>i!=selected).Append(selected).Concat(Enumerable.Range(0,holes.Count).Where(i=>!group.Contains(i))));
                if(step+1<path.Length && group.Any(i=>!before[i].Finished && (holes[i].Finished || holes[i].Layer!=layers[i])))
                    throw new Exception("Independent replay continued after finish/layer transition.");
            }
            foreach(int index in group)holes[index].Picked=false;
            activeDrag=null;
            Settle(Enumerable.Range(0,holes.Count));
            foreach(int index in group)
            {
                var member=holes[index];
                if(before[index].Finished || member.Countdown<=0)continue;
                if(member.Finished || member.Position!=before[index].Position || member.BoardDrops!=before[index].BoardDrops)
                    if(--member.Countdown==0 && !member.Finished)throw new Exception("Independent replay countdown exhausted before hole finish.");
            }
            RefreshBarries();
            if(ReleaseIndependentSpawners())Settle(Enumerable.Range(0,holes.Count));
        }
        if(cats.Any(c=>c.Alive || c.Pending) || boxes.Any(b=>b.Offset<b.Queue.Length) || holes.Any(h=>(h.Pending || !h.Finished) && h.Colors[0]<1000))
            throw new Exception("Independent replay did not clear level.");
    }
}

