using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;
using ReferenceJson = CatDom.Reference.V42.IO.SolverJson;

internal static class ResearchAcceptance
{
    internal static void Run(string root,string experiment,string mode,string filter)
    {
#if DEBUG
        throw new InvalidOperationException("Research acceptance requires a Release build.");
#endif
        var levels=File.ReadAllLines(Path.Combine(root,"docs/benchmarks/performance.csv")).Skip(1)
            .Select(line=>line.Split(',')).Where(row=>long.Parse(row[2])>10000).Select(row=>row[0]).ToArray();
        if(levels.Length!=21)throw new Exception("Research scope changed.");
        levels=levels.Where(level=>level!="Level00206"&&level!="Level00233").ToArray();
        if(levels.Length!=19)throw new Exception("Final research scope must contain 19 levels.");
        if(filter!=null)
        {
            var requested=filter.Split(',').ToHashSet();
            if(requested.Any(level=>!levels.Contains(level)))throw new Exception("Level outside research scope.");
            levels=levels.Where(requested.Contains).ToArray();
        }
        string assemblyPath;
        Func<string,CancellationToken,JObject> solve;
        if(mode=="baseline")
        {
            assemblyPath=Path.Combine(root,"artifacts/unity/IndependentSolver.dll");
            var assembly=Assembly.LoadFile(assemblyPath);
            var method=assembly.GetType("CatDom.CoreSolver.CatLevelSolver",true).GetMethod("SolveLevel",new[]{typeof(string),typeof(CancellationToken)});
            solve=(json,token)=>JObject.FromObject(method.Invoke(null,new object[]{json,token}));
        }
        else if(mode=="current")
        {
            assemblyPath=typeof(Product.CatLevelSolver).Assembly.Location;
            solve=(json,token)=>
            {
                var result=Product.CatLevelSolver.SolveLevel(json,token);
                var output=JObject.FromObject(result);
                output["abstractExpanded"]=result.abstractExpanded;
                return output;
            };
        }
        else throw new Exception("Expected baseline or current mode.");
        var catalog=ReferenceJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        string directory=Path.Combine(root,"docs/research",experiment);
        Directory.CreateDirectory(directory);
        var reports=new List<object>();
        foreach(var level in levels)
        {
            string file=Path.Combine(root,"Levels",level+".json"),json=File.ReadAllText(file);
            JObject warmup;
            using(var token=new CancellationTokenSource(TimeSpan.FromMinutes(3)))warmup=solve(json,token.Token);
            var samples=new List<JObject>();
            for(int iteration=0;iteration<3;iteration++)
            {
                using var token=new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var result=solve(json,token.Token);
                bool replayed=false;
                if((string)result["status"]=="Solved")
                {
                    var moves=result["moves"].ToObject<List<Reference.SolverMove>>();
                    var replay=Reference.CatLevelSolver.VerifyPlan(ReferenceJson.ReadLevel(json,catalog),moves,0,true);
                    replayed=replay.status==Reference.SolveStatus.Solved&&replay.finalState.remainingCatIds.Count==0;
                    if(!replayed)throw new Exception("Independent replay failed "+level);
                }
                var sample=new JObject { ["iteration"]=iteration,["status"]=result["status"],
                    ["expanded"]=result["expanded"],["solveTimeMs"]=result["solveTimeMs"],
                    ["abstractExpanded"]=(long?)result["abstractExpanded"]??0,
                    ["replayed"]=replayed,["drags"]=((JArray)result["moves"]).Count,
                    ["steps"]=((JArray)result["moves"]).Sum(m=>((JArray)m["path"]).Count-1),
                    ["planHash"]=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(result["moves"].ToString(Formatting.None)))) };
                samples.Add(sample);
                File.WriteAllText(Path.Combine(directory,level+".sample"+iteration+".json"),result.ToString(Formatting.Indented));
                Console.WriteLine($"{level} sample={iteration} expanded={sample["expanded"]} ms={(double)sample["solveTimeMs"]:F1} replay={replayed}");
            }
            double median=samples.Select(s=>(double)s["solveTimeMs"]).Order().ElementAt(1);
            bool passed=samples.All(s=>(bool)s["replayed"]&&(string)s["status"]=="Solved"&&(long)s["expanded"]<30000)&&median<3000;
            var report=new {level,mode,medianMs=median,passed,warmupStatus=(string)warmup["status"],samples,
                expandedExclusiveLimit=30000,medianExclusiveLimitMs=3000,
                assemblyHash=Hash(assemblyPath),levelHash=Hash(file),runtime=Environment.Version.ToString(),
                referenceAssemblyHash=Hash(typeof(Reference.CatLevelSolver).Assembly.Location),
                harnessAssemblyHash=Hash(typeof(ResearchAcceptance).Assembly.Location),
                catalogHash=Hash(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")),
                machine=Environment.MachineName,os=Environment.OSVersion.ToString(),processors=Environment.ProcessorCount,
                method="Release; sequential; one target warm-up; three samples; median; internal solveTimeMs includes parsing and replay"};
            reports.Add(report);
            File.WriteAllText(Path.Combine(directory,level+".json"),JsonConvert.SerializeObject(report,Formatting.Indented));
            File.WriteAllText(Path.Combine(directory,"summary.json"),JsonConvert.SerializeObject(reports,Formatting.Indented));
            Console.WriteLine($"{level} median={median:F1}ms acceptance={passed}");
        }
    }
    private static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
}
