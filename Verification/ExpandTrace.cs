using System.Diagnostics;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class ExpandTrace
{
    internal static void Run(string root,string selection,string output,int timeoutMs,string corpus=null)
    {
        Directory.CreateDirectory(output);
        var selected=selection=="all"?null:selection.Split(',').ToHashSet();
        string catalog=File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json"));
        string productHash=Hash(typeof(Product.CatLevelSolver).Assembly.Location);
        var rows=new List<object>();
        foreach(string file in Directory.GetFiles(corpus??Path.Combine(root,"Levels"),"*.json").OrderBy(p=>p))
        {
            string level=Path.GetFileNameWithoutExtension(file);
            if(selected!=null && !selected.Contains(level))continue;
            string json=File.ReadAllText(file);
            var input=JObject.Parse(json);
            if(selected==null && !((JArray)input["holeInfos"]).Any(h=>h["posExpand"] is JObject p && (int)p["x"]>=0 && (int)p["y"]>=0))continue;
            var trace=new List<object>();
            string stage=null;
            long previousMs=-1000,observedExpanded=0;
            int observedMoves=0;
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            using var self=Process.GetCurrentProcess();
            long peakPrivateBytes=0,lastMemoryCheck=-1000;
            bool memoryCancelled=false;
            const long memoryLimitBytes=2L*1024*1024*1024;
            long allocations=GC.GetTotalAllocatedBytes();
            var clock=Stopwatch.StartNew();
            using var cancellation=new CancellationTokenSource(timeoutMs);
            var result=Product.CatLevelSolver.SolveLevelWithProgress(json,cancellation.Token,r=>
            {
                observedExpanded=r.expanded;observedMoves=r.moves.Count;
                if(clock.ElapsedMilliseconds-lastMemoryCheck>=250)
                {
                    lastMemoryCheck=clock.ElapsedMilliseconds;self.Refresh();
                    peakPrivateBytes=Math.Max(peakPrivateBytes,self.PrivateMemorySize64);
                    if(peakPrivateBytes>memoryLimitBytes){memoryCancelled=true;cancellation.Cancel();}
                }
                if(stage!=r.searchStage || clock.ElapsedMilliseconds-previousMs>=1000)
                {
                    stage=r.searchStage;previousMs=clock.ElapsedMilliseconds;
                    bool keepState=stage=="expand-stalled" || stage?.StartsWith("curtain-")==true || stage?.StartsWith("expand-retry-")==true || stage?.StartsWith("bomb-retry-")==true || stage?.StartsWith("bomb-frontier-progress-")==true;
                    trace.Add(new{stage,ms=clock.Elapsed.TotalMilliseconds,r.expanded,r.eventBacktracks,moves=r.moves.Count,state=keepState?r.finalState:null,partialMoves=keepState?JArray.FromObject(r.moves):null});
                }
            });
            allocations=GC.GetTotalAllocatedBytes()-allocations;
            bool? bombInvariant=null;string bombInvariantError=null;
            if(result.status==Product.SolverStatus.Solved)
            {
                try{HoleBombReplayOracle.Verify(json,result.diagnosticInitialEaten.Concat(result.moves.SelectMany(m=>m.eaten)));bombInvariant=true;}
                catch(Exception ex){bombInvariant=false;bombInvariantError=ex.Message;}
            }
            string replay=result.status==Product.SolverStatus.Solved?"Verified":"NotSolved",replayError=null;
            if(result.status==Product.SolverStatus.Solved)
            {
                try{V3IndependentReplay.Verify(json,catalog,JArray.FromObject(result.moves));}
                catch(Exception ex){replay="Rejected";replayError=ex.Message;}
            }
            var row=new{level,levelHash=Hash(file),productHash,timeoutMs,status=result.status.ToString(),result.message,
                result.solveTimeMs,result.expanded,observedExpanded,observedMoves,allocatedBytes=allocations,peakPrivateBytes,memoryCancelled,memoryLimitBytes,
                independentReplay=replay,replayError,bombInvariant,bombInvariantError,diagnosticFinalState=result.diagnosticFinalState,moves=result.moves,trace};
            File.WriteAllText(Path.Combine(output,level+".json"),JsonConvert.SerializeObject(row,Formatting.Indented));
            rows.Add(new{level,row.status,row.solveTimeMs,row.expanded,replay});
            Console.WriteLine(JsonConvert.SerializeObject(rows.Last()));
        }
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonConvert.SerializeObject(new{productHash,rows},Formatting.Indented));
    }
    private static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
}
