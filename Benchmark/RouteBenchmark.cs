using CatDom.CoreSolver;
using CatDom.CoreSolver.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

internal static class RouteBenchmark
{
    internal static void Run(string[] args)
    {
        var root = Path.GetFullPath(args[1]);
        var output = Path.Combine(root, "Benchmark", "results", args.Length > 4 ? args[4] : "route-base-v4");
        Directory.CreateDirectory(output);
        if (args[0] == "route-worker")
        {
            var catalog = SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root, "IndependentSolver/IO/HoleShapes.json")));
            var input = SolverJson.ReadLevel(File.ReadAllText(args[2]), catalog);
            var config = SolverConfig.CreateRouteClearingDefault();
            config.captureSubproblemInputs = true;
            config.progress = r => File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(args[2]) + ".progress.json"), SolverJson.WriteResult(r));
            var result = CatLevelSolver.Solve(input, config);
            using var self = Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(args[2]) + ".memory.json"),
                JsonConvert.SerializeObject(new { peakWorkingSetBytes = self.PeakWorkingSet64 }));
            File.WriteAllText(Path.Combine(output, Path.GetFileName(args[2])), SolverJson.WriteResult(result));
            Console.WriteLine($"{result.status} expanded={result.expanded} moves={result.moves.Count}");
            return;
        }
        var files = Directory.GetFiles(Path.Combine(root, "Levels"), "*.json").OrderBy(f => f).ToArray();
        if (args.Length > 2 && args[2] != "*") files = files.Where(f => Path.GetFileNameWithoutExtension(f) == args[2]).ToArray();
        if (args.Length > 3) files = files.Where(f => string.CompareOrdinal(Path.GetFileNameWithoutExtension(f), args[3]) >= 0).ToArray();
        var rows = new List<object>();
        using var csv = new StreamWriter(Path.Combine(output, "metrics.csv"));
        csv.WriteLine("level,status,calculations,peakWorkingSetBytes,moves");
        foreach (var file in files)
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(RouteBenchmark).Assembly.Location);
            start.ArgumentList.Add("route-worker"); start.ArgumentList.Add(root); start.ArgumentList.Add(file);
            start.ArgumentList.Add("unused"); start.ArgumentList.Add(Path.GetFileName(output));
            using var worker = Process.Start(start);
            var stdout = worker.StandardOutput.ReadToEndAsync(); var stderr = worker.StandardError.ReadToEndAsync();
            worker.WaitForExit();
            var name = Path.GetFileNameWithoutExtension(file);
            if (worker.ExitCode != 0) throw new Exception($"Worker failed for {name}: {stderr.GetAwaiter().GetResult()}");
            var result = JObject.Parse(File.ReadAllText(Path.Combine(output, Path.GetFileName(file))));
            var row = new { level = name, status = (string)result["status"], calculations = (long)result["expanded"],
                peakWorkingSetBytes = (long)JObject.Parse(File.ReadAllText(Path.Combine(output, name + ".memory.json")))["peakWorkingSetBytes"], moves = result["moves"].Count() };
            rows.Add(row);
            csv.WriteLine($"{name},{row.status},{row.calculations},{row.peakWorkingSetBytes},{row.moves}"); csv.Flush();
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonConvert.SerializeObject(rows, Formatting.Indented));
            Console.WriteLine($"{rows.Count}/{files.Length} {name}: {stdout.GetAwaiter().GetResult().Trim()} RAM={row.peakWorkingSetBytes / 1048576.0:F1}MiB");
        }
    }
}
