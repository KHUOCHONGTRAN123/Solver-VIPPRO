using System.Diagnostics;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;
using ReferenceJson = CatDom.Reference.V42.IO.SolverJson;

string root = Path.GetFullPath(args.Length > 1 ? args[1] : ".");
if(args.Length>0&&args[0]=="budget-5000"){EasyComparison.Run(root,true);return;}
if(args.Length>0&&args[0]=="hybrid-check"){HybridVerification.Run(root);return;}
if(args.Length>0&&args[0]=="easy-comparison"){EasyComparison.Run(root);return;}
if(args.Length>0&&args[0]=="route-graph-tests"){RouteGraphTests.Run(root);return;}
if(args.Length>0&&args[0]=="phase-search"){PhaseSearch.Run(root,args[2],args[3],args.Length>4?int.Parse(args[4]):10000);return;}
if(args.Length>0&&args[0]=="phase-fixture"){PhaseFixture.Run(root,args[2],args[3],int.Parse(args[4]));return;}
if(args.Length>0&&args[0]=="constraint-probe"){ConstraintProbe.Run(root,args[2],args.Length>3&&args[3]=="hardest");return;}
if(args.Length>0&&args[0]=="research-acceptance"){ResearchAcceptance.Run(root,args[2],args.Length>3?args[3]:"current",args.Length>4?args[4]:null);return;}
if(args.Length>0&&args[0]=="research"){ResearchBenchmark.Run(root,args.Length>2?args[2]:"baseline",args.Length>3&&args[3]!="all"?args[3]:null,args.Length>4?long.Parse(args[4]):150000);return;}
if(args.Length>0&&args[0]=="profile"){StageProfile.Run(root);return;}
if(args.Length>0 && args[0]=="worker")
{
    var mode=args[2]; var file=args[3]; var repetitions=int.Parse(args[4]);
    var json=File.ReadAllText(file);
    var catalog=ReferenceJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
    Product.CatLevelSolver.SolveLevel(File.ReadAllText(Path.Combine(root,"Levels/Level00001.json")));
    Reference.CatLevelSolver.Solve(ReferenceJson.ReadLevel(File.ReadAllText(Path.Combine(root,"Levels/Level00001.json")),catalog),Reference.SolverConfig.CreateRouteClearingDefault());
    if(args.Length>6&&(args[6]=="warm-target"||args[6]=="steady-target"))
    {
      int warmups=args[6]=="steady-target"?3:1;
      for(int warm=0;warm<warmups;warm++)
      {
        if(mode=="reference")Reference.CatLevelSolver.Solve(ReferenceJson.ReadLevel(json,catalog),Reference.SolverConfig.CreateRouteClearingDefault());
        else Product.CatLevelSolver.SolveLevel(json);
      }
      if(args[6]=="steady-target")Thread.Sleep(500); // Harness only: allow background tiered JIT work to settle.
    }
    var samples=new List<object>();
    for(int iteration=0;iteration<repetitions;iteration++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long allocated=GC.GetAllocatedBytesForCurrentThread();var g0=GC.CollectionCount(0);var g1=GC.CollectionCount(1);var g2=GC.CollectionCount(2);
        var timer=Stopwatch.StartNew(); long expanded;string status;JArray moves;int gc0,gc1,gc2;
        if(mode=="reference")
        {
            using var cancellation=new CancellationTokenSource();var config=Reference.SolverConfig.CreateRouteClearingDefault();
            config.progress=r=>{if(r.expanded>100000)cancellation.Cancel();};
            var result=Reference.CatLevelSolver.Solve(ReferenceJson.ReadLevel(json,catalog),config,cancellation.Token);
            timer.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;gc0=GC.CollectionCount(0)-g0;gc1=GC.CollectionCount(1)-g1;gc2=GC.CollectionCount(2)-g2;expanded=result.expanded;status=result.status.ToString();
            moves=JArray.FromObject(result.moves.Select(m=>new {m.holeId,m.start,m.path,m.eaten}));
        }
        else
        {
            using var cancellation=new CancellationTokenSource();
            var result=Product.CatLevelSolver.SolveLevelWithProgress(json,cancellation.Token,r=>{if(r.expanded>100000)cancellation.Cancel();});
            timer.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;gc0=GC.CollectionCount(0)-g0;gc1=GC.CollectionCount(1)-g1;gc2=GC.CollectionCount(2)-g2;expanded=result.expanded;status=result.status.ToString();moves=JArray.FromObject(result.moves);
            Assert(double.IsFinite(result.solveTimeMs)&&result.solveTimeMs>=0,"Invalid solveTimeMs");
        }
        var expected=JObject.Parse(File.ReadAllText(Path.Combine(root,"Verification/V42Results",Path.GetFileName(file))));
        var expectedMoves=new JArray(((JArray)expected["moves"]).Select(m=>new JObject { ["holeId"]=m["holeId"],["start"]=m["start"],["path"]=m["path"],["eaten"]=m["eaten"] }));
        Assert(status==(string)expected["status"]&&expanded==(long)expected["expanded"],$"Status/expanded mismatch {file}: {status}/{expanded}");
        Assert(JToken.DeepEquals(moves,expectedMoves),"Move mismatch "+file);
        var replayMoves=JsonConvert.DeserializeObject<List<Reference.SolverMove>>(moves.ToString());
        var replay=Reference.CatLevelSolver.VerifyPlan(ReferenceJson.ReadLevel(json,catalog),replayMoves,0,true);
        Assert(replay.status==Reference.SolveStatus.Solved,"Replay failed "+file);
        Assert(replay.finalState.remainingCatIds.Count==0&&replay.finalState.boxes.All(b=>b.consumed>=ReferenceJson.ReadLevel(json,catalog).boxes.Single(i=>i.id==b.id).colors.Length),"Final state failed");
        samples.Add(new {iteration,solveTimeMs=timer.Elapsed.TotalMilliseconds,allocatedBytes=allocated,gen0=gc0,gen1=gc1,gen2=gc2,expanded});
    }
    using var self=Process.GetCurrentProcess();
    var assemblyPath=mode=="reference"?typeof(Reference.CatLevelSolver).Assembly.Location:typeof(Product.CatLevelSolver).Assembly.Location;
    File.WriteAllText(args[5],JsonConvert.SerializeObject(new{level=Path.GetFileNameWithoutExtension(file),mode,status="Passed",assemblyHash=Hash(assemblyPath),levelHash=Hash(file),planHash=Hash(Path.Combine(root,"Verification/V42Results",Path.GetFileName(file))),peakWorkingSetBytes=self.PeakWorkingSet64,samples},Formatting.Indented));
    Console.WriteLine($"PASS {Path.GetFileName(file)} {mode}");return;
}
if(args.Length>0 && args[0]=="all")
{
    Directory.CreateDirectory(Path.Combine(root,"Verification/results"));
    foreach(var file in Directory.GetFiles(Path.Combine(root,"Levels"),"*.json").OrderBy(x=>x))
    {
        foreach(var mode in new[]{"reference","optimized"})
        {
            string output=Path.Combine(root,"Verification/results",Path.GetFileNameWithoutExtension(file)+"."+mode+".json");
            if(File.Exists(output))
            {
                var previous=JObject.Parse(File.ReadAllText(output));
                var assemblyPath=mode=="reference"?typeof(Reference.CatLevelSolver).Assembly.Location:typeof(Product.CatLevelSolver).Assembly.Location;
                bool provenance=(string)previous["assemblyHash"]==Hash(assemblyPath)&&(string)previous["levelHash"]==Hash(file)&&(string)previous["planHash"]==Hash(Path.Combine(root,"Verification/V42Results",Path.GetFileName(file)));
                if(provenance&&(string)previous["status"]=="Passed"&&((JArray)previous["samples"]).Count==3){Console.WriteLine("Retained verified "+Path.GetFileName(file)+" "+mode);continue;}
            }
            var psi=new ProcessStartInfo("dotnet"){UseShellExecute=false};
            foreach(var value in new[]{typeof(Program).Assembly.Location,"worker",root,mode,file,"3",output})psi.ArgumentList.Add(value);
            using var worker=Process.Start(psi);
            while(!worker.WaitForExit(250))
            {
                try{worker.Refresh();if(worker.PeakWorkingSet64>2147483648){worker.Kill(true);throw new Exception("RAM budget exceeded: "+file);}}
                catch(InvalidOperationException) when(worker.HasExited){break;}
            }
            Assert(worker.ExitCode==0,"Worker failed "+file);
        }
    }
    Console.WriteLine("PASS 299-level differential and performance runs");return;
}
var random=new Random(42);
HybridTests.Run(root);
CheckpointTests.Run(root);
RouteTests.Run();
var heap=new Product.StableMinHeap<int>();
var orderedHeap=new SortedSet<(long score,int serial,int node)>(Comparer<(long score,int serial,int node)>.Create((a,b)=>a.score!=b.score?a.score.CompareTo(b.score):a.serial.CompareTo(b.serial)));
for(int i=0;i<10000;i++){var entry=((long)random.Next(0,100),i,i);heap.Add(entry);orderedHeap.Add(entry);}
while(heap.Count>0){var expected=orderedHeap.Min;orderedHeap.Remove(expected);Assert(heap.Pop()==expected,"Heap tie-break mismatch");}
for(int i=0;i<10000;i++)
{
    var a=new BigInteger(random.NextInt64())<<random.Next(0,210);var b=new BigInteger(random.NextInt64())<<random.Next(0,210);
    var x=new Product.CellMask(a);var y=new Product.CellMask(b);
    Assert((x&y).ToBigInteger()==(a&b)&&(x|y).ToBigInteger()==(a|b)&&(x^y).ToBigInteger()==(a^b),"Bitboard boolean mismatch");
    Assert((~x).ToBigInteger()==~a&&((~x)&y).ToBigInteger()==((~a)&b),"Bitboard complement mismatch");
    Assert((x<<i%180).ToBigInteger()==(a<<i%180)&&(x-y).ToBigInteger()==a-b,"Bitboard arithmetic mismatch");
    int count=0;var bits=a;while(!bits.IsZero){bits&=bits-1;count++;}Assert(x.PopCount()==count,"PopCount mismatch");
}
var invalid=Product.CatLevelSolver.SolveLevel("{");Assert(invalid.status==Product.SolverStatus.InvalidInput&&double.IsFinite(invalid.solveTimeMs),"Invalid JSON result");
foreach(var malformed in new[]{"{\"sizeX\":3,\"sizeY\":3,\"holeInfos\":[5]}","{\"sizeX\":\"abc\",\"sizeY\":3}","null","[]",""})
{var result=Product.CatLevelSolver.SolveLevel(malformed);Assert(result.status==Product.SolverStatus.InvalidInput&&double.IsFinite(result.solveTimeMs)&&result.solveTimeMs>=0,"Malformed level must return InvalidInput");}
foreach(int width in new[]{64,65,128,129,256})
{
    var board=new Reference.BoardInput {width=width,height=1};
    board.holes.Add(new Reference.HoleInput {id=0,color=1,remaining=1,position=new Reference.Cell(0,0),footprint=new[]{new Reference.Cell(0,0)}});
    board.cats.Add(new Reference.CatInput {id=0,color=1,position=new Reference.Cell(width-1,0)});
    var result=V1TestAdapter.Solve(board,Reference.SolverConfig.CreateRouteClearingDefault());
    Assert(result.status==Reference.SolveStatus.Solved&&V1TestAdapter.VerifyPlan(board,result.moves,0,true).status==Reference.SolveStatus.Solved,"Mask/packed-state boundary failed "+width);
}
using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();var result=Product.CatLevelSolver.SolveLevel("{}",cancelled.Token);Assert(result.status==Product.SolverStatus.Cancelled&&result.solveTimeMs>=0,"Cancellation result");}
var fixture=File.ReadAllText(Path.Combine(root,"Levels/Level00001.json"));
var unsupported=JObject.Parse(fixture);unsupported["unknownMechanic"]=true;
var unsupportedResult=Product.CatLevelSolver.SolveLevel(unsupported.ToString());
Assert(unsupportedResult.status==Product.SolverStatus.UnsupportedMechanics&&double.IsFinite(unsupportedResult.solveTimeMs)&&unsupportedResult.solveTimeMs>=0,"Unsupported mechanics timing");
var noNext=Product.CatLevelSolver.SolveLevel("{\"sizeX\":1,\"sizeY\":1,\"catInfos\":[{\"colorId\":1,\"position\":{\"x\":0,\"y\":0}}]}");
Assert(noNext.status==Product.SolverStatus.NoNextCatReachable&&double.IsFinite(noNext.solveTimeMs)&&noNext.solveTimeMs>=0,"No-next-cat timing");
using(var runningCancellation=new CancellationTokenSource())
{
    runningCancellation.CancelAfter(30);
    var cancelledSearch=Product.CatLevelSolver.SolveLevel(File.ReadAllText(Path.Combine(root,"Levels/Level00206.json")),runningCancellation.Token);
    Assert(cancelledSearch.status==Product.SolverStatus.Cancelled&&double.IsFinite(cancelledSearch.solveTimeMs)&&cancelledSearch.solveTimeMs>=0,"Cancellation during search");
}
var parallel=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>Product.CatLevelSolver.SolveLevel(fixture))));
Assert(parallel.All(r=>r.status==Product.SolverStatus.Solved),"Concurrent solves failed");
Console.WriteLine("PASS mask equivalence, malformed JSON, cancellation, concurrent calls");
static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
static string Hash(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
