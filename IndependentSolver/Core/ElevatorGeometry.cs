using System;

namespace CatDom.CoreSolver
{
    internal static class ElevatorGeometry
    {
        internal static Cell Offset(Cell cell,Cell origin)=>new Cell(cell.x+origin.x,cell.y+origin.y);
        internal static Cell[] Area(ElevatorInput input)
        {
            int width=Math.Max(1,input.child.width),height=Math.Max(1,input.child.height);
            var cells=new Cell[checked(width*height)];int at=0;
            for(int x=0;x<width;x++)for(int y=0;y<height;y++)cells[at++]=Offset(new Cell(x,y),input.position);
            return cells;
        }
        // Translate world coordinates only. Shape/gate/expand cells are local
        // to a hole and must retain their original coordinates.
        internal static BoardInput WorldChild(ElevatorInput input,int holeBase,int catBase,int boxBase,int plugBase,int coverBase)
        {
            var child=input.child.Copy();var origin=input.position;
            for(int i=0;i<child.obstacles.Count;i++)child.obstacles[i]=Offset(child.obstacles[i],origin);
            foreach(var hole in child.holes)
            {
                hole.id+=holeBase;hole.position=Offset(hole.position,origin);
                if(hole.runesealedUnlockHoleIndex>=0)hole.runesealedUnlockHoleIndex+=holeBase;
            }
            foreach(var cat in child.cats){cat.id+=catBase;cat.position=Offset(cat.position,origin);}
            foreach(var box in child.boxes){box.id+=boxBase;box.position=Offset(box.position,origin);box.mouth=Offset(box.mouth,origin);}
            foreach(var plug in child.plugs){plug.id+=plugBase;plug.position=Offset(plug.position,origin);}
            foreach(var cover in child.covers)
            {
                cover.id+=coverBase;
                for(int i=0;i<cover.cells.Length;i++)cover.cells[i]=Offset(cover.cells[i],origin);
            }
            foreach(var fence in child.fences)fence.position=Offset(fence.position,origin);
            for(int i=0;i<child.links.Count;i++)
            {
                var link=child.links[i];link.holeId1+=holeBase;link.holeId2+=holeBase;child.links[i]=link;
            }
            for(int i=0;i<child.colorPaths.Count;i++)
            {
                var path=child.colorPaths[i];path.position=Offset(path.position,origin);child.colorPaths[i]=path;
            }
            foreach(var spawner in child.spawners)
            {
                spawner.position=Offset(spawner.position,origin);
                for(int i=0;i<spawner.holeIds.Length;i++)spawner.holeIds[i]+=holeBase;
            }
            foreach(var barrie in child.barries)
            {
                for(int i=0;i<barrie.positions.Length;i++)barrie.positions[i]=Offset(barrie.positions[i],origin);
                for(int i=0;i<barrie.switchPositions.Length;i++)barrie.switchPositions[i]=Offset(barrie.switchPositions[i],origin);
            }
            foreach(var nested in child.elevators)nested.position=Offset(nested.position,origin);
            return child;
        }
    }
}
