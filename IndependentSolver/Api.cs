using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CatDom.CoreSolver.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace CatDom.CoreSolver
{
    public enum SolverStatus { Solved, NoNextCatReachable, InvalidInput, UnsupportedMechanics, Cancelled, CountdownExceeded }
    public sealed class SolveResult
    {
        public string algorithmVersion = "3.0.0";
        public string searchAlgorithm = "baseline";
        public long baselineExpanded, improvedExpanded;
        [JsonConverter(typeof(StringEnumConverter))] public SolverStatus status;
        public string message;
        public long expanded;
        public double solveTimeMs;
        internal long abstractExpanded;
        public List<SolverMove> moves = new List<SolverMove>();
        // Diagnostics retained for generation; does not participate in search.
        internal BoardSnapshot diagnosticFinalState;
        internal List<EatEvent> diagnosticInitialEaten = new List<EatEvent>();
    }
    public sealed class SolverMove
    {
        public int holeId;
        public Cell start;
        public List<Cell> path;
        public List<EatEvent> eaten;
    }
    public static partial class CatLevelSolver
    {
        private static readonly Lazy<ShapeCatalog> Catalog = new Lazy<ShapeCatalog>(() =>
        {
            using (var stream = typeof(CatLevelSolver).Assembly.GetManifestResourceStream("IndependentSolver.IO.HoleShapes.json"))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("Embedded shape catalog is missing.")))
                return SolverJson.ReadCatalog(reader.ReadToEnd());
        });
        /// <summary>Tries baseline for 10,000 states, then restarts with improved search. Time and expanded include both attempts.</summary>
        public static SolveResult SolveLevel(string levelJson, CancellationToken cancellationToken = default)
            => SolveLevelWithProgress(levelJson, cancellationToken, null);

        internal static SolveResult SolveLevelWithProgress(string levelJson, CancellationToken cancellationToken, Action<EngineResult> progress, long baselineStateBudget = 10000)
        {
            var clock = Stopwatch.StartNew(); var output = new SolveResult();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(levelJson)) throw new ArgumentException("Level JSON is required.");
                BoardInput input;
                try { input = SolverJson.ReadLevel(levelJson, Catalog.Value); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is NullReferenceException)
                { output.status = SolverStatus.InvalidInput; output.message = ex.Message; return output; }
                var original = input.Copy();
                if(input.holes.Exists(h=>h.countdownMoves>0))
                {
                    // Countdown is excluded from search; validate the chosen
                    // plan against original limits without another search.
                    var relaxed=JObject.Parse(levelJson);
                    foreach(var property in relaxed.Descendants().OfType<JProperty>().Where(p=>p.Name=="countdownMoveLimit").ToArray())
                        property.Value=0;
                    using(var attempt=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        var candidate=SolveLevelWithProgress(relaxed.ToString(Formatting.None),attempt.Token,progress,baselineStateBudget);
                        candidate.searchAlgorithm="countdown-relaxed+"+candidate.searchAlgorithm;
                        if(candidate.status==SolverStatus.Solved)
                        {
                            var moves=candidate.moves.Select(m=>new EngineMove{holeId=m.holeId,start=m.start,path=m.path,eaten=m.eaten}).ToList();
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var replay=VerifyPlan(original,moves);
                                if(replay.status==SolveStatus.Solved)
                                {
                                    candidate.diagnosticFinalState=replay.finalState;

                                    output=candidate;
                                    return output;
                                }
                                throw new ArgumentException("Relaxed plan failed original mechanic replay.");
                            }
                            catch(ArgumentException ex) when(ex.Message=="Replay exhausted countdown before hole finish.")
                            {
                                candidate.status=SolverStatus.CountdownExceeded;
                                candidate.message="Solution found ignoring Countdown, but exceeds the original drag limit. "+ex.Message;
                            }
                        }
                        output=candidate;
                        return output;
                    }
                }
                // New event-dependent mechanics use the improved planner directly.
                // Existing levels retain the exact 2.0 hybrid entry policy.
                bool dynamicMechanics = input.holes.Exists(h => ExpandGeometry.Enabled(h) || h.isPlug || h.bombRemaining > 0 || h.countdownMoves > 0 || h.movementTypeLimit > 0 || h.runesealedUnlockHoleIndex >= 0 || h.axe || h.isOpenClose)
                    || input.elevators.Count>0 || input.barries.Count>0 || input.spawners.Count > 0 || input.plugs.Count > 0 || input.fences.Count > 0
                    || input.covers.Exists(c => c.rope || c.finishHoleCounter || c.barricade || c.secretLockCounter)
                    || input.cats.Exists(c => c.scissorsColorId >= 0 || c.mysteryBoxId >= 0)
                    || input.boxes.Exists(b => Array.Exists(b.cats, c => c.scissorsColorId >= 0 || c.mysteryBoxId >= 0));
                var result = dynamicMechanics ? SolveCore(input, cancellationToken, progress)
                    : LegacyLevelSolver.SolveCore(input, cancellationToken, progress, baselineStateBudget);
                output.searchAlgorithm = dynamicMechanics ? "improved" : "baseline";
                output.baselineExpanded = dynamicMechanics ? 0 : result.expanded;
                output.improvedExpanded = dynamicMechanics ? result.expanded : 0;
                output.expanded = result.expanded;
                if (result.baselineBudgetExceeded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.searchAlgorithm = "baseline+improved";
                    long baselineCost = result.expanded;
                    Action<EngineResult> combinedProgress = progress == null ? null : r =>
                    {
                        long improvedCost = r.expanded;
                        try { r.expanded = baselineCost + improvedCost; progress(r); }
                        finally { r.expanded = improvedCost; }
                    };
                    result = SolveCore(original, cancellationToken, combinedProgress);
                    output.improvedExpanded = result.expanded;
                    result.expanded += baselineCost;
                }
                output.status = (SolverStatus)Enum.Parse(typeof(SolverStatus), result.status.ToString());
                output.message = result.message; output.expanded = result.expanded;
                output.abstractExpanded = result.abstractExpanded;
                output.diagnosticFinalState = result.finalState;
                output.diagnosticInitialEaten = result.initialEaten;
                foreach (var move in result.moves)
                    output.moves.Add(new SolverMove { holeId = move.holeId, start = move.start, path = move.path, eaten = move.eaten });
            }
            catch (OperationCanceledException) { output.status = SolverStatus.Cancelled; output.message = "Search cancelled."; }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is InvalidCastException || ex is OverflowException)
            { output.status = SolverStatus.InvalidInput; output.message = ex.Message; }
            finally { output.solveTimeMs = clock.Elapsed.TotalMilliseconds; }
            return output;
        }
    }
}





