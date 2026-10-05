using CatDom.CoreSolver;
using CatDom.CoreSolver.IO;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Reflection;

if (args.Length > 0 && args[0] == "route-inspect")
{
    var input = JsonConvert.DeserializeObject<BoardInput>(File.ReadAllText(args[1]));
    var config = SolverConfig.CreateRouteClearingDefault();
    var result = new SolveResult { config = config };
    var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    var type = typeof(CatLevelSolver).GetNestedType("Engine", BindingFlags.NonPublic);
    var engine = Activator.CreateInstance(type, flags, null, new object[] { input.Copy(), config, CancellationToken.None, Stopwatch.StartNew(), result }, null);
    object Field(string name) => type.GetField(name, flags).GetValue(engine);
    object Call(string name, params object[] values) => type.GetMethod(name, flags).Invoke(engine, values);
    type.GetField("stats", flags).SetValue(engine, new SubproblemStats());
    Call("Prepare");
    var state = Field("current");
    var masks = (System.Numerics.BigInteger[][])Field("masks");
    var anchors = (int[][])Field("goalAnchors");
    var groups = (int[][])Field("linkedGroups");
    var occupied = (System.Numerics.BigInteger)Call("Occupancy", state);
    var generated = (System.Collections.IEnumerable)Call("Generate", state);
    foreach (var move in generated)
    {
        var candidateType = move.GetType();
        if (!(bool)candidateType.GetField("terminal", flags).GetValue(move)) continue;
        Console.WriteLine(JsonConvert.SerializeObject(new { terminalHole = candidateType.GetField("hole", flags).GetValue(move),
            destination = candidateType.GetField("destination", flags).GetValue(move), accepted = Call("AcceptTerminal", move) }));
    }
    for (int hole = 0; hole < anchors.Length; hole++)
    {
        int position = (int)state.GetType().GetMethod("GetValue", flags).Invoke(state, new object[] { hole });
        if (position < 0) continue;
        var blockers = occupied;
        foreach (int member in groups[hole])
        {
            int p = (int)state.GetType().GetMethod("GetValue", flags).Invoke(state, new object[] { member });
            if (p >= 0) blockers ^= masks[member][p];
        }
        var scores = anchors[hole].Select(destination => new { destination,
            distance = (long)Call("OccupancyPathDistance", hole, position, blockers, destination),
            clearance = (long)Call("RouteClearancePenalty", hole, state, blockers, destination) }).ToArray();
        Console.WriteLine(JsonConvert.SerializeObject(new { id = input.holes[hole].id, linked = groups[hole].Length, scores }));
    }
    return;
}

if (args.Length > 0 && args[0] == "route-guard-audit")
{
    var auditRoot = Path.GetFullPath(args[1]);
    var folder = Path.Combine(auditRoot, "Benchmark/results", args[2]);
    var auditCatalog = SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(auditRoot, "IndependentSolver/IO/HoleShapes.json")));
    int checkedPlans = 0;
    foreach (var path in Directory.GetFiles(folder, "Level*.json").Where(p => !p.Contains(".progress.") && !p.Contains(".memory.")))
    {
        var result = JsonConvert.DeserializeObject<SolveResult>(File.ReadAllText(path));
        if (result.status != SolveStatus.Solved) continue;
        var input = SolverJson.ReadLevel(File.ReadAllText(Path.Combine(auditRoot, "Levels", Path.GetFileName(path))), auditCatalog);
        var replay = CatLevelSolver.VerifyPlan(input, result.moves, 0, true);
        if (replay.status != SolveStatus.Solved) throw new Exception($"Guard audit {Path.GetFileName(path)}: {replay.message}");
        checkedPlans++;
        if (checkedPlans % 25 == 0) Console.WriteLine($"Guards PASS {checkedPlans} plans");
    }
    File.WriteAllText(Path.Combine(folder, "guard-audit.json"), JsonConvert.SerializeObject(new { checkedPlans, status = "Passed" }));
    Console.WriteLine($"PASS guards and replay: {checkedPlans} plans");
    return;
}

if (args.Length > 0 && args[0] == "route-probe")
{
    var input = JsonConvert.DeserializeObject<BoardInput>(File.ReadAllText(args[1]));
    var config = SolverConfig.CreateRouteClearingDefault();
    using var cancel = new CancellationTokenSource();
    long maximum = args.Length > 3 ? long.Parse(args[3]) : 100000;
    config.progress = r => { if (r.expanded >= maximum) cancel.Cancel(); };
    var result = CatLevelSolver.FindNextCat(input, config, cancel.Token);
    File.WriteAllText(args[2], SolverJson.WriteResult(result));
    Console.WriteLine($"Probe {result.status} expanded={result.expanded} moves={result.moves.Count}");
    return;
}

