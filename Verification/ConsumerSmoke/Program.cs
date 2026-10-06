using CatDom.CoreSolver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
var result=CatLevelSolver.SolveLevel(File.ReadAllText(args[0]));
if(result.status!=SolverStatus.Solved||!double.IsFinite(result.solveTimeMs)||result.solveTimeMs<0)throw new Exception("Consumer solve failed");
var output=JObject.Parse(JsonConvert.SerializeObject(result));
var names=output.Properties().Select(p=>p.Name).ToHashSet();
if(!names.SetEquals(new[]{"algorithmVersion","status","message","expanded","solveTimeMs","moves"}))throw new Exception("Unexpected public output");
if(output["algorithmVersion"].Value<string>()!="1.0.0"||output["status"].Value<string>()!="Solved")throw new Exception("Version/status contract failed");
foreach(var move in output["moves"]){var fields=((JObject)move).Properties().Select(p=>p.Name).ToHashSet();if(!fields.SetEquals(new[]{"holeId","start","path","eaten"}))throw new Exception("Unexpected move fields");}
Console.WriteLine("PASS external consumer and six-field public output contract");
