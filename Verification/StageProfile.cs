using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;

internal static class StageProfile
{
    internal static void Run(string root)
    {
        var rows=new List<object>();
        var shapeJson=File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json"));
        var levelJson=File.ReadAllText(Path.Combine(root,"Levels/Level00206.json"));
        var fixtureJson=File.ReadAllText(Path.Combine(root,"Levels/Level00001.json"));
        var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        foreach(bool optimized in new[]{false,true})
        {
            object catalog=optimized?(object)Product.IO.SolverJson.ReadCatalog(shapeJson):Reference.IO.SolverJson.ReadCatalog(shapeJson);
            object context=optimized?(object)new Product.EngineContext():Reference.SolverConfig.CreateRouteClearingDefault();
            Type solver=optimized?typeof(Product.CatLevelSolver):typeof(Reference.CatLevelSolver);
            object input=Measure("parse",()=>optimized?(object)Product.IO.SolverJson.ReadLevel(levelJson,(Product.IO.ShapeCatalog)catalog):Reference.IO.SolverJson.ReadLevel(levelJson,(Reference.IO.ShapeCatalog)catalog));
            Measure("validation",()=>solver.GetMethod("Validate",flags).Invoke(null,new[]{input,context}));
            var engineType=solver.GetNestedType("Engine",BindingFlags.NonPublic);
            object engine=Measure("precompute",()=>optimized
                ?Activator.CreateInstance(engineType,flags,null,new[]{input,context,(object)CancellationToken.None,new Product.EngineResult()},null)
                :Activator.CreateInstance(engineType,flags,null,new[]{input,context,(object)CancellationToken.None,Stopwatch.StartNew(),new Reference.SolveResult()},null));
            engineType.GetField("stats",flags).SetValue(engine,optimized?(object)new Product.SubproblemStats():new Reference.SubproblemStats());
            Measure("prepare",()=>engineType.GetMethod("Prepare",flags).Invoke(engine,null));
            var state=engineType.GetField("current",flags).GetValue(engine);
            var generated=(System.Collections.IEnumerable)Measure("generate",()=>engineType.GetMethod("Generate",flags).Invoke(engine,new[]{state}));
            var moves=generated.Cast<object>().ToArray();
            Measure("rank_candidates",()=>{foreach(var move in moves){var candidateState=move.GetType().GetField("state",flags).GetValue(move);engineType.GetMethod("Score",flags).Invoke(engine,new object[]{candidateState,true});}return (object)moves.Length;});
            Measure("continuation_guards",()=>{int count=0;foreach(var move in moves){if(!(bool)move.GetType().GetField("terminal",flags).GetValue(move))continue;engineType.GetMethod("HasRouteContinuation",flags).Invoke(engine,new[]{move});count++;}return (object)count;});
            var result=Reference.CatLevelSolver.Solve(Reference.IO.SolverJson.ReadLevel(fixtureJson,(optimized?Reference.IO.SolverJson.ReadCatalog(shapeJson):(Reference.IO.ShapeCatalog)catalog)),Reference.SolverConfig.CreateRouteClearingDefault());
            Measure("replay_fixture",()=>optimized
                ?(object)Product.CatLevelSolver.VerifyPlan(Product.IO.SolverJson.ReadLevel(fixtureJson,(Product.IO.ShapeCatalog)catalog),JsonConvert.DeserializeObject<List<Product.EngineMove>>(JsonConvert.SerializeObject(result.moves)))
                :Reference.CatLevelSolver.VerifyPlan(Reference.IO.SolverJson.ReadLevel(fixtureJson,(Reference.IO.ShapeCatalog)catalog),result.moves,0));
            object Measure(string stage,Func<object> action)
            {
                long allocated=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();var value=action();timer.Stop();
                rows.Add(new{mode=optimized?"optimized":"reference",stage,timeMs=timer.Elapsed.TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated});return value;
            }
        }
        var output=Path.Combine(root,"docs/benchmarks/stage-profile.json");Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output,JsonConvert.SerializeObject(new{method="Representative level206 initial state; reflection overhead included; allocation hotspot attribution, not acceptance timing. Replay uses level1.",rows},Formatting.Indented));
        Console.WriteLine("Stage profile written");
    }
}
