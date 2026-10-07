using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;

internal static class PhaseSearch
{
    // Diagnostic single phase only; no acceptance or complete-level claim.
    internal static void Run(string root,string fixtureName,string method,int limit)
    {
        var scope=File.ReadAllLines(Path.Combine(root,"docs/benchmarks/performance.csv"))
            .Skip(1).Select(s=>s.Split(',')).Where(r=>long.Parse(r[2])>10000).Select(r=>r[0]).ToHashSet();
        if(!scope.Contains(fixtureName.Split('.')[0])||fixtureName!=Path.GetFileName(fixtureName))throw new ArgumentException("Outside fixture scope");
        string path=Path.Combine(root,"docs/research/phase-fixtures",fixtureName);
        string json=File.ReadAllText(path);
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var trace=new List<object>();
        var clock=Stopwatch.StartNew();
        var input=JsonConvert.DeserializeObject<Product.BoardInput>(json)!;
        var result=new Product.EngineResult();
        var context=new Product.EngineContext{progress=r=>{
            trace.Add(new{r.expanded,r.abstractExpanded,r.searchStage,ms=clock.Elapsed.TotalMilliseconds});
            if(r.expanded>=limit)cancellation.Cancel();
        }};
        var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic)!;
        object engine=Activator.CreateInstance(type,flags,null,new object[]{input,context,cancellation.Token,result},null)!;
        object Call(string name,params object[] arguments)=>type.GetMethod(name,flags)!.Invoke(engine,arguments)!;
        object Field(string name)=>type.GetField(name,flags)!.GetValue(engine)!;
        bool cancelled=false,found=false,replayed=false;
        var auditRows=new List<object>();
        try
        {
            Call("Consume",-1);
            type.GetField("stats",flags)!.SetValue(engine,new Product.SubproblemStats());
            Call("Prepare");
            type.GetField("bestDepth",flags)!.SetValue(engine,int.MaxValue);
            switch(method)
            {
                case "pipeline": Call("SearchRouteClearing");break;
                case "pair": Call("SearchPairPatterns",limit,false);break;
                case "additive": Call("SearchAdditivePatterns",limit,false,false);break;
                case "additive-greedy": Call("SearchAdditivePatterns",limit,true,false);break;
                case "additive-mobile": Call("SearchAdditivePatterns",limit,true,true);break;
                case "triple": Call("SearchPairPatterns",limit,true);break;
                case "four": Call("SearchPairPatterns",limit,false);break;
                case "dependency": Call("SearchRouteDependencies",false);break;
                case "target": Call("SearchTargetBeams");break;
                case "key": type.GetField("prioritizeRouteKeys",flags)!.SetValue(engine,true);Call("SearchKeyBeams");break;
                case "key-wide": type.GetField("prioritizeRouteKeys",flags)!.SetValue(engine,true);Call("SearchRouteBeam",false,128,2048,-1,-1,false);break;
                case "beam": Call("SearchRouteBeam",true,16,limit,-1,-1,true);break;
                case "audit":
                case "audit-triple":
                case "audit-four":
                case "audit-five":
                case "audit-six":
                    var bodies=((System.Collections.IEnumerable)Call("PatternBodies")).Cast<object>().ToArray();
                    object BodyField(object body,string name)=>body.GetType().GetField(name,flags)!.GetValue(body)!;
                    var state=Field("current");
                    int Position(int hole)=>(int)state.GetType().GetMethod("GetValue",flags)!.Invoke(state,new object[]{hole})!;
                    int count=(int)Field("count");
                    foreach(var target in bodies)
                    {
                        int rootHole=(int)BodyField(target,"root");
                        var goals=(int[])BodyField(target,"goals");
                        if(goals.Length==0)continue;
                        var targetMasks=(Product.CellMask[])BodyField(target,"masks");
                        long staticCost=(long)Call("OccupancyPathDistance",rootHole,Position(rootHole),(Product.CellMask)Field("routeFixedMask"),-1);
                        var occupied=(Product.CellMask)Call("Occupancy",state);
                        long currentCost=(long)Call("OccupancyPathDistance",rootHole,Position(rootHole),occupied & ~targetMasks[Position(rootHole)],-1);
                        var rows=new List<object>();
                        bool pairBlocked=false;
                        foreach(var other in bodies)
                        {
                            if(ReferenceEquals(target,other))continue;
                            int otherHole=(int)BodyField(other,"root");
                            int[] distances=(int[])Call("PairPattern",target,other);
                            int distance=distances[Position(rootHole)*count+Position(otherHole)];
                            pairBlocked|=distance==int.MaxValue;
                            rows.Add(new{other=otherHole,unreachable=distance==int.MaxValue,distance});
                        }
                        var triples=new List<object>();
                        object four=null;
                        if((method=="audit-four"||method=="audit-five"||method=="audit-six")&&!pairBlocked&&bodies.Length>=5)
                        {
                            var relevant=(HashSet<int>)Call("RouteDependencyGroups",rootHole,state,-1,null);
                            var partners=bodies.Where(b=>!ReferenceEquals(b,target)).ToList();
                            partners.Sort((a,b)=>{
                                int ar=(int)BodyField(a,"root"),br=(int)BodyField(b,"root");
                                int order=relevant.Contains(br).CompareTo(relevant.Contains(ar));
                                if(order!=0)return order;
                                int origin=Position(rootHole),ap=Position(ar),bp=Position(br);
                                int width=input.width;
                                int ad=Math.Abs(ap%width-origin%width)+Math.Abs(ap/width-origin/width);
                                int bd=Math.Abs(bp%width-origin%width)+Math.Abs(bp/width-origin/width);
                                return ad!=bd?ad.CompareTo(bd):ar.CompareTo(br);
                            });
                            int modelSize=method=="audit-six"?6:method=="audit-five"?5:4;
                            var selected=Array.CreateInstance(target.GetType(),modelSize);
                            selected.SetValue(target,0);
                            for(int member=1;member<modelSize;member++)selected.SetValue(partners[member-1],member);
                            var table=bodies.Length>modelSize?Call("JointPattern",selected,25000):null;
                            if(table!=null)four=new{
                                roots=BodyField(table,"roots"),domains=((int[][])BodyField(table,"anchors")).Select(a=>a.Length).ToArray(),
                                complete=BodyField(table,"complete"),
                                initialDistance=table.GetType().GetMethod("Distance",flags)!.Invoke(table,new[]{state})
                            };
                        }
                        if(method=="audit-triple"&&!pairBlocked&&bodies.Length>=4&&count<=81)
                        {
                            var others=bodies.Where(b=>!ReferenceEquals(b,target)).ToArray();
                            bool blocked=false;
                            for(int first=0;first<others.Length&&!blocked;first++)
                                for(int second=first+1;second<others.Length;second++)
                                {
                                    int a=(int)BodyField(others[first],"root"),b=(int)BodyField(others[second],"root");
                                    var distances=(int[])Call("TriplePattern",target,others[first],others[second]);
                                    int distance=distances[Position(rootHole)*count*count+Position(a)*count+Position(b)];
                                    triples.Add(new{first=a,second=b,unreachable=distance==0,distance});
                                    if(distance==0){blocked=true;break;}
                                }
                        }
                        auditRows.Add(new{target=rootHole,goals=goals.Length,staticCost,currentCost,
                            validStart=((bool[])BodyField(target,"valid"))[Position(rootHole)],pairs=rows,triples,four});
                    }
                    break;
                default: throw new ArgumentException("Unknown phase method");
            }
            var best=Field("best");
            found=best!=null;
            if(found)Call("AppendPlan",best);
        }
        catch(TargetInvocationException e) when(e.InnerException is OperationCanceledException){cancelled=true;}
        clock.Stop();
        if(found&&!cancelled)
        {
            var referenceInput=JsonConvert.DeserializeObject<Reference.BoardInput>(json)!;
            var moves=JsonConvert.DeserializeObject<List<Reference.SolverMove>>(JsonConvert.SerializeObject(result.moves))!;
            var replay=Reference.CatLevelSolver.VerifyPlan(referenceInput,moves,0,true);
            var expected=JToken.FromObject(Call("Snapshot"));
            replayed=replay.status!=Reference.SolveStatus.InvalidInput&&replay.status!=Reference.SolveStatus.TimedOut
                &&JToken.DeepEquals(expected,JToken.FromObject(replay.finalState));
            if(!replayed)throw new Exception("Phase replay/snapshot mismatch");
        }
        string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        string directory=Path.Combine(root,"docs/research/phase-search");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,method+"."+fixtureName),JsonConvert.SerializeObject(new{
            measurementKind="Single diagnostic phase; capped search; not a full-level solve or acceptance",
            fixture=fixtureName,fixtureHash=Hash(path),assemblyHash=Hash(typeof(Product.CatLevelSolver).Assembly.Location),
            method,limit,cancelled,found=found&&!cancelled,replayed,result.expanded,result.abstractExpanded,
            phaseMs=clock.Elapsed.TotalMilliseconds,drags=result.moves.Count,steps=result.moves.Sum(m=>m.path.Count-1),trace,auditRows,moves=result.moves
        },Formatting.Indented));
        Console.WriteLine($"{fixtureName} {method} found={found&&!cancelled} cancelled={cancelled} replay={replayed} expanded={result.expanded} ms={clock.Elapsed.TotalMilliseconds:F1}");
    }
}
