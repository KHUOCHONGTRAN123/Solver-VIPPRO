using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private readonly Dictionary<string,bool> dragAssignments=new Dictionary<string,bool>();
            private bool ConsumptionAssignmentPossible()
            {
                // Pending holes and changing expand footprints require a wider
                // model. Never reject them using the active-hole assignment.
                if(board.barries.Count>0 || board.spawners.Count>0 || hasExpandableHoles)return true;
                if(ElevatorHasPendingCandidates())return true;
                if(board.obstacles.Count==0 && board.colorPaths.Count==0 && board.links.Count==0 &&
                    !board.holes.Exists(h=>h.movementType>0 || h.footprint.Length>1))return true;
                string key=FullStateKey();
                if(dragAssignments.TryGetValue(key,out bool possible))return possible;
                possible=HasRelaxedCatAssignment();
                if(dragAssignments.Count>=2048)dragAssignments.Clear();
                dragAssignments[key]=possible;
                return possible;
            }
        }
    }
}
