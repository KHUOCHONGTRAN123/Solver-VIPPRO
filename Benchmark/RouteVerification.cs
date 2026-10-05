using CatDom.CoreSolver;
using CatDom.CoreSolver.IO;
using Newtonsoft.Json;

internal static class RouteVerification
{
    internal static void RunCompleted(string root, string[] runs)
    {
        root = Path.GetFullPath(root);
        var catalog = SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root, "IndependentSolver/IO/HoleShapes.json")));
        var levels = Directory.GetFiles(Path.Combine(root, "Levels"), "*.json").ToDictionary(Path.GetFileNameWithoutExtension);
        var completed = new Dictionary<string, string>();
        foreach (var run in runs)
        {
            var folder = Path.Combine(root, "Benchmark/results", run);
            using var stream = new FileStream(Path.Combine(folder, "metrics.csv"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var snapshot = reader.ReadToEnd();
            // The writer flushes complete rows; ignore a trailing row still being written.
            var lines = snapshot.Split('\n');
            foreach (var line in lines.Take(lines.Length - 1).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var name = line.Split(',')[0].Trim('"');
                if (!levels.ContainsKey(name)) throw new Exception("Unknown completed level: " + name);
                completed.TryAdd(name, folder);
            }
        }
        var rows = new List<object>();
        foreach (var entry in completed.OrderBy(e => e.Key))
        {
            var result = JsonConvert.DeserializeObject<SolveResult>(File.ReadAllText(Path.Combine(entry.Value, entry.Key + ".json")));
            var input = SolverJson.ReadLevel(File.ReadAllText(levels[entry.Key]), catalog);
            if (result.status == SolveStatus.Solved && CatLevelSolver.VerifyPlan(input, result.moves, 0).status != SolveStatus.Solved)
                throw new Exception("Persisted replay failed: " + entry.Key);
            rows.Add(new { level = entry.Key, sourceRun = Path.GetFileName(entry.Value), status = result.status.ToString(),
                replay = result.status == SolveStatus.Solved ? "Solved" : "NotApplicable" });
        }
        File.WriteAllText(Path.Combine(root, "Benchmark/results/completed-replay-audit.json"), JsonConvert.SerializeObject(rows, Formatting.Indented));
        Console.WriteLine($"PASS: {rows.Count} unique completed results read from disk; all solved plans replayed without timeout. Full299 audit remains required.");
    }

    internal static void Run(string root, string run)
    {
        root = Path.GetFullPath(root);
        var output = Path.Combine(root, "Benchmark/results", run);
        var catalog = SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root, "IndependentSolver/IO/HoleShapes.json")));
        var files = Directory.GetFiles(Path.Combine(root, "Levels"), "*.json").OrderBy(f => f).ToArray();
        if (files.Length != 299) throw new Exception("Expected the complete 299-level dataset.");
        var rows = new List<object>();
        foreach (var file in files)
        {
            var result = JsonConvert.DeserializeObject<SolveResult>(File.ReadAllText(Path.Combine(output, Path.GetFileName(file))));
            var input = SolverJson.ReadLevel(File.ReadAllText(file), catalog);
            if (result.status == SolveStatus.Solved)
            {
                var replay = CatLevelSolver.VerifyPlan(input, result.moves, 0);
                if (replay.status != SolveStatus.Solved) throw new Exception($"Replay failed for {file}: {replay.message}");
                var initialPlayable = input.holes.Where(h => h.color < 1000).Select(h => h.id).ToHashSet();
                if (result.finalState.holes.Any(h => initialPlayable.Contains(h.id) && !h.finished))
                    throw new Exception($"Playable holes remain in {file}");
                if (result.finalState.boxes.Any(b => b.consumed < input.boxes.Single(i => i.id == b.id).colors.Length))
                    throw new Exception($"Queued cats remain in {file}");
            }
            rows.Add(new { level = Path.GetFileNameWithoutExtension(file), status = result.status.ToString(),
                replay = result.status == SolveStatus.Solved ? "Solved" : "NotApplicable" });
        }
        File.WriteAllText(Path.Combine(output, "replay-audit.json"), JsonConvert.SerializeObject(rows, Formatting.Indented));
        Console.WriteLine($"PASS: {rows.Count} results inspected; all solved plans replayed with no time limit.");
    }
}
