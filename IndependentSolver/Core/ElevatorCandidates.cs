using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    internal enum ElevatorObjectKind { Cat,Box,Fence,Tower,Hole,Link,Rope,Barricade,Plug,Cover,SecretLock, CurtainArea }
    internal sealed class ElevatorCandidate
    {
        internal ElevatorObjectKind kind;
        internal int index;
        internal int localIndex;
        internal Cell[] footprint;
        // 0 pending, 1 spawned, 2 skipped outside runtime board.
        internal int state;
    }
    internal sealed class ElevatorPlan
    {
        internal Cell[] area;
        internal BoardInput world;
        internal readonly List<ElevatorCandidate> candidates=new List<ElevatorCandidate>();
    }
    internal static class ElevatorCandidates
    {
        internal static ElevatorPlan Compile(ElevatorInput input,int holeBase,int catBase,int boxBase,int plugBase,int coverBase,int width,int height)
        {
            var plan=new ElevatorPlan{area=ElevatorGeometry.Area(input),world=ElevatorGeometry.WorldChild(input,holeBase,catBase,boxBase,plugBase,coverBase)};
            var board=plan.world;
            void Add(ElevatorObjectKind kind,int index,Cell[] cells)
            {
                bool inside=cells.Length>0;
                foreach(var cell in cells)if(cell.x<0 || cell.y<0 || cell.x>=width || cell.y>=height)inside=false;
                plan.candidates.Add(new ElevatorCandidate{kind=kind,index=index,localIndex=index,footprint=cells,state=inside || kind==ElevatorObjectKind.Link?0:2});
            }
            for(int i=0;i<board.cats.Count;i++)Add(ElevatorObjectKind.Cat,i,new[]{board.cats[i].position});
            for(int i=0;i<board.boxes.Count;i++)if(!board.boxes[i].tower)Add(ElevatorObjectKind.Box,i,new[]{board.boxes[i].position});
            for(int i=0;i<board.fences.Count;i++)Add(ElevatorObjectKind.Fence,i,new[]{board.fences[i].position});
            for(int i=0;i<board.boxes.Count;i++)if(board.boxes[i].tower)Add(ElevatorObjectKind.Tower,i,new[]{board.boxes[i].position});
            for(int i=0;i<board.holes.Count;i++)
            {
                var h=board.holes[i];var local=ExpandGeometry.Enabled(h)?new[]{h.expandCell}:h.footprint;
                var cells=new Cell[local.Length];for(int p=0;p<cells.Length;p++)cells[p]=ElevatorGeometry.Offset(local[p],h.position);
                Add(ElevatorObjectKind.Hole,i,cells);
            }
            for(int i=0;i<board.links.Count;i++)Add(ElevatorObjectKind.Link,i,Array.Empty<Cell>());
            foreach(var kind in new[]{ElevatorObjectKind.Rope,ElevatorObjectKind.Barricade,ElevatorObjectKind.Plug,ElevatorObjectKind.Cover,ElevatorObjectKind.SecretLock,ElevatorObjectKind.CurtainArea})
            {
                if(kind==ElevatorObjectKind.Plug)
                {for(int i=0;i<board.plugs.Count;i++)Add(kind,i,new[]{board.plugs[i].position});continue;}
                for(int i=0;i<board.covers.Count;i++)
                {
                    var c=board.covers[i];var type=c.rope?ElevatorObjectKind.Rope:c.barricade?ElevatorObjectKind.Barricade:
                        c.secretLockCounter?ElevatorObjectKind.SecretLock:c.finishHoleCounter?ElevatorObjectKind.CurtainArea:ElevatorObjectKind.Cover;
                    if(type==kind)Add(kind,i,(Cell[])c.cells.Clone());
                }
            }
            return plan;
        }
    }
}
