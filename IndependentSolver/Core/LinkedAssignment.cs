using System;
using System.Collections.Generic;
namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            // A necessary assignment test, not a movement generator. Removable
            // fences/covers and future axis releases stay relaxed here.
            private bool AssignmentPartnersFollow(int hole, int origin, int from, int to, bool allPartners)
            {
                var group = linkedGroups[hole];
                if (group.Length == 1)
                    return true;
                int dx = CellAt(to).x - CellAt(origin).x, dy = CellAt(to).y - CellAt(origin).y;
                int stepDx = CellAt(to).x - CellAt(from).x, stepDy = CellAt(to).y - CellAt(from).y;
                foreach (int member in group)
                {
                    int start = current.GetValue(member);
                    if (member == hole || start < 0 || !allPartners && Color(member) < 1000)
                        continue;
                    var cell = new Cell(CellAt(start).x + dx, CellAt(start).y + dy);
                    if (!Inside(cell))
                        return false;
                    var previous = new Cell(cell.x - stepDx, cell.y - stepDy);
                    int destination = Index(cell);
                    if (masks[member][destination].IsZero || !Inside(previous) || !CanStepForAssignment(member, Index(previous), destination))
                        return false;
                }

                return true;
            }

            // A link breaks only when a member finishes. If no member can collect its
            // whole remaining capacity while the group moves as one body, the link is
            // permanent and every member is limited to the group's reachable anchors.
            private void FreezeAssignmentLinks(List<(int color, CellMask target)> pending, bool[][] reachable, bool[][] linkedReach)
            {
                var done = new bool[capacities.Length];
                for (int root = 0; root < capacities.Length; root++)
                {
                    if (done[root] || current.GetValue(root) < 0 || linkedReach[root] == null)
                        continue;
                    bool canRelease = false;
                    foreach (int member in linkedGroups[root])
                    {
                        done[member] = true;
                        if (current.GetValue(member) < 0 || linkedReach[member] == null || canRelease || Color(member) >= 1000)
                            continue;
                        var hole = board.holes[member];
                        bool finishes = true;
                        int last = hole.layerColors.Length == 0 ? layers[member] + 1 : hole.layerColors.Length;
                        for (int layer = layers[member]; layer < last && finishes; layer++)
                        {
                            int color = hole.layerColors.Length == 0 ? hole.color : hole.layerColors[layer];
                            int need = layer == layers[member] ? capacities[member] : hole.layerCounts[layer];
                            int available = 0;
                            foreach (var item in pending)
                            {
                                if (item.color != color)
                                    continue;
                                for (int p = 0; p < count; p++)
                                    if (linkedReach[member][p] && !(masks[member][p] & item.target).IsZero)
                                    {
                                        available++;
                                        break;
                                    }
                            }

                            finishes = available >= need;
                        }

                        canRelease = finishes;
                    }

                    if (canRelease)
                        continue;
                    foreach (int member in linkedGroups[root])
                        if (current.GetValue(member) >= 0 && linkedReach[member] != null)
                            reachable[member] = linkedReach[member];
                }
            }

        }
    }
}
