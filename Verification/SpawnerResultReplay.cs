using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

internal static class SpawnerResultReplay
{
    internal static void Run(string root,string directory,string corpus=null)
    {
        var run=JObject.Parse(File.ReadAllText(Path.Combine(directory,"run-info.json")));
        string expectedHash=(string)run["solverDllSha256"];
        string productHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(CatDom.CoreSolver.CatLevelSolver).Assembly.Location)));
        if(productHash!=expectedHash)throw new Exception("Replay product differs from measured solver DLL.");
        string catalog=File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json"));
        var rows=new JArray();
        foreach(string file in Directory.GetFiles(directory,"Level*.json").OrderBy(f=>f))
        {
            var result=JObject.Parse(File.ReadAllText(file));
            if((string)result["status"]!="Solved")continue;
            string level=Path.GetFileNameWithoutExtension(file);
            string input=Path.Combine(corpus??Path.Combine(root,"Levels"),level+".json");
            string verdict="Verified",error=null;
            try{V3IndependentReplay.Verify(File.ReadAllText(input),catalog,(JArray)result["moves"]);}
            catch(Exception ex){verdict="Rejected";error=ex.Message;}
            rows.Add(new JObject{{"level",level},{"status",verdict},{"error",error},
                {"productHash",productHash},
                {"levelHash",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)))},
                {"resultHash",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))}});
        }
        File.WriteAllText(Path.Combine(directory,"independent-replay.json"),rows.ToString());
        Console.WriteLine($"Independent replay: {rows.Count(r=>(string)r["status"]=="Verified")}/{rows.Count} verified");
        if(rows.Count==0 || rows.Any(r=>(string)r["status"]!="Verified"))throw new Exception("Spawner replay acceptance failed.");
    }
}
