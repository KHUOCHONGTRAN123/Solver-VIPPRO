using System;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private long SpawnerPortfolioRank(int depth)
            {
                int cats=0,route=999;
                foreach(bool alive in catsAlive)if(alive)cats++;
                for(int b=0;b<boxOffsets.Length;b++)cats+=board.boxes[b].colors.Length-boxOffsets[b];
                for(int i=0;i<capacities.Length;i++)
                {
                    int anchor=current.GetValue(i);
                    if(anchor>=0 && CanDrag(i,anchor))route=Math.Min(route,distances[i][anchor]);
                }
                return SpawnerDragRank(cats,depth,route);
            }
            // Ordering only: preserve every legal release, including early births.
            // Consumption dominates parking; delaying birth must not reward loops.
            private long SpawnerDragRank(int cats,int depth,int route)
            {
                int active=0,released=0,unblocked=0;
                bool canProgress=false;
                var occupied=Occupancy(current);
                for(int i=0;i<capacities.Length;i++)
                {
                    int anchor=current.GetValue(i);
                    if(anchor<0)continue;
                    active++;
                    if(CanDrag(i,anchor) && distances[i][anchor]!=int.MaxValue)
                        canProgress=true;
                }
                foreach(var machine in board.spawners)
                {
                    released+=machine.consumed;
                    if(machine.consumed>=machine.holeIds.Length)continue;
                    var next=board.holes.Find(h=>h.id==machine.holeIds[machine.consumed]);
                    bool blocked=false;
                    foreach(var local in next.footprint)
                    {
                        var cell=new Cell(next.position.x+local.x,next.position.y+local.y);
                        if(Inside(cell) && !(occupied&Bit(cell)).IsZero){blocked=true;break;}
                    }
                    if(!blocked)unblocked++;
                }
                // Prefer small active boards and retain the next birth blocker
                // while an active hole has a relaxed route to consumption.
                // If none has a route, prefer queue advancement instead.
                long queuePreference=-released*1000L + (canProgress ? unblocked*40L : 0);
                return cats*10000L + AlivePlugCount()*1000L + active*40L +
                    queuePreference + Math.Min(depth,9999)*10L + Math.Min(route,999);
            }

            // HoleSpawner.OnHoleEndDrag checks one queued item per spawner.
            private bool ResolveSpawnerRelease()
            {
                bool spawned=false;
                foreach(var spawner in board.spawners)
                {
                    if(spawner.consumed>=spawner.holeIds.Length)continue;
                    int hole=board.holes.FindIndex(h=>h.id==spawner.holeIds[spawner.consumed]);
                    if(hole<0 || !board.holes[hole].pendingSpawn)
                        throw new ArgumentException("Invalid pending spawner queue.");
                    var input=board.holes[hole];
                    bool free=true;
                    foreach(var local in input.footprint)
                    {
                        var cell=new Cell(input.position.x+local.x,input.position.y+local.y);
                        if(!Inside(cell) || board.obstacles.Contains(cell)){free=false;break;}
                        foreach(var area in board.covers)
                            if(!area.pendingSpawn && area.remainingHits>0 && (area.finishHoleCounter || area.rope || area.barricade))
                            {
                                int index=Array.IndexOf(area.cells,cell);
                                if(index>=0 && (!area.barricade || index<area.remainingHits))free=false;
                            }
                        for(int c=0;c<catsAlive.Length;c++)
                            if(catsAlive[c] && board.cats[c].position.Equals(cell))free=false;
                        foreach(var plug in board.plugs)if(plug.alive && plug.position.Equals(cell))free=false;
                        foreach(var box in board.boxes)if(!box.pendingSpawn && box.position.Equals(cell))free=false;
                        for(int i=0;i<capacities.Length;i++)
                            if(current.GetValue(i)>=0 && !(masks[i][current.GetValue(i)] & Bit(cell)).IsZero)free=false;
                        if(!free)break;
                    }
                    if(!free)continue;
                    var next=(int[])current.values.Clone();
                    next[hole]=Index(input.position);
                    current=new Positions(next);
                    InvalidatePhaseKey();
                    input.pendingSpawn=false;
                    spawner.consumed++;
                    spawned=true;
                }
                return spawned;
            }
        }
    }
}


