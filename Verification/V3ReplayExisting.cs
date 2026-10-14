using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class V3ReplayExisting
{
    internal static void Run(string root,string corpusPath=null,string inputPath=null,string outputPath=null)
    {
        string corpus=corpusPath==null?Path.Combine(root,"Levels_V3_NewMechanics"):Path.GetFullPath(corpusPath);
        string input=inputPath==null?Path.Combine(root,"Verification/results/v3-screen"):Path.GetFullPath(inputPath);
        string output=outputPath==null?Path.Combine(root,"Verification/results/v3-independent"):Path.GetFullPath(outputPath);Directory.CreateDirectory(output);
        string productHash=Hash(typeof(Product.CatLevelSolver).Assembly.Location);
        string verifierHash=Hash(typeof(V3ReplayExisting).Assembly.Location);
        string catalogPath=Path.Combine(root,"IndependentSolver/IO/HoleShapes.json");
        string catalog=File.ReadAllText(catalogPath),catalogHash=Hash(catalogPath);
        var assembly=typeof(Product.CatLevelSolver).Assembly;
        string resource=assembly.GetManifestResourceNames().Single(n=>n.EndsWith("HoleShapes.json",StringComparison.Ordinal));
        using(var reader=new StreamReader(assembly.GetManifestResourceStream(resource)))
            if(!JToken.DeepEquals(JToken.Parse(reader.ReadToEnd()),JToken.Parse(catalog)))throw new Exception("Verifier catalog differs from product embedded shapes.");
        var rows=new List<JObject>();
        foreach(string file in Directory.GetFiles(corpus,"*.json").OrderBy(p=>p))
        {
            string planPath=Path.Combine(input,Path.GetFileName(file));
            if(!File.Exists(planPath))throw new Exception("Missing measured result: "+planPath);
            JObject solved;
            try{solved=JObject.Parse(File.ReadAllText(planPath));}
            catch(IOException){continue;}
            catch(JsonReaderException){continue;} // A live writer may be between truncate and flush.
            if((string)solved["status"]!="Solved")continue;
            string level=Path.GetFileNameWithoutExtension(file),levelPath=file;
            string levelHash=Hash(levelPath);
            if((string)solved["level"]!=level || (string)solved["assemblyHash"]!=productHash || (string)solved["levelHash"]!=levelHash)
                throw new Exception("Replay provenance mismatch: "+level);
            string destination=Path.Combine(output,level+".json");
            JObject evidence=null;
            if(File.Exists(destination))
            {
                var cached=JObject.Parse(File.ReadAllText(destination));
                if((string)cached["productHash"]==productHash && (string)cached["levelHash"]==levelHash &&
                    (string)cached["verifierHash"]==verifierHash && (string)cached["catalogHash"]==catalogHash &&
                    JToken.DeepEquals(cached["moves"],solved["moves"]))evidence=cached;
            }
            if(evidence==null)
            {
                string verdict="Verified",error=null;
                try{V3IndependentReplay.Verify(File.ReadAllText(levelPath),catalog,(JArray)solved["moves"]);}
                catch(NotSupportedException ex){verdict="MissingRule";error=ex.Message;}
                catch(Exception ex){verdict="ReplayRejected";error=ex.Message;}
                evidence=JObject.FromObject(new{level,levelHash,productHash,verifierHash,catalogHash,verdict,error,
                    independentReplayVerified=verdict=="Verified",moves=solved["moves"]});
                File.WriteAllText(destination,evidence.ToString(Formatting.Indented));
            }
            rows.Add(evidence);
        }
        var summary=new{method="Replay existing Solved plans only when product/input hashes match; separate verifier provenance and strict missing-rule rejection. Does not solve or establish 90% corpus coverage.",
            productHash,verifierHash,catalogHash,count=rows.Count,
            verdicts=rows.GroupBy(r=>(string)r["verdict"]).ToDictionary(g=>g.Key,g=>g.Count())};
        File.WriteAllText(Path.Combine(output,"summary.json"),JsonConvert.SerializeObject(summary,Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(new{summary.count,summary.verdicts}));
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
