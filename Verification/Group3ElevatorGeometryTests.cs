using System.Reflection;
using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class Group3ElevatorGeometryTests
{
    internal static void Run(string root)
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        string catalogJson=File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json"));
        var catalog=Product.IO.SolverJson.ReadCatalog(catalogJson);
        const string json="""
        {"sizeX":6,"sizeY":6,"elevatorInfos":[{"position":{"x":2,"y":3},"levelInfo":{"sizeX":2,"sizeY":2,
        "holeInfos":[{"holeType":10,"position":{"x":0,"y":0},"colorIds":[1],"numOfCats":[1]},
        {"holeType":10,"position":{"x":1,"y":0},"colorIds":[2],"numOfCats":[1]}],
        "catInfos":[{"position":{"x":0,"y":1},"colorId":1}],"linkInfos":[{"idHole1":0,"idHole2":1}],
        "plugs":[{"position":{"x":1,"y":1},"colorId":2}]}}]}
        """;
        var input=Product.IO.SolverJson.ReadLevel(json,catalog);
        if(input.elevators.Count!=1)throw new Exception("Elevator parser lost child data.");
        var elevator=input.elevators[0];var clone=input.Copy();clone.elevators[0].child.holes[0].position=new(99,99);
        if(elevator.child.holes[0].position.x!=0)throw new Exception("Elevator child copy aliases source.");
        var assembly=typeof(Product.CatLevelSolver).Assembly;
        var geometry=assembly.GetType("CatDom.CoreSolver.ElevatorGeometry");
        var world=(Product.BoardInput)geometry.GetMethod("WorldChild",flags).Invoke(null,new object[]{elevator,7,11,13,17,19});
        if(world.holes[0].id!=7 || !world.holes[0].position.Equals(new Product.Cell(2,3)) ||
            world.links[0].holeId1!=7 || world.links[0].holeId2!=8 || world.cats[0].id!=11 ||
            !world.cats[0].position.Equals(new Product.Cell(2,4)) || world.plugs[0].id!=17)
            throw new Exception("Elevator world position or runtime ID mapping failed.");
        if(!world.holes[0].footprint[0].Equals(elevator.child.holes[0].footprint[0]))throw new Exception("Elevator changed local hole geometry.");
        var candidates=assembly.GetType("CatDom.CoreSolver.ElevatorCandidates");
        object plan=candidates.GetMethod("Compile",flags).Invoke(null,new object[]{elevator,7,11,13,17,19,6,6});
        var pf=BindingFlags.Instance|BindingFlags.NonPublic;
        var list=(System.Collections.IList)plan.GetType().GetField("candidates",pf).GetValue(plan);
        string[] kinds=list.Cast<object>().Select(c=>c.GetType().GetField("kind",pf).GetValue(c).ToString()).ToArray();
        if(!kinds.SequenceEqual(new[]{"Cat","Hole","Hole","Link","Plug"}))throw new Exception("Elevator runtime spawn order differs.");
        var runtimePlan=Product.ElevatorCandidates.Compile(elevator,7,11,13,17,19,6,6);
        int spawned=0;
        bool Spawn(Product.ElevatorCandidate c){spawned++;return true;}
        if(Product.ElevatorActivation.Refresh(runtimePlan,_=>true,_=>false,_=>false,Spawn) || spawned!=0)
            throw new Exception("Elevator spawned while area occupied.");
        if(Product.ElevatorActivation.Refresh(runtimePlan,_=>false,_=>false,_=>true,Spawn) || spawned!=0)
            throw new Exception("Elevator spawned through actively dragged area.");
        var saved=Product.ElevatorActivation.Save(runtimePlan);
        if(!Product.ElevatorActivation.PendingPlayableHole(runtimePlan))throw new Exception("Elevator pending hole was not a win requirement.");
        if(!Product.ElevatorActivation.Refresh(runtimePlan,_=>false,_=>false,_=>false,Spawn) || spawned!=5 || Product.ElevatorActivation.PendingPlayableHole(runtimePlan))
            throw new Exception("Elevator did not activate complete unobstructed child.");
        Product.ElevatorActivation.Restore(runtimePlan,saved);
        if(runtimePlan.candidates.Any(c=>c.state!=0))throw new Exception("Elevator rollback did not restore pending candidates.");
        int calls=0;
        Product.ElevatorActivation.Refresh(runtimePlan,_=>false,_=>false,_=>false,c=>{calls++;return c.kind!=Product.ElevatorObjectKind.Link;});
        if(runtimePlan.candidates.Single(c=>c.kind==Product.ElevatorObjectKind.Link).state!=0)
            throw new Exception("Elevator reference must remain pending when runtime targets are not ready.");
        Console.WriteLine("PASS elevator parser/deepcopy, world offsets, IDs/local geometry and candidate order");
        string source=File.ReadAllText(Path.Combine(root,"Levels_Group2_Assessment_2026_10_12_Run2/Group3_Special/CURRENT_00021.json"));
        var playable=Product.IO.SolverJson.ReadLevel(source,catalog);
        if(playable.ignoredMechanics.Count!=0)throw new Exception("Elevator source fixture unexpectedly unsupported.");
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var solved=Product.CatLevelSolver.SolveCore(playable,cancellation.Token);
        if(solved.status!=Product.SolveStatus.Solved)throw new Exception("Elevator activation Core fixture failed: "+solved.status+" / "+solved.message);
        var api=Product.CatLevelSolver.SolveLevelWithProgress(source,cancellation.Token,_=>{});
        if(api.status!=Product.SolverStatus.Solved)throw new Exception("Elevator API fixture failed: "+api.status);
        V3IndependentReplay.Verify(source,catalogJson,JArray.FromObject(api.moves));
        bool rejected=false;
        try{V3IndependentReplay.Verify(source,catalogJson,new JArray());}catch(Exception){rejected=true;}
        if(!rejected)throw new Exception("Independent elevator replay accepted unspawned playable targets.");
        string measured=Path.Combine(root,"Verification/results/elevator-all43-v1/CURRENT_00970.json");
        if(File.Exists(measured))
        {
            var measuredPlan=JObject.Parse(File.ReadAllText(measured));
            if((string)measuredPlan["status"]!="Solved")throw new Exception("Expected solved elevator child-hole fixture plan.");
            V3IndependentReplay.Verify(File.ReadAllText(Path.Combine(root,"Levels_Group2_Assessment_2026_10_12_Run2/Group3_Special/CURRENT_00970.json")),catalogJson,(JArray)measuredPlan["moves"]);
        }
        Console.WriteLine("PASS elevator current source fixture with internal state replay");
        const string futureCapacity="""
        {"sizeX":3,"sizeY":2,"holeInfos":[{"position":{"x":1,"y":1},"holeType":10,"colorIds":[1],"numOfCats":[2]}],
        "catInfos":[{"position":{"x":1,"y":0},"colorId":1},{"position":{"x":0,"y":1},"colorId":1},{"position":{"x":2,"y":0},"colorId":2}],
        "elevatorInfos":[{"position":{"x":0,"y":0},"levelInfo":{"sizeX":2,"sizeY":2,"holeInfos":[{"position":{"x":1,"y":1},"holeType":10,"colorIds":[2],"numOfCats":[1]}]}}]}
        """;
        var engineType=assembly.GetType("CatDom.CoreSolver.CatLevelSolver+Engine");
        var engineResult=new Product.EngineResult();
        var engine=Activator.CreateInstance(engineType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            new object[]{Product.IO.SolverJson.ReadLevel(futureCapacity,catalog),new Product.EngineContext(),CancellationToken.None,engineResult},null);
        if(!(bool)engineType.GetMethod("TryDecisionPlan",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(engine,new object[]{0}))
            throw new Exception("Event planner rejected a valid event requiring future elevator capacity.");
        V3IndependentReplay.Verify(futureCapacity,catalogJson,JArray.FromObject(engineResult.moves));
        Console.WriteLine("PASS event planning retains future elevator capacity and independent replay");
    }
}

