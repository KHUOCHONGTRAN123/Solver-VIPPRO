using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CatDom.CoreSolver.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CatDom.CoreSolver
{
    public enum SolverStatus { Solved, NoNextCatReachable, InvalidInput, UnsupportedMechanics, Cancelled }
    public sealed class SolveResult
    {
        public string algorithmVersion = "2.0.0";
        public string searchAlgorithm = "baseline";
        public long baselineExpanded, improvedExpanded;
        [JsonConverter(typeof(StringEnumConverter))] public SolverStatus status;
        public string message;
        public long expanded;
        public double solveTimeMs;
        internal long abstractExpanded;
        public List<SolverMove> moves = new List<SolverMove>();
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
                var result = LegacyLevelSolver.SolveCore(input, cancellationToken, progress, baselineStateBudget);
                output.baselineExpanded = result.expanded;
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
