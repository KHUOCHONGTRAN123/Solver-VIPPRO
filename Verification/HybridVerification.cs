using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;
using Reference = CatDom.Reference.V42;

internal static class HybridVerification
{
    internal static void Run(string root)
    {
        var catalog=Reference.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var rows=new List<object>();
        string dir=Path.Combine(root,"docs/benchmarks/v2");Directory.CreateDirectory(dir);
        Product.CatLevelSolver.SolveLevel(File.ReadAllText(Path.Combine(root,"Levels/Level00001.json")));
        foreach(string file in Directory.GetFiles(Path.Combine(root,"Levels"),"*.json").OrderBy(f=>f))
        {
            string level=Path.GetFileNameWithoutExtension(file),json=File.ReadAllText(file);
            using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result=Product.CatLevelSolver.SolveLevel(json,cancellation.Token);
            if(result.algorithmVersion!="2.0.0"||result.expanded!=result.baselineExpanded+result.improvedExpanded||result.baselineExpanded>10000)
                throw new Exception("Hybrid accounting failed "+level);
            var baseline=JObject.Parse(File.ReadAllText(Path.Combine(root,"Verification/V42Results",Path.GetFileName(file))));
            bool preserved=false,replayed=false;
            if((long)baseline["expanded"]<=10000)
            {
                var expectedMoves=new JArray(((JArray)baseline["moves"]!).Select(m=>new JObject {
                    ["holeId"]=m["holeId"],["start"]=m["start"],["path"]=m["path"],["eaten"]=m["eaten"] }));
                preserved=result.status==Product.SolverStatus.Solved&&result.searchAlgorithm=="baseline"&&result.expanded==(long)baseline["expanded"]&&
                    JToken.DeepEquals(JToken.FromObject(result.moves),expectedMoves);
                if(!preserved)throw new Exception("Baseline preservation failed "+level);
            }
            if(result.status==Product.SolverStatus.Solved)
            {
                var moves=JsonConvert.DeserializeObject<List<Reference.SolverMove>>(JsonConvert.SerializeObject(result.moves))!;
                var replay=Reference.CatLevelSolver.VerifyPlan(Reference.IO.SolverJson.ReadLevel(json,catalog),moves,0,true);
                replayed=replay.status==Reference.SolveStatus.Solved&&replay.finalState.remainingCatIds.Count==0;
                if(!replayed)throw new Exception("Hybrid replay failed "+level);
            }
            var row=new {level,status=result.status.ToString(),result.searchAlgorithm,result.expanded,result.baselineExpanded,result.improvedExpanded,result.solveTimeMs,moves=result.moves.Count,steps=result.moves.Sum(m=>m.path.Count-1),preserved,replayed};
            rows.Add(row);
            File.WriteAllText(Path.Combine(dir,level+".json"),JsonConvert.SerializeObject(result,Formatting.Indented));
            File.WriteAllText(Path.Combine(dir,"hybrid-screen.json"),JsonConvert.SerializeObject(new {method="Release; single screening; 10s diagnostic cancellation; preservation compared to frozen v42 results; independent replay",count=rows.Count,assemblyHash=Hash(typeof(Product.CatLevelSolver).Assembly.Location),rows},Formatting.Indented));
            Console.WriteLine($"{rows.Count}/299 {level} {result.status} {result.searchAlgorithm} expanded={result.expanded}");
        }
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
