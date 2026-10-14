using System;

namespace CatDom.CoreSolver
{
    // Engine supplies current grid/cover occupancy and active dragging masks.
    // This owns only candidate state, so rollback can restore it exactly.
    internal static class ElevatorActivation
    {
        internal static bool Refresh(ElevatorPlan plan,Func<Cell,bool> occupied,Func<Cell,bool> covered,
            Func<Cell,bool> dragging,Func<ElevatorCandidate,bool> spawn)
        {
            foreach(var cell in plan.area)
                if(occupied(cell) || covered(cell) || dragging(cell))return false;
            bool changed=false;
            foreach(var candidate in plan.candidates)
            {
                if(candidate.state!=0)continue;
                if(candidate.kind==ElevatorObjectKind.Link)
                {
                    if(spawn(candidate)){candidate.state=1;changed=true;}
                    continue;
                }
                bool free=true;
                bool cover=candidate.kind==ElevatorObjectKind.Cover || candidate.kind==ElevatorObjectKind.SecretLock || candidate.kind==ElevatorObjectKind.CurtainArea;
                foreach(var cell in candidate.footprint)
                    if(dragging(cell) || !cover && (occupied(cell) || covered(cell))){free=false;break;}
                if(!free)continue;
                candidate.state=spawn(candidate)?1:2;
                changed=true;
            }
            return changed;
        }
        internal static int[] Save(ElevatorPlan plan)
        {
            var state=new int[plan.candidates.Count];
            for(int i=0;i<state.Length;i++)state[i]=plan.candidates[i].state;
            return state;
        }
        internal static void Restore(ElevatorPlan plan,int[] state)
        {
            if(state.Length!=plan.candidates.Count)throw new ArgumentException("Elevator checkpoint size differs.");
            for(int i=0;i<state.Length;i++)plan.candidates[i].state=state[i];
        }
        internal static bool PendingPlayableHole(ElevatorPlan plan)
        {
            foreach(var candidate in plan.candidates)
                if(candidate.kind==ElevatorObjectKind.Hole && candidate.state==0 && plan.world.holes[candidate.localIndex].color<1000)return true;
            return false;
        }
    }
}
