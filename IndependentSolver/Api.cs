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
        public string algorithmVersion = "1.0.0";
        [JsonConverter(typeof(StringEnumConverter))] public SolverStatus status;
        public string message;
        public long expanded;
        public double solveTimeMs;
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
        /// <summary>Solves one level with fixed v42 logic. Elapsed time includes parsing and replay, not serialization or file I/O.</summary>
        public static SolveResult SolveLevel(string levelJson, CancellationToken cancellationToken = default)
            => SolveLevelWithProgress(levelJson, cancellationToken, null);

        internal static SolveResult SolveLevelWithProgress(string levelJson, CancellationToken cancellationToken, Action<EngineResult> progress)
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
                var result = SolveCore(input, cancellationToken, progress);
                output.status = (SolverStatus)Enum.Parse(typeof(SolverStatus), result.status.ToString());
                output.message = result.message; output.expanded = result.expanded;
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
