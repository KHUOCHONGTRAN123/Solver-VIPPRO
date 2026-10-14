using System;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private bool[][] barrieBlocked;
            private void InitializeBarries()
            {
                barrieBlocked=new bool[board.barries.Count][];
                for(int g=0;g<barrieBlocked.Length;g++)barrieBlocked[g]=new bool[board.barries[g].positions.Length];
                RefreshBarries();
            }
            private bool BarrieOccupied(Cell cell)
            {
                for(int h=0;h<capacities.Length;h++)
                    if(current.GetValue(h)>=0 && !(masks[h][current.GetValue(h)]&Bit(cell)).IsZero)return true;
                for(int c=0;c<catsAlive.Length;c++)if(catsAlive[c] && board.cats[c].position.Equals(cell))return true;
                foreach(var plug in board.plugs)if(plug.alive && plug.position.Equals(cell))return true;
                foreach(var box in board.boxes)if(!box.pendingSpawn && box.position.Equals(cell))return true;
                return board.obstacles.Contains(cell);
            }
            private void RefreshBarries()
            {
                if(barrieBlocked==null)return;
                bool changed=false;
                for(int g=0;g<board.barries.Count;g++)
                {
                    var group=board.barries[g];bool pressed=group.switchPositions.Length>0;
                    foreach(var cell in group.switchPositions)
                    {
                        bool matching=false;
                        for(int h=0;h<capacities.Length;h++)
                            if(current.GetValue(h)>=0 && Color(h)>=0 && Color(h)%10==group.id && !(masks[h][current.GetValue(h)]&Bit(cell)).IsZero){matching=true;break;}
                        if(!matching)pressed=false;
                    }
                    for(int p=0;p<group.positions.Length;p++)
                    {
                        bool blocking=!pressed && Inside(group.positions[p]) && !BarrieOccupied(group.positions[p]);
                        changed|=blocking!=barrieBlocked[g][p];barrieBlocked[g][p]=blocking;
                    }
                }
                if(changed)InvalidatePhaseKey();
            }
            private CellMask BarrieMask()
            {
                CellMask mask=CellMask.Zero;
                if(barrieBlocked!=null)
                    for(int g=0;g<board.barries.Count;g++)
                        for(int p=0;p<barrieBlocked[g].Length;p++)if(barrieBlocked[g][p])mask|=Bit(board.barries[g].positions[p]);
                return mask;
            }
            private bool BarrieSwitchAllows(int hole,int anchor)
            {
                foreach(var group in board.barries)
                    if(Color(hole)<0 || Color(hole)%10!=group.id)
                        foreach(var cell in group.switchPositions)
                            if(Inside(cell) && !(masks[hole][anchor]&Bit(cell)).IsZero)return false;
                return true;
            }
        }
    }
}
