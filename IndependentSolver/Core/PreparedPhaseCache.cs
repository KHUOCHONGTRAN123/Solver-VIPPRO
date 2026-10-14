using System.Collections.Generic;
using System.Text;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private sealed class PreparedPhase
            {
                internal bool[][] valid,goals;
                internal int[][] distances,anchors;
            }
            private readonly Dictionary<string,PreparedPhase> preparedPhases=new Dictionary<string,PreparedPhase>();
            private readonly Queue<string> spawnerPreparedOrder=new Queue<string>();
            private bool preparedCacheEnabled;
            private void InitializePreparedCache()
            {
                preparedCacheEnabled=hasExpandableHoles ||
                    (board.elevators.Count>0 || board.spawners.Count>0 || board.plugs.Count>0 || board.fences.Count>0 || board.holes.Exists(h=>h.isPlug || h.isOpenClose || h.countdownMoves>0 || h.bombRemaining>0 || h.axe || h.movementTypeLimit>0 || h.runesealedUnlockHoleIndex>=0) ||
                     board.covers.Exists(c=>c.rope || c.barricade || c.secretLockCounter || c.finishHoleCounter) ||
                     board.cats.Exists(c=>c.scissorsColorId>=0 || c.mysteryBoxId>=0));
            }
            private string PreparedGeometryKey()
            {
                string state=FullStateKey(false);
                if(board.spawners.Count==0 && !board.holes.Exists(h=>h.isPlug))return state;
                var key=new StringBuilder(state);
                // Plug goals depend on CanDrop at the current anchor, including
                // whether that footprint is inside a live cover. Preserve this
                // boolean rather than disabling sharing across all positions.
                for(int i=0;i<capacities.Length;i++)
                    if(board.holes[i].isPlug)key.Append(CanDrop(i,current.GetValue(i))?'1':'0');
                key.Append('|');
                // Prepare blocks a spawner's own cell only while no active hole
                // occupies it. Other occupancy remains a search-state concern.
                var occupied=board.spawners.Count==0 ? CellMask.Zero : Occupancy(current);
                foreach(var machine in board.spawners)
                    key.Append(Inside(machine.position) && !(occupied&Bit(machine.position)).IsZero?'1':'0');
                return key.ToString();
            }
            private bool RestorePreparedPhase(string key)
            {
                if(!preparedPhases.TryGetValue(key,out var phase))return false;
                valid=phase.valid;goals=phase.goals;distances=phase.distances;goalAnchors=phase.anchors;
                targetDistances=new Dictionary<long,int[]>();
                catEntryBlocks=new int[capacities.Length][];
                routeFixedMask=PreparedFixedMask();
                return true;
            }
            private void StorePreparedPhase(string key)
            {
                if(board.spawners.Count>0)
                {
                    // Avoid losing every reusable geometry phase at once on
                    // queues with many consumption/parking arrangements.
                    if(!preparedPhases.ContainsKey(key))
                    {
                        if(preparedPhases.Count>=1024)
                            preparedPhases.Remove(spawnerPreparedOrder.Dequeue());
                        spawnerPreparedOrder.Enqueue(key);
                    }
                }
                else if(preparedPhases.Count>=256)preparedPhases.Clear();
                preparedPhases[key]=new PreparedPhase{valid=valid,goals=goals,distances=distances,anchors=goalAnchors};
            }
        }
    }
}
