using System.Reflection;
using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Product = CatDom.CoreSolver;

// Research export only. A SAT witness is not a solved level or acceptance result.
internal static class ConstraintProbe
{
    internal static void Run(string root, string level, bool hardest = false)
    {
        string[] scope = { "269","206","233","117","246","267","274","101","289","239","259","268","100","213","184","149","176","219","186","225","216" };
        if (!scope.Contains(int.Parse(level).ToString())) throw new ArgumentException("Outside research scope");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var catalog = Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        object input = Product.IO.SolverJson.ReadLevel(File.ReadAllText(Path.Combine(root,$"Levels/Level{int.Parse(level):00000}.json")),catalog);
        if(hardest)
        {
            var saved=JObject.Parse(File.ReadAllText(Path.Combine(root,$"Verification/V42Results/Level{int.Parse(level):00000}.json")));
            var phase=saved["subproblems"]!.OrderByDescending(p=>(long)p["expanded"]!).First();
            input=JsonConvert.DeserializeObject(phase["input"]!.ToString(),input.GetType())!;
        }
        var type = typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic)!;
        object engine = Activator.CreateInstance(type,flags,null,new object[]{input,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null)!;
        object Field(object obj,string name) => obj.GetType().GetField(name,flags)!.GetValue(obj)!;
        object Call(string name,params object[] arguments) => type.GetMethod(name,flags)!.Invoke(engine,arguments)!;
        Call("Consume",-1);
        type.GetField("stats",flags)!.SetValue(engine,new Product.SubproblemStats());
        Call("Prepare");
        var state=Field(engine,"current");
        int Position(int hole)=>(int)state.GetType().GetMethod("GetValue",flags)!.Invoke(state,new object[]{hole})!;
        var bodies=((IEnumerable)Call("PatternBodies")).Cast<object>().ToArray();
        var neighbors=(int[][])Field(engine,"neighbors");
        var groups=(int[][])Field(engine,"linkedGroups");
        int count=(int)Field(engine,"count");
        object Cell(int p)=>Call("CellAt",p);
        int Coordinate(object cell,string name)=>(int)Field(cell,name);
        var rows=new List<object>();
        foreach(var body in bodies)
        {
            int hole=(int)Field(body,"root"), origin=Position(hole);
            var valid=(bool[])Field(body,"valid");
            var masks=(Array)Field(body,"masks");
            var edges=new List<int[]>();
            for(int p=0;p<count;p++) if(valid[p]) foreach(int q in neighbors[p]) if(valid[q])
            {
                var oldCell=Cell(p); var newCell=Cell(q);
                int dx=Coordinate(newCell,"x")-Coordinate(oldCell,"x"), dy=Coordinate(newCell,"y")-Coordinate(oldCell,"y");
                bool legal=true;
                foreach(int member in groups[hole])
                {
                    int start=Position(member); if(start<0) continue;
                    var original=Cell(origin); var memberCell=Cell(start);
                    int x=Coordinate(memberCell,"x")+Coordinate(oldCell,"x")-Coordinate(original,"x");
                    int y=Coordinate(memberCell,"y")+Coordinate(oldCell,"y")-Coordinate(original,"y");
                    int a=-1,b=-1;
                    for(int c=0;c<count;c++){var cell=Cell(c);if(Coordinate(cell,"x")==x&&Coordinate(cell,"y")==y)a=c;if(Coordinate(cell,"x")==x+dx&&Coordinate(cell,"y")==y+dy)b=c;}
                    int direction=(int)Call("Direction",oldCell,newCell);
                    if(a<0||b<0||!(bool)Call("CanStep",member,a,b)||!(bool)Call("CanEnterCat",member,b,direction)){legal=false;break;}
                }
                if(legal)edges.Add(new[]{p,q});
            }
            string Mask(int p){var mask=masks.GetValue(p)!;return mask.GetType().GetMethod("ToBigInteger",flags)!.Invoke(mask,null)!.ToString()!;}
            rows.Add(new{hole,origin,goals=(int[])Field(body,"goals"),valid, masks=Enumerable.Range(0,count).Select(Mask).ToArray(),edges});
        }
        string output=Path.Combine(root,$"docs/research/constraint-probe/Level{int.Parse(level):00000}{(hardest?".hardest":"")}.geometry.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output,JsonConvert.SerializeObject(new{level,count,bodies=rows,limitation="Initial phase geometry only; consumption continuation and full independent replay remain required."},Formatting.Indented));
        Console.WriteLine(output);
    }
}