if (args.Length > 0 && args[0] == "route-verify-completed")
{
    RouteVerification.RunCompleted(args[1], args.Skip(2).ToArray());
    return;
}

if (args.Length > 0 && args[0] == "route-verify")
{
    RouteVerification.Run(args[1], args[2]);
    return;
}

if (args.Length > 0 && args[0] == "route-tests")
{
    RouteTests.Run();
    return;
}

if (args.Length > 0 && (args[0] == "route" || args[0] == "route-worker"))
{
    RouteBenchmark.Run(args);
    return;
}

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "Benchmark", "results", "baseline-default");
Directory.CreateDirectory(output);
var catalog = SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root, "IndependentSolver", "IO", "HoleShapes.json")));
var files = Directory.GetFiles(Path.Combine(root, "Levels"), "*.json").OrderBy(x => x).ToArray();
var rows = new List<object>();
var counts = new Dictionary<string, int>();
var durations = new List<double>();
var solvedDurations = new List<double>();
var timer = Stopwatch.StartNew();
// Warm up JIT on a sample; measured runs still start from fresh boards.
CatLevelSolver.Solve(SolverJson.ReadLevel(File.ReadAllText(files[0]), catalog), new SolverConfig());
using var csv = new StreamWriter(Path.Combine(output, "levels.csv"));
csv.WriteLine("level,status,seconds,moves,steps,expanded,backtracks,remainingCats,unfinishedHoles,message");
string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
foreach (var file in files)
{
    SolveResult result;
    try { result = CatLevelSolver.Solve(SolverJson.ReadLevel(File.ReadAllText(file), catalog), new SolverConfig()); }
    catch (Exception ex) { result = new SolveResult { status = SolveStatus.InvalidInput, message = ex.ToString() }; }
    var name = Path.GetFileNameWithoutExtension(file);
    var status = result.status.ToString();
    counts[status] = counts.GetValueOrDefault(status) + 1;
    durations.Add(result.seconds);
    if (result.status == SolveStatus.Solved) solvedDurations.Add(result.seconds);
    var row = new { level = name, status, result.seconds, moves = result.moves.Count,
        steps = result.moves.Sum(m => m.path.Count), result.expanded, result.backtracks,
        remainingCats = result.finalState?.remainingCatIds.Count,
        unfinishedHoles = result.finalState?.holes.Count(h => !h.finished), result.message };
    rows.Add(row);
    csv.WriteLine(string.Join(",", Quote(name), status, result.seconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture), row.moves, row.steps, row.expanded, row.backtracks, row.remainingCats, row.unfinishedHoles, Quote(row.message)));
    csv.Flush();
    File.WriteAllText(Path.Combine(output, name + ".json"), SolverJson.WriteResult(result));
    Console.WriteLine($"{rows.Count}/{files.Length} {name}: {status} {result.seconds:F3}s moves={row.moves} expanded={row.expanded}");
}
double Percentile(List<double> data, double p) => data.Count == 0 ? 0 : data.OrderBy(x => x).ElementAt((int)Math.Ceiling(p * data.Count) - 1);
var summary = new { dateUtc = DateTime.UtcNow, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    processorCount = Environment.ProcessorCount, config = new SolverConfig(), total = files.Length, counts,
    wallSeconds = timer.Elapsed.TotalSeconds, solveSeconds = durations.Sum(),
    solvedMeanSeconds = solvedDurations.Count == 0 ? 0 : solvedDurations.Average(),
    solvedMedianSeconds = Percentile(solvedDurations, .5), solvedP95Seconds = Percentile(solvedDurations, .95),
    solvedMaxSeconds = solvedDurations.Count == 0 ? 0 : solvedDurations.Max(),
    sourceHashes = Directory.GetFiles(Path.Combine(root, "IndependentSolver", "Core"), "*.cs").ToDictionary(Path.GetFileName, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))), levels = rows };
File.WriteAllText(Path.Combine(output, "summary.json"), JsonConvert.SerializeObject(summary, Formatting.Indented));
Console.WriteLine(JsonConvert.SerializeObject(new { counts, summary.wallSeconds, summary.solvedMeanSeconds, summary.solvedMedianSeconds, summary.solvedP95Seconds }, Formatting.Indented));
