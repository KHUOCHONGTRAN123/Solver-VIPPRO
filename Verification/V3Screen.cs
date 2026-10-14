using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

// Diagnostic coverage screen. Internal replay is NOT independent runtime acceptance.
internal static class V3Screen
{
    private static int ReadPositiveLimit(string name,int fallback)
    {
        string value=Environment.GetEnvironmentVariable(name);
        if(string.IsNullOrEmpty(value))return fallback;
        if(!int.TryParse(value,out int limit)||limit<=0)
            throw new ArgumentException(name+" must be a positive integer.");
        return limit;
    }
    internal static void Run(string root, string selection = null, string corpusPath = null, string outputPath = null)
    {
        string corpus=corpusPath==null ? Path.Combine(root,"Levels_V3_NewMechanics") : Path.GetFullPath(corpusPath);
        string directory=outputPath==null ? Path.Combine(root,"Verification/results/v3-screen") : Path.GetFullPath(outputPath);
        Directory.CreateDirectory(directory);
        string assemblyHash=Hash(typeof(Product.CatLevelSolver).Assembly.Location);
        string verificationAssemblyHash=Hash(typeof(V3Screen).Assembly.Location);
        // A diagnostic collected while other CPU-heavy work runs must never
        // masquerade as a serial timing acceptance sample or reusable serial cache.
        string measurementContext=Environment.GetEnvironmentVariable("CATDOM_SCREEN_CONTEXT") ?? "unspecified-diagnostic";
        int timeoutMs=ReadPositiveLimit("CATDOM_SCREEN_TIMEOUT_MS",3000);
        int expandedLimit=ReadPositiveLimit("CATDOM_SCREEN_EXPANDED_LIMIT",30000);
        var rows=new List<JObject>();
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var selected=selection?.Split(',').ToHashSet();
        foreach(string file in Directory.GetFiles(corpus,"*.json").OrderBy(f=>f))
        {
            if(selected!=null && !selected.Contains(Path.GetFileNameWithoutExtension(file)))continue;
            string level=Path.GetFileNameWithoutExtension(file), levelHash=Hash(file);
            string output=Path.Combine(directory,level+".json");
            JObject row=null;
            if(File.Exists(output))
            {
                var cached=JObject.Parse(File.ReadAllText(output));
                if((string)cached["assemblyHash"]==assemblyHash && (string)cached["levelHash"]==levelHash
                    && (string)cached["measurementContext"]==measurementContext
                    && (string)cached["verificationAssemblyHash"]==verificationAssemblyHash && (int?)cached["timeoutMs"]==timeoutMs && (int?)cached["expandedLimit"]==expandedLimit)
                    row=cached;
            }
            if(row==null)
            {
                string json=File.ReadAllText(file);
                var unknown=new List<string>();
                try{unknown=Product.IO.SolverJson.ReadLevel(json,catalog).ignoredMechanics;}
                catch(Exception ex){unknown.Add("parse: "+ex.Message);}
                using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                var result=Product.CatLevelSolver.SolveLevelWithProgress(json,cancellation.Token,r=>
                {
                    if(r.expanded>=expandedLimit)cancellation.Cancel();
                });
                bool? countdownInvariantVerified=null;
                bool? holeBombInvariantVerified=null;
                string holeBombInvariantError=null;
                if(result.status==Product.SolverStatus.Solved && ((JArray)JObject.Parse(json)["holeInfos"]??new JArray()).Any(h=>((int?)h["timeLimited"]??0)>0))
                {
                    try{HoleBombReplayOracle.Verify(json,result.diagnosticInitialEaten.Concat(result.moves.SelectMany(m=>m.eaten)));holeBombInvariantVerified=true;}
                    catch(Exception ex){holeBombInvariantVerified=false;holeBombInvariantError=ex.Message;}
                }
                string countdownInvariantError=null;
                if(result.status==Product.SolverStatus.Solved && ((JArray)JObject.Parse(json)["holeInfos"]??new JArray()).Any(h=>((int?)h["countdownMoveLimit"]??0)>0))
                {
                    try
                    {
                        CountdownReplayOracle.Verify(json,result.moves.Select(m=>new Product.EngineMove{holeId=m.holeId,start=m.start,path=m.path,eaten=m.eaten}).ToArray(),result.diagnosticInitialEaten);
                        countdownInvariantVerified=true;
                    }
                    catch(Exception ex){countdownInvariantVerified=false;countdownInvariantError=ex.Message;}
                }
                row=JObject.FromObject(new {level,levelHash,assemblyHash,verificationAssemblyHash,measurementContext,timeoutMs,expandedLimit,
                    status=result.status.ToString(),result.message,result.expanded,result.solveTimeMs,
                    result.searchAlgorithm,result.baselineExpanded,result.improvedExpanded,
                    meetsConfiguredTargets=result.status==Product.SolverStatus.Solved && result.expanded<=expandedLimit && result.solveTimeMs<=timeoutMs && countdownInvariantVerified!=false && holeBombInvariantVerified!=false,
                    meetsNumericalTargets=result.status==Product.SolverStatus.Solved && result.expanded<30000 && result.solveTimeMs<3000 && countdownInvariantVerified!=false && holeBombInvariantVerified!=false,
                    holeBombInvariantVerified,holeBombInvariantError,
                    countdownInvariantVerified,countdownInvariantError,
                    independentReplayVerified=false,unknownMechanics=unknown,initialEaten=result.diagnosticInitialEaten,moves=result.moves});
                File.WriteAllText(output,row.ToString(Formatting.Indented));
            }
            rows.Add(row);
            if(rows.Count%100==0)Console.WriteLine($"Screened {rows.Count} levels");
        }
        var summary=new {
            method="Release diagnostic, sequential; explicit timeoutMs and expandedLimit; internal replay and specific invariants, not full independent acceptance.",
            assemblyHash,verificationAssemblyHash,measurementContext,timeoutMs,expandedLimit,selection,count=rows.Count,
            statuses=rows.GroupBy(r=>(string)r["status"]).ToDictionary(g=>g.Key,g=>g.Count()),
            meetsConfiguredTargets=rows.Count(r=>(bool)r["meetsConfiguredTargets"]),
            meetsNumericalTargets=rows.Count(r=>(bool)r["meetsNumericalTargets"]),
            unknownMechanics=rows.SelectMany(r=>r["unknownMechanics"].Values<string>()).GroupBy(s=>s).OrderByDescending(g=>g.Count()).ToDictionary(g=>g.Key,g=>g.Count()),
            rows=rows.Select(r=>new {level=(string)r["level"],status=(string)r["status"],expanded=(long)r["expanded"],solveTimeMs=(double)r["solveTimeMs"],meetsConfiguredTargets=(bool)r["meetsConfiguredTargets"],meetsNumericalTargets=(bool)r["meetsNumericalTargets"]})};
        string summaryPath=Path.Combine(directory,selected==null ? "summary.json" : "summary-selected.json");
        File.WriteAllText(summaryPath,JsonConvert.SerializeObject(summary,Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(new{summary.count,summary.statuses,summary.meetsConfiguredTargets,summary.meetsNumericalTargets,summaryPath}));
    }
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}



