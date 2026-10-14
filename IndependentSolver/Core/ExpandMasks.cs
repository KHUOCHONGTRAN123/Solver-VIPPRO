using System;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private CellMask[][] expandedMasks, restingMasks;
            private bool hasExpandableHoles;
            private Cell[] Footprint(int hole) => ExpandGeometry.Footprint(board.holes[hole],board.holes[hole].expandPicked);
            private void InitializeExpandMasks()
            {
                expandedMasks=(CellMask[][])masks.Clone();
                restingMasks=(CellMask[][])masks.Clone();
                for(int i=0;i<board.holes.Count;i++)
                {
                    var hole=board.holes[i];
                    if(!ExpandGeometry.Enabled(hole))continue;
                    hasExpandableHoles=true;
                    restingMasks[i]=new CellMask[count];
                    expandedMasks[i]=new CellMask[count];
                    for(int p=0;p<count;p++)
                    {
                        var origin=CellAt(p);
                        var resting=new Cell(origin.x+hole.expandCell.x,origin.y+hole.expandCell.y);
                        if(Inside(resting) && !board.obstacles.Contains(resting))restingMasks[i][p]=Bit(resting);
                        CellMask full=CellMask.Zero;
                        foreach(var local in hole.footprint)
                        {
                            var cell=new Cell(origin.x+local.x,origin.y+local.y);
                            if(!Inside(cell))continue;
                            full|=Bit(cell);
                        }
                        expandedMasks[i][p]=full;
                    }
                }
                ApplyExpandMasks();
            }
            private bool ExpandedInside(int hole,int anchor)
            {
                var origin=CellAt(anchor);
                foreach(var local in board.holes[hole].footprint)
                    if(!Inside(new Cell(origin.x+local.x,origin.y+local.y)))return false;
                return true;
            }
            private void ApplyExpandMasks()
            {
                if(expandedMasks==null)return;
                for(int i=0;i<board.holes.Count;i++)
                    masks[i]=board.holes[i].expandPicked ? expandedMasks[i] : restingMasks[i];
            }
            private void SetExpandGroup(int selected,bool picked)
            {
                if(!hasExpandableHoles)return;
                foreach(int member in linkedGroups[selected])
                    if((!picked || current.GetValue(member)>=0) && ExpandGeometry.Enabled(board.holes[member]))
                        board.holes[member].expandPicked=picked;
                ApplyExpandMasks();
                InvalidatePhaseKey();
                // Positional occupancy caches belong to one footprint mode.
                current=new Positions((int[])current.values.Clone());
            }
            private System.Collections.Generic.List<EatEvent> BeginExpandPickup(int selected)
            {
                var eaten=new System.Collections.Generic.List<EatEvent>();
                // Hole.OnBeginDrag expands selected first, then linked partners.
                ExpandOne(selected);
                foreach(int member in linkedGroups[selected])if(member!=selected)ExpandOne(member);
                return eaten;
                void ExpandOne(int member)
                {
                    if(current.GetValue(member)<0 || !ExpandGeometry.Enabled(board.holes[member]))return;
                    board.holes[member].expandPicked=true;
                    InvalidatePhaseKey();
                    ApplyExpandMasks();
                    current=new Positions((int[])current.values.Clone());
                    eaten.AddRange(Consume(member,true));
                }
            }
            private CellMask OccupancyExcept(Positions state,int[] excluded)
            {
                CellMask occupied=CellMask.Zero;
                for(int i=0;i<capacities.Length;i++)
                    if(state.GetValue(i)>=0 && Array.IndexOf(excluded,i)<0)
                        occupied|=masks[i][state.GetValue(i)];
                return occupied;
            }
        }
    }
}



