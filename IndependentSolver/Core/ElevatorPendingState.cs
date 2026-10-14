namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private bool[] SavePendingEntities()
            {
                var flags=new bool[board.cats.Count+board.boxes.Count+board.plugs.Count+board.covers.Count+board.fences.Count];int at=0;
                foreach(var c in board.cats)flags[at++]=c.pendingSpawn;
                foreach(var b in board.boxes)flags[at++]=b.pendingSpawn;
                foreach(var p in board.plugs)flags[at++]=p.pendingSpawn;
                foreach(var c in board.covers)flags[at++]=c.pendingSpawn;
                foreach(var f in board.fences)flags[at++]=f.pendingSpawn;
                return flags;
            }
            private void RestorePendingEntities(bool[] flags)
            {
                int at=0;
                foreach(var c in board.cats)c.pendingSpawn=flags[at++];
                foreach(var b in board.boxes)b.pendingSpawn=flags[at++];
                foreach(var p in board.plugs)p.pendingSpawn=flags[at++];
                foreach(var c in board.covers)c.pendingSpawn=flags[at++];
                foreach(var f in board.fences)f.pendingSpawn=flags[at++];
                cachedCoverHits=null;
                // A position can be occupied by different sequentially spawned
                // cats. Rebuild the runtime cell index after checkpoint restore.
                for(int p=0;p<catAtCell.Length;p++)catAtCell[p]=-1;
                for(int c=0;c<catsAlive.Length;c++)if(catsAlive[c])catAtCell[Index(board.cats[c].position)]=c;
            }
        }
    }
}
