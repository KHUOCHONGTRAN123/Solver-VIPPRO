using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    internal static class ElevatorPreparation
    {
        // Preallocate fixed entity slots before Engine allocates masks/arrays.
        // Child objects remain invisible until their candidate is activated.
        internal static List<ElevatorPlan> Prepare(BoardInput board)
        {
            var plans=new List<ElevatorPlan>();
            foreach(var elevator in board.elevators)
            {
                int hb=board.holes.Count,cb=board.cats.Count,bb=board.boxes.Count,pb=board.plugs.Count,
                    coverBase=board.covers.Count,fb=board.fences.Count;
                var plan=ElevatorCandidates.Compile(elevator,hb,cb,bb,pb,coverBase,board.width,board.height);
                foreach(var hole in plan.world.holes)hole.pendingSpawn=true;
                foreach(var cat in plan.world.cats)cat.pendingSpawn=true;
                foreach(var box in plan.world.boxes)box.pendingSpawn=true;
                foreach(var plug in plan.world.plugs){plug.pendingSpawn=true;plug.alive=false;}
                foreach(var cover in plan.world.covers)cover.pendingSpawn=true;
                foreach(var fence in plan.world.fences){fence.pendingSpawn=true;fence.blocked=false;}
                board.holes.AddRange(plan.world.holes);board.cats.AddRange(plan.world.cats);board.boxes.AddRange(plan.world.boxes);
                board.plugs.AddRange(plan.world.plugs);board.covers.AddRange(plan.world.covers);board.fences.AddRange(plan.world.fences);
                // Reference links are not active until both child holes exist.
                foreach(var candidate in plan.candidates)
                {
                    switch(candidate.kind)
                    {
                        case ElevatorObjectKind.Cat:candidate.index+=cb;break;
                        case ElevatorObjectKind.Hole:candidate.index+=hb;break;
                        case ElevatorObjectKind.Box:case ElevatorObjectKind.Tower:candidate.index+=bb;break;
                        case ElevatorObjectKind.Plug:candidate.index+=pb;break;
                        case ElevatorObjectKind.Fence:candidate.index+=fb;break;
                        case ElevatorObjectKind.Link:break; // indexes plan.world.links
                        default:candidate.index+=coverBase;break;
                    }
                }
                plans.Add(plan);
                board.ignoredMechanics.AddRange(plan.world.ignoredMechanics);
            }
            return plans;
        }
    }
}
