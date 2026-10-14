using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private int[][] fenceStepPhase;
            private readonly Dictionary<string,int[][]> fenceStepPhases=new Dictionary<string,int[][]>();
            private void PrepareFenceStepCache()
            {
                fenceStepPhase=null;
                if(board.fences.Count==0 || hasExpandableHoles)return;
                var flags=new char[board.fences.Count];
                for(int i=0;i<flags.Length;i++)flags[i]=board.fences[i].blocked?'1':'0';
                string signature=new string(flags);
                if(!fenceStepPhases.TryGetValue(signature,out fenceStepPhase))
                {
                    fenceStepPhase=new int[board.holes.Count][];
                    fenceStepPhases.Add(signature,fenceStepPhase);
                }
            }
            private bool FenceStepAllows(int hole,int from,int to)
            {
                if(fenceStepPhase==null)return Fences.AllowsMove(Footprint(hole),CellAt(from),CellAt(to));
                var entries=fenceStepPhase[hole];
                if(entries==null)
                {
                    entries=new int[count];
                    for(int i=0;i<count;i++)entries[i]=-1;
                    fenceStepPhase[hole]=entries;
                }
                if(entries[from]<0)
                {
                    int allowed=0;
                    foreach(int destination in neighbors[from])
                        if(Fences.AllowsMove(board.holes[hole].footprint,CellAt(from),CellAt(destination)))
                            allowed|=1<<Direction(CellAt(from),CellAt(destination));
                    entries[from]=allowed;
                }
                return (entries[from]&(1<<Direction(CellAt(from),CellAt(to))))!=0;
            }
        }
    }
}
