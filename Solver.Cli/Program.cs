using CatDom.CoreSolver;
using Newtonsoft.Json;

if(args.Contains("--help")||args.Contains("-h"))
{
    Console.WriteLine("cat-solver [level.json|-] [--output result.json]\nWithout a file, reads one level JSON from stdin. No algorithm settings.");return 0;
}
string input=null,output=null;
for(int i=0;i<args.Length;i++)
{
    if(args[i]=="--output"&&i+1<args.Length){output=args[++i];continue;}
    if(input==null&&(!args[i].StartsWith("-")||args[i]=="-")){input=args[i];continue;}
    Console.Error.WriteLine("Usage: cat-solver [level.json|-] [--output result.json]");return 2;
}
using var cancellation=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
try
{
    string level=input==null||input=="-"?Console.In.ReadToEnd():File.ReadAllText(input);
    var result=CatLevelSolver.SolveLevel(level,cancellation.Token);
    string json=JsonConvert.SerializeObject(result,Formatting.Indented);
    if(output==null)Console.WriteLine(json);else File.WriteAllText(output,json);
    return result.status switch {SolverStatus.Solved=>0,SolverStatus.Cancelled=>130,SolverStatus.NoNextCatReachable=>1,_=>2};
}
catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException)
{Console.Error.WriteLine(ex.Message);return 2;}
