using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    // One remaining layer per hole. Ignores positions, locks and board cats:
    // these inputs must be relaxed by the caller, never made more restrictive.
    internal static class BombQueueRelaxation
    {
        internal static bool Feasible(int color,int required,int allowed,int[] otherColors,int[] capacities,int[][] queues,int workLimit=4096)
        {
            if(required<=0)return true;
            var slack=new Dictionary<int,int>();var finishable=new Dictionary<int,int>();
            for(int i=0;i<capacities.Length;i++)
            {
                if(capacities[i]<=0 || otherColors[i]>=1000)continue;
                int c=otherColors[i];slack.TryGetValue(c,out int s);finishable.TryGetValue(c,out int f);
                slack[c]=s+capacities[i]-1;finishable[c]=f+1;
            }
            int work=0;bool exhausted=false;
            bool Fits(Dictionary<int,int> spent)
            {
                int finishes=0;
                foreach(var pair in spent)
                {
                    slack.TryGetValue(pair.Key,out int s);finishable.TryGetValue(pair.Key,out int f);
                    int forced=Math.Max(0,pair.Value-s);
                    if(forced>f)return false;
                    finishes+=forced;
                }
                return finishes<=allowed;
            }
            bool Visit(int box,int need,Dictionary<int,int> spent)
            {
                if(++work>workLimit){exhausted=true;return true;}
                if(need<=0)return Fits(spent);
                if(box==queues.Length)return false;
                if(Visit(box+1,need,spent))return true;
                var local=new Dictionary<int,int>(spent);int gained=0;
                foreach(int token in queues[box])
                {
                    if(++work>workLimit){exhausted=true;return true;}
                    if(token==color)
                    {
                        gained++;
                        if(Visit(box+1,need-gained,local))return true;
                    }
                    else
                    {
                        local.TryGetValue(token,out int used);local[token]=used+1;
                        if(!Fits(local))break;
                    }
                }
                return false;
            }
            bool feasible=Visit(0,required,new Dictionary<int,int>());
            return feasible || exhausted; // Work bound is unknown, never a dead end.
        }
    }
}
