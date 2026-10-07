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
            private bool TryDecisionPlan()
            {
                var initial = new StateCheckpoint(this);
                initial.Save(this);
                int moveCount = result.moves.Count;
                decisionTotalEnd = result.expanded + 6000;
                bool solved = false;
                try
                {
                    solved = SearchEventDecisions(0);
                    return solved;
                }
                finally
                {
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
                bool solved = false;
                try
                {
                    for (int alternative = 0; alternative < 4; alternative++)
                    {
                        stats = new SubproblemStats();
                        best = null;
                        bestDepth = int.MaxValue;
                        excludedEventStates = exclusions;
                        decisionPhaseStart = result.expanded;
                        decisionPhaseLimit = 512;
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
                        saved.Save(this);
                        result.moves.RemoveRange(moveCount, result.moves.Count - moveCount);
                        // Different parking arrangements before the same eat can
                        // have very different continuation costs. Exclude only
                        // the complete arrangement that was actually attempted.
                        exclusions.Add((chosen.hole, chosen.state));
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
                    var move = new EngineMove { holeId = board.holes[candidate.hole].id, start = CellAt(current.GetValue(candidate.hole)) };
                    foreach (int position in candidate.path)
                        move.path.Add(CellAt(position));
                    current = candidate.state;
                    move.eaten = Consume(candidate.hole);
                    result.moves.Add(move);
                }
            }
        }
    }
}
