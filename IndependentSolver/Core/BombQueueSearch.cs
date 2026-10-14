using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private readonly Dictionary<string,bool> bombQueuePhases=new Dictionary<string,bool>();
            private bool BombQueueDeadEnd()
            {
                // Future spawns and future layers supply additional capacity.
                // This first implementation leaves those inputs unpruned.
                if(board.spawners.Count>0 || board.elevators.Count>0 || !board.holes.Exists(h=>h.bombRemaining>0))return false;
                for(int i=0;i<capacities.Length;i++)
                    if(current.GetValue(i)>=0 && layers[i]+1<board.holes[i].layerColors.Length)return false;
                string key=FullStateKey(false);
                if(bombQueuePhases.TryGetValue(key,out bool cached))return cached;
                CheckBudget();
                bool impossible=false;
                for(int bomb=0;bomb<capacities.Length;bomb++)
                {
                    if(current.GetValue(bomb)<0 || board.holes[bomb].bombRemaining<=0)continue;
                    int color=Color(bomb),required=capacities[bomb];
                    // Every matching board cat is optimistically available,
                    // including locked/iced/hidden cats and unreachable cats.
                    for(int c=0;c<catsAlive.Length;c++)
                        if(catsAlive[c] && board.cats[c].color==color)required--;
                    if(required<=0)continue;
                    var colors=new List<int>();var counts=new List<int>();
                    for(int h=0;h<capacities.Length;h++)
                        if(h!=bomb && current.GetValue(h)>=0)
                        {colors.Add(Color(h));counts.Add(capacities[h]);}
                    var queues=new int[board.boxes.Count][];
                    for(int q=0;q<queues.Length;q++)
                    {
                        var source=board.boxes[q].colors;
                        queues[q]=new int[source.Length-boxOffsets[q]];
                        Array.Copy(source,boxOffsets[q],queues[q],0,queues[q].Length);
                    }
                    if(!BombQueueRelaxation.Feasible(color,required,board.holes[bomb].bombRemaining-1,colors.ToArray(),counts.ToArray(),queues))
                    {impossible=true;break;}
                }
                CheckBudget();
                if(bombQueuePhases.Count>=256)bombQueuePhases.Clear();
                bombQueuePhases[key]=impossible;
                return impossible;
            }
        }
    }
}
