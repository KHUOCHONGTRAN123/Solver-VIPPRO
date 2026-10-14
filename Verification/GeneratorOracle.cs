using System.Diagnostics;
using System.Security.Cryptography;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;

// Research-only JSONL bridge. Builds the project-referenced source, never release artifacts.
internal static class GeneratorOracle
{
    internal static void Run(string root)
    {
        var shapes = File.ReadAllText(Path.Combine(root, "IndependentSolver/IO/HoleShapes.json"));
        var catalog = Reference.IO.SolverJson.ReadCatalog(shapes);
        Product.CatLevelSolver.SolveLevel(File.ReadAllText(Path.Combine(root, "Levels/Level00001.json")));
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                var request = JObject.Parse(line);
                if ((string)request["op"] == "info")
                {
                    Reply(new { version = "2.0.0", assemblyPath = typeof(Product.CatLevelSolver).Assembly.Location,
                        assemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Product.CatLevelSolver).Assembly.Location))) });
                    continue;
                }
                string json = request["level"].ToString(Formatting.None);
                if ((string)request["op"] == "simulate")
                {
                    var prefix = request["moves"].ToObject<List<Reference.SolverMove>>();
                    var states = Trace(Reference.IO.SolverJson.ReadLevel(json, catalog), prefix);
                    Reply(new { status = "PrefixValid", snapshots = states, finalState = states.Last,
                        complete = states.Last["remainingCatIds"].Count() == 0
                            && states.Last["holes"].All(h => (bool)h["finished"] || (int)h["color"] >= 1000)
                            && states.Last["boxes"].All(b => (int)b["consumed"] >= Reference.IO.SolverJson.ReadLevel(json, catalog).boxes.Single(x => x.id == (int)b["id"]).colors.Length) });
                    continue;
                }
                List<Product.SolverMove> moves;
                Product.SolveResult result = null;
                if ((string)request["op"] == "replay")
                    moves = request["moves"].ToObject<List<Product.SolverMove>>();
                else
                {
                    using var cancellation = new CancellationTokenSource();
                    long stateBudget = (long?)request["stateBudget"] ?? long.MaxValue;
                    result = Product.CatLevelSolver.SolveLevelWithProgress(json, cancellation.Token,
                        r => { if (r.expanded >= stateBudget) cancellation.Cancel(); });
                    if (result.status != Product.SolverStatus.Solved)
                    {
                        Reply(new { status = result.status.ToString(), result.message, result.algorithmVersion,
                            result.searchAlgorithm, result.expanded, result.solveTimeMs,
                            partialMoves = result.moves, finalState = result.diagnosticFinalState,
                            initialEaten = result.diagnosticInitialEaten,
                            replayed = false, elapsedMs = timer.Elapsed.TotalMilliseconds });
                        continue;
                    }
                    moves = result.moves;
                }
                var replayMoves = JsonConvert.DeserializeObject<List<Reference.SolverMove>>(JsonConvert.SerializeObject(moves));
                var replay = Reference.CatLevelSolver.VerifyPlan(Reference.IO.SolverJson.ReadLevel(json, catalog), replayMoves, 0, true);
                bool passed = replay.status == Reference.SolveStatus.Solved && replay.finalState.remainingCatIds.Count == 0
                    && replay.finalState.holes.Where(h => h.color < 1000).All(h => h.finished) && replay.finalState.boxes.All(b => b.consumed >=
                        Reference.IO.SolverJson.ReadLevel(json, catalog).boxes.Single(x => x.id == b.id).colors.Length);
                var snapshots = passed ? Trace(Reference.IO.SolverJson.ReadLevel(json, catalog), replayMoves) : new JArray();
                var replayNonEat = passed ? Enumerable.Range(0, moves.Count).Select(i =>
                    snapshots[i]["remainingCatIds"].Count() == snapshots[i+1]["remainingCatIds"].Count()
                    && snapshots[i]["boxes"].Sum(b => (int)b["consumed"]) == snapshots[i+1]["boxes"].Sum(b => (int)b["consumed"])).ToArray() : Array.Empty<bool>();
                var actualNonEat = replayNonEat.Count(x => x);
                if (passed && Enumerable.Range(0, moves.Count).Any(i => replayNonEat[i] != (moves[i].eaten.Count == 0)))
                    throw new Exception("A solver move consumption flag disagrees with independent replay.");
                Reply(new { status = passed ? "Solved" : "ReplayFailed", replayed = passed, result,
                    initialEaten = result?.diagnosticInitialEaten,
                    moves, nonEat = actualNonEat, snapshots, steps = moves.Sum(m => m.path.Count - 1),
                    elapsedMs = timer.Elapsed.TotalMilliseconds });
            }
            catch (Exception ex) { Reply(new { status = "Error", message = ex.Message, replayed = false, elapsedMs = timer.Elapsed.TotalMilliseconds }); }
        }
    }
    private static JArray Trace(Reference.BoardInput input, List<Reference.SolverMove> moves)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var type = typeof(Reference.CatLevelSolver).GetNestedType("Engine", BindingFlags.NonPublic);
        var config = new Reference.SolverConfig { maxSolveSeconds = 0 };
        var result = new Reference.SolveResult { config = config };
        var engine = Activator.CreateInstance(type, flags, null,
            new object[] { input.Copy(), config, CancellationToken.None, Stopwatch.StartNew(), result }, null);
        var replay = type.GetMethod("Replay", flags); var snapshot = type.GetMethod("Snapshot", flags);
        replay.Invoke(engine, new object[] { new List<Reference.SolverMove>(), false });
        var states = new JArray(JToken.FromObject(snapshot.Invoke(engine, null)));
        foreach (var move in moves)
        {
            replay.Invoke(engine, new object[] { new List<Reference.SolverMove> { move }, false });
            states.Add(JToken.FromObject(snapshot.Invoke(engine, null)));
        }
        return states;
    }
    private static void Reply(object value) => Console.WriteLine(JsonConvert.SerializeObject(value, Formatting.None));
}
