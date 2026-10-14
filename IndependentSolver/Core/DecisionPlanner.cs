using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private int decisionPhaseLimit;
            private long decisionPhaseStart, decisionTotalEnd;
            private HashSet<(int hole, int destination)> excludedEvents;
            private HashSet<(int hole, Positions state)> excludedEventStates;
            private sealed class DecisionBudgetExceeded : Exception { }

            // Bounded event planning can reconsider a consumption instead of
            // exhausting parking arrangements after an unfortunate commitment.
            // Failed attempts retain their expanded count before normal fallback.
            private bool TryDecisionPlan(int totalBudget=0)
            {
                var initial = new StateCheckpoint(this);
                initial.Save(this);
                int moveCount = result.moves.Count;
                decisionTotalEnd = result.expanded + (totalBudget>0 ? totalBudget : board.elevators.Count>0 ? 60000 : 6000);
                bool solved = false;
                bool savedPreference=preferBombEvents;
                if(!hasExpandableHoles && HasActiveBomb())preferBombEvents=false;
                try
                {
                    solved = SearchEventDecisions(0);
                    return solved;
                }
                finally
                {
                    preferBombEvents=savedPreference;
                    decisionPhaseLimit = 0;
                    decisionTotalEnd = 0;
                    excludedEvents = null;
                    excludedEventStates = null;
                    if (!solved)
                    {
                        initial.Restore(this);
                        result.moves.RemoveRange(moveCount, result.moves.Count - moveCount);
                    }
                }
            }

            private bool SearchEventDecisions(int depth)
            {
                token.ThrowIfCancellationRequested();
                if (BombFailed()) return false;
                if (BombQueueDeadEnd()) return false;
                if (Cleared())
                    return true;
                if (depth >= 256 || result.expanded >= decisionTotalEnd)
                    return false;
                Prepare();
                if (!HasMovableTarget())
                    return false;
                var saved = new StateCheckpoint(this);
                saved.Save(this);
                int moveCount = result.moves.Count;
                var exclusions = new HashSet<(int hole, Positions state)>();
                var eventExclusions = new HashSet<(int hole, int destination)>();
                bool solved = false;
                try
                {
                    for (int alternative = 0; alternative < (board.elevators.Count>0 ? 8 : 4); alternative++)
                    {
                        stats = new SubproblemStats();
                        best = null;
                        bestDepth = int.MaxValue;
                        excludedEventStates = exclusions;
                        excludedEvents = eventExclusions;
                        decisionPhaseStart = result.expanded;
                        decisionPhaseLimit = board.elevators.Count>0 ? 4096 : 512;
                        try
                        {
                            // Cheap direct events dominate most phases. Parking
                            // search is needed only when direct consumption fails.
                            searchDepthLimit = 1;
                            visited = new Dictionary<Positions, int>();
                            Search(current, new Node());
                            if (best == null)
                            {
                                int active = 0;
                                foreach (int position in current.values)
                                    if (position >= 0) active++;
                                bool found = active <= 4
                                    ? SearchPairPatterns(128) : SearchTargetBeams();
                                if (!found && !SearchRouteBeam(true, 4, 128, cheapRank: true))
                                    SearchRouteBeam(true, 16, 128, cheapRank: true);
                            }
                        }
                        catch (DecisionBudgetExceeded)
                        {
                            best = null;
                        }
                        finally
                        {
                            decisionPhaseLimit = 0;
                        }
                        if (best == null)
                            break;
                        var chosen = best.move;
                        AppendPlan(best);
                        excludedEvents = null;
                        excludedEventStates = null;
                        if (SearchEventDecisions(depth + 1))
                        {
                            solved = true;
                            return true;
                        }
                        saved.Restore(this);
                        result.eventBacktracks++;
                        saved.Save(this);
                        result.moves.RemoveRange(moveCount, result.moves.Count - moveCount);
                        // Diversify consumption targets within this bounded
                        // accelerator. The complete fallback retains every
                        // parking arrangement if these event choices fail.
                        exclusions.Add((chosen.hole, chosen.state));
                        eventExclusions.Add((chosen.hole, chosen.destination));
                        if (result.expanded >= decisionTotalEnd)
                            break;
                    }
                    return false;
                }
                finally
                {
                    decisionPhaseLimit = 0;
                    excludedEvents = null;
                    excludedEventStates = null;
                    if (!solved)
                    {
                        saved.Restore(this);
                        result.moves.RemoveRange(moveCount, result.moves.Count - moveCount);
                    }
                }
            }

            private void AppendPlan(Node end)
            {
                var chain = new List<Candidate>();
                for (Node node = end; node.parent != null; node = node.parent)
                    chain.Add(node.move);
                chain.Reverse();
                foreach (var candidate in chain)
                {
                    var beforeBirth=board.elevators.Count>0 ? SaveElevatorPlans() : null;
                    var move = new EngineMove { holeId = board.holes[candidate.hole].id, start = CellAt(current.GetValue(candidate.hole)) };
                    foreach (int position in candidate.path)
                        move.path.Add(CellAt(position));
                    current = candidate.state;
                    move.eaten = Consume(candidate.hole);
                    bool born=false;
                    if(beforeBirth!=null)
                    {
                        if(RefreshElevators())move.eaten.AddRange(Consume());
                        for(int e=0;e<elevatorPlans.Count;e++)
                            for(int c=0;c<beforeBirth[e].Length;c++)
                                if(beforeBirth[e][c]!=elevatorPlans[e].candidates[c].state)born=true;
                        move.expected=Snapshot();
                    }
                    result.moves.Add(move);
                    // Birth changes occupancy and links. Replan immediately
                    // rather than applying an old parking suffix to new objects.
                    if(born)break;
                }
            }
        }
    }
}
