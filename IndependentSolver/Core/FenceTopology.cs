using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    // Keep directional registrations distinct: runtime unregisters the raw
    // (cell,direction), whereas traversal checks both ends of an edge.
    internal sealed class FenceTopology
    {
        private readonly List<FenceEdgeInput> edges;
        private readonly Dictionary<(Cell, int), List<FenceEdgeInput>> registrations
            = new Dictionary<(Cell, int), List<FenceEdgeInput>>();
        internal FenceTopology(List<FenceEdgeInput> edges)
        {
            this.edges = edges;
            foreach (var edge in edges)
            {
                var key = (edge.position, edge.direction);
                if (!registrations.TryGetValue(key, out var list))
                    registrations.Add(key, list = new List<FenceEdgeInput>());
                list.Add(edge);
            }
        }
        private bool Registered(Cell cell, int direction)
        {
            if (registrations.TryGetValue((cell, direction), out var list))
                foreach (var edge in list)
                    if (edge.blocked) return true;
            return false;
        }
        internal bool Blocks(Cell from, Cell to)
        {
            if (Math.Abs(from.x-to.x) + Math.Abs(from.y-to.y) != 1) return false;
            int direction = to.y>from.y ? 0 : to.x>from.x ? 1 : to.y<from.y ? 2 : 3;
            return Registered(from,direction) || Registered(to,(direction+2)%4);
        }
        internal bool AllowsMove(Cell[] footprint, Cell from, Cell to)
        {
            foreach (var offset in footprint)
                if (Blocks(new Cell(from.x+offset.x,from.y+offset.y),new Cell(to.x+offset.x,to.y+offset.y)))
                    return false;
            // An edge inside the destination footprint is also illegal, even
            // when the translation itself runs parallel to that edge.
            for (int i=0;i<footprint.Length;i++)
                for (int j=i+1;j<footprint.Length;j++)
                    if (Blocks(new Cell(to.x+footprint[i].x,to.y+footprint[i].y),
                               new Cell(to.x+footprint[j].x,to.y+footprint[j].y))) return false;
            return true;
        }
        internal void ReleaseColor(int color)
        {
            // HashSet lookup in Unity loses a raw registration when any fence
            // of the released color unregisters it, including shared entries.
            foreach (var edge in edges)
                if (!edge.pendingSpawn && edge.color==color && registrations.TryGetValue((edge.position,edge.direction),out var list))
                    foreach (var duplicate in list)if(!duplicate.pendingSpawn)duplicate.blocked=false;
        }
    }
}
