using System;

namespace CatDom.CoreSolver
{
    internal static class SpawnerGeometry
    {
        // HoleSpawner.GetSpawnHolePos: direction 0 down, 1 right,
        // 2 up, 3 left; anchor uses maximum local coordinate, not width.
        internal static Cell Anchor(Cell position, int direction, Cell[] footprint)
        {
            if (footprint == null || footprint.Length == 0 || direction < 0 || direction > 3)
                throw new ArgumentException("Invalid spawner shape/direction.");
            int maxX=0,maxY=0;
            foreach(var cell in footprint)
            {
                maxX=Math.Max(maxX,cell.x); maxY=Math.Max(maxY,cell.y);
            }
            switch(direction)
            {
                case 0: return new Cell(position.x,position.y-1-maxY);
                case 1: return new Cell(position.x+1,position.y);
                case 2: return new Cell(position.x,position.y+1);
                default: return new Cell(position.x-1-maxX,position.y);
            }
        }
    }
}
