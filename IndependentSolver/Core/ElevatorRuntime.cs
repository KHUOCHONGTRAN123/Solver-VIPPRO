using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private List<ElevatorPlan> elevatorPlans=new List<ElevatorPlan>();
            private bool ElevatorHasPendingCandidates()
            {
                foreach(var plan in elevatorPlans)foreach(var candidate in plan.candidates)
                    if(candidate.state==0)return true;
                return false;
            }
            private bool ElevatorSkippedHole(int index)
            {
                foreach(var plan in elevatorPlans)foreach(var c in plan.candidates)
                    if(c.kind==ElevatorObjectKind.Hole && c.index==index)return c.state==2;
                return false;
            }
            private bool ElevatorPendingTargets()
            {
                foreach(var plan in elevatorPlans)foreach(var c in plan.candidates)
                    if(c.state==0 && (c.kind==ElevatorObjectKind.Cat || c.kind==ElevatorObjectKind.Box || c.kind==ElevatorObjectKind.Tower ||
                        c.kind==ElevatorObjectKind.Hole && board.holes[c.index].color<1000))return true;
                return false;
            }
            private bool ElevatorGridOccupied(Cell cell)
            {
                if(!Inside(cell))return false;
                if(!(Occupancy(current)&Bit(cell)).IsZero || board.obstacles.Contains(cell) || !(BarrieMask()&Bit(cell)).IsZero)return true;
                for(int c=0;c<catsAlive.Length;c++)if(catsAlive[c] && board.cats[c].position.Equals(cell))return true;
                foreach(var p in board.plugs)if(p.alive && p.position.Equals(cell))return true;
                foreach(var b in board.boxes)if(!b.pendingSpawn && b.position.Equals(cell))return true;
                return false;
            }
            private bool RefreshElevators(int dragged=-1)
            {
                bool changed=false;
                for(int g=elevatorPlans.Count-1;g>=0;g--)
                {
                    var plan=elevatorPlans[g];
                    bool Dragged(Cell cell)
                    {
                        if(dragged<0 || !Inside(cell))return false;
                        foreach(int h in linkedGroups[dragged])
                            if(current.GetValue(h)>=0 && !(masks[h][current.GetValue(h)]&Bit(cell)).IsZero)return true;
                        return false;
                    }
                    bool Spawn(ElevatorCandidate c)
                    {
                        switch(c.kind)
                        {
                            case ElevatorObjectKind.Cat:
                                var cat=board.cats[c.index];cat.pendingSpawn=false;catsAlive[c.index]=true;catAtCell[Index(cat.position)]=c.index;break;
                            case ElevatorObjectKind.Hole:
                                var hole=board.holes[c.index];hole.pendingSpawn=false;
                                var positions=(int[])current.values.Clone();positions[c.index]=Index(hole.position);current=new Positions(positions);break;
                            case ElevatorObjectKind.Box:case ElevatorObjectKind.Tower:board.boxes[c.index].pendingSpawn=false;break;
                            case ElevatorObjectKind.Plug:board.plugs[c.index].pendingSpawn=false;board.plugs[c.index].alive=true;break;
                            case ElevatorObjectKind.Fence:board.fences[c.index].pendingSpawn=false;board.fences[c.index].blocked=true;break;
                            case ElevatorObjectKind.Link:
                                var link=plan.world.links[c.localIndex];
                                if(board.holes[link.holeId1].pendingSpawn || board.holes[link.holeId2].pendingSpawn)return false;
                                break;
                            default:board.covers[c.index].pendingSpawn=false;cachedCoverHits=null;break;
                        }
                        InvalidatePhaseKey();return true;
                    }
                    changed|=ElevatorActivation.Refresh(plan,ElevatorGridOccupied,
                        cell=>Inside(cell) && !(CoverMask()&Bit(cell)).IsZero,Dragged,Spawn);
                }
                if(changed)RebuildElevatorLinks();
                return changed;
            }
            private void RebuildElevatorLinks()
            {
                int[] groups=new int[board.holes.Count];for(int i=0;i<groups.Length;i++)groups[i]=i;
                void Link(LinkInput link)
                {
                    int old=groups[link.holeId2],target=groups[link.holeId1];
                    for(int i=0;i<groups.Length;i++)if(groups[i]==old)groups[i]=target;
                }
                foreach(var link in board.links)Link(link);
                foreach(var plan in elevatorPlans)foreach(var c in plan.candidates)
                    if(c.kind==ElevatorObjectKind.Link && c.state==1)Link(plan.world.links[c.localIndex]);
                for(int i=0;i<groups.Length;i++)
                {
                    var members=new List<int>();for(int j=0;j<groups.Length;j++)if(groups[j]==groups[i])members.Add(j);
                    linkedGroups[i]=members.ToArray();
                }
            }
            private int[][] SaveElevatorPlans()
            {
                var state=new int[elevatorPlans.Count][];for(int i=0;i<state.Length;i++)state[i]=ElevatorActivation.Save(elevatorPlans[i]);return state;
            }
            private void RestoreElevatorPlans(int[][] state)
            {
                for(int i=0;i<state.Length;i++)ElevatorActivation.Restore(elevatorPlans[i],state[i]);
                RebuildElevatorLinks();
            }
        }
    }
}
