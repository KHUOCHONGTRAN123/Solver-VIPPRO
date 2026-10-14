using Newtonsoft.Json.Linq;
using Product=CatDom.CoreSolver;

internal static class V3IndependentReplayTests
{
    internal static void Run(string root)
    {
        string catalog=File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json"));
        const string basic="""
        {"sizeX":3,"sizeY":1,"holeInfos":[{"position":{"x":0,"y":0},"holeType":10,"colorIds":[0],"numOfCats":[1]}],"catInfos":[{"position":{"x":2,"y":0},"colorId":0}]}
        """;
        const string plug="""
        {"sizeX":4,"sizeY":1,"holeInfos":[{"position":{"x":0,"y":0},"holeType":10,"colorIds":[0],"numOfCats":[1],"isPlug":true,"plugColorId":2}],"catInfos":[{"position":{"x":3,"y":0},"colorId":0,"bonusTime":5,"timeLimited":5}],"plugs":[{"position":{"x":1,"y":0},"colorId":2}]}
        """;
        const string expand="""
        {"sizeX":2,"sizeY":2,"holeInfos":[{"position":{"x":0,"y":0},"holeType":8,"colorIds":[0],"numOfCats":[1],"posExpand":{"x":0,"y":1}}],"catInfos":[{"position":{"x":0,"y":0},"colorId":0}]}
        """;
        const string box="""
        {"sizeX":3,"sizeY":1,"holeInfos":[{"position":{"x":0,"y":0},"holeType":10,"colorIds":[0],"numOfCats":[1]}],"catBoxInfos":[{"position":{"x":2,"y":0},"direction":1,"colorIds":[{"colorId":0,"position":{"x":0,"y":0},"bonusTime":2,"timeLimited":2}]}]}
        """;
        const string tower="""
        {"sizeX":3,"sizeY":1,"holeInfos":[{"position":{"x":0,"y":0},"holeType":10,"colorIds":[0],"numOfCats":[1]}],"towerInfos":[{"position":{"x":2,"y":0},"colorIds":[{"colorId":0,"position":{"x":0,"y":0},"bonusTime":3,"timeLimited":3}]}]}
        """;
        const string curtain="""
        {"sizeX":3,"sizeY":2,"holeInfos":[
        {"position":{"x":0,"y":0},"holeType":10,"colorIds":[1],"numOfCats":[1],"isOpenClose":true},
        {"position":{"x":0,"y":1},"holeType":10,"colorIds":[2],"numOfCats":[1]}],
        "catInfos":[{"position":{"x":2,"y":0},"colorId":1},{"position":{"x":1,"y":1},"colorId":2}]}
        """;
        foreach(string json in new[]{basic,plug,expand,box,tower,curtain})
        {
            var solved=Product.CatLevelSolver.SolveLevel(json);
            if(solved.status!=Product.SolverStatus.Solved)throw new Exception("Independent fixture failed product solve: "+solved.status+" / "+solved.message+" / "+json);
            V3IndependentReplay.Verify(json,catalog,JArray.FromObject(solved.moves));
        }
        var plan=JArray.FromObject(Product.CatLevelSolver.SolveLevel(basic).moves);
        var broken=(JArray)plan.DeepClone();
        broken[0]["path"]=new JArray(JObject.FromObject(new{x=0,y=0}),JObject.FromObject(new{x=2,y=0}));
        Reject(()=>V3IndependentReplay.Verify(basic,catalog,broken));
        var wrong=JObject.Parse(plug);wrong["plugs"][0]["colorId"]=3;
        Reject(()=>V3IndependentReplay.Verify(wrong.ToString(),catalog,JArray.FromObject(Product.CatLevelSolver.SolveLevel(plug).moves)));
        var invalidCurtainPlan=new JArray(new JObject{["holeId"]=0,["start"]=new JObject{["x"]=0,["y"]=0},
            ["path"]=new JArray(new JObject{["x"]=0,["y"]=0},new JObject{["x"]=1,["y"]=0},new JObject{["x"]=2,["y"]=0})});
        Reject(()=>V3IndependentReplay.Verify(curtain,catalog,invalidCurtainPlan));
        const string rune="""
        {"sizeX":3,"sizeY":2,"holeInfos":[
        {"position":{"x":0,"y":0},"holeType":10,"colorIds":[2],"numOfCats":[1],"runesealedUnlockHoleIndex":1},
        {"position":{"x":1,"y":0},"holeType":10,"colorIds":[1],"numOfCats":[1]},
        {"position":{"x":2,"y":0},"holeType":10,"colorIds":[101],"numOfCats":[1]}],
        "catInfos":[{"position":{"x":0,"y":1},"colorId":2},{"position":{"x":1,"y":1},"colorId":1},{"position":{"x":2,"y":1},"colorId":101}]}
        """;
        JArray runePlan=new(new[]{2,0,1}.Select(i=>new JObject{["holeId"]=i,["start"]=new JObject{["x"]=i,["y"]=0},
            ["path"]=new JArray(new JObject{["x"]=i,["y"]=0},new JObject{["x"]=i,["y"]=1})}));
        V3IndependentReplay.Verify(rune,catalog,runePlan);
        Reject(()=>V3IndependentReplay.Verify(rune,catalog,new JArray(runePlan[1].DeepClone(),runePlan[0].DeepClone(),runePlan[2].DeepClone())));
        var wrongSignature=JObject.Parse(rune);wrongSignature["holeInfos"][2]["colorIds"][0]=103;wrongSignature["catInfos"][2]["colorId"]=103;
        Reject(()=>V3IndependentReplay.Verify(wrongSignature.ToString(),catalog,runePlan));
        var zeroRune=JObject.Parse(rune);zeroRune["holeInfos"][0]["runesealedUnlockHoleIndex"]=-1;zeroRune["holeInfos"][1]["runesealedUnlockHoleIndex"]=0;
        Reject(()=>V3IndependentReplay.Verify(zeroRune.ToString(),catalog,new JArray(runePlan[2].DeepClone(),runePlan[1].DeepClone(),runePlan[0].DeepClone())));
        const string axis="""
        {"sizeX":3,"sizeY":2,"holeInfos":[
        {"position":{"x":0,"y":0},"holeType":10,"colorIds":[1],"numOfCats":[1],"movementType":1,"movementTypeLimit":1},
        {"position":{"x":0,"y":1},"holeType":10,"colorIds":[2],"numOfCats":[1]}],
        "catInfos":[{"position":{"x":2,"y":0},"colorId":1},{"position":{"x":1,"y":1},"colorId":2}]}
        """;
        JArray axisPlan=new(new JObject{["holeId"]=1,["start"]=new JObject{["x"]=0,["y"]=1},
            ["path"]=new JArray(new JObject{["x"]=0,["y"]=1},new JObject{["x"]=1,["y"]=1})},
            new JObject{["holeId"]=0,["start"]=new JObject{["x"]=0,["y"]=0},
            ["path"]=new JArray(new JObject{["x"]=0,["y"]=0},new JObject{["x"]=1,["y"]=0},new JObject{["x"]=2,["y"]=0})});
        V3IndependentReplay.Verify(axis,catalog,axisPlan);
        Reject(()=>V3IndependentReplay.Verify(axis,catalog,new JArray(axisPlan[1].DeepClone(),axisPlan[0].DeepClone())));
        var thaw=JObject.Parse(axis);thaw["holeInfos"][0]["movementType"]=0;thaw["holeInfos"][0]["movementTypeLimit"]=0;
        thaw["catInfos"][0]["position"]["x"]=1;
        foreach(string counter in new[]{"numIced","hiddenCount"})
        {
            var fixture=(JObject)thaw.DeepClone();fixture["holeInfos"][0][counter]=1;
            var solved=Product.CatLevelSolver.SolveLevel(fixture.ToString());
            if(solved.status!=Product.SolverStatus.Solved)throw new Exception("Independent thaw/reveal fixture failed product solve.");
            V3IndependentReplay.Verify(fixture.ToString(),catalog,JArray.FromObject(solved.moves));
            Reject(()=>V3IndependentReplay.Verify(fixture.ToString(),catalog,new JArray(new JObject{["holeId"]=0,["start"]=new JObject{["x"]=0,["y"]=0},
                ["path"]=new JArray(new JObject{["x"]=0,["y"]=0},new JObject{["x"]=1,["y"]=0})})));
        }
        var catIce=JObject.Parse(basic);catIce["holeInfos"][0]["numOfCats"][0]=2;
        catIce["catInfos"]=new JArray(new JObject{["position"]=new JObject{["x"]=1,["y"]=0},["colorId"]=0},
            new JObject{["position"]=new JObject{["x"]=2,["y"]=0},["colorId"]=0,["numIced"]=1});
        var catIceSolved=Product.CatLevelSolver.SolveLevel(catIce.ToString());
        if(catIceSolved.status!=Product.SolverStatus.Solved)throw new Exception("Independent per-cat thaw fixture failed.");
        var catIcePlan=JArray.FromObject(catIceSolved.moves);V3IndependentReplay.Verify(catIce.ToString(),catalog,catIcePlan);
        catIce["catInfos"][0]["numIced"]=1;
        Reject(()=>V3IndependentReplay.Verify(catIce.ToString(),catalog,catIcePlan));
        var keyFixture=(JObject)thaw.DeepClone();keyFixture["holeInfos"][0]["lockColorId"]=0;keyFixture["holeInfos"][1]["numOfCats"][0]=2;
        keyFixture["catInfos"][1]["keyColorIds"]=0;
        ((JArray)keyFixture["catInfos"]).Add(new JObject{["colorId"]=2,["keyColorIds"]=0,["position"]=new JObject{["x"]=2,["y"]=1}});
        var keySolved=Product.CatLevelSolver.SolveLevel(keyFixture.ToString());
        if(keySolved.status!=Product.SolverStatus.Solved)throw new Exception("Independent two-key zero-ID fixture failed.");
        V3IndependentReplay.Verify(keyFixture.ToString(),catalog,JArray.FromObject(keySolved.moves));
        Reject(()=>V3IndependentReplay.Verify(keyFixture.ToString(),catalog,new JArray(
            new JObject{["holeId"]=1,["start"]=new JObject{["x"]=0,["y"]=1},["path"]=new JArray(new JObject{["x"]=0,["y"]=1},new JObject{["x"]=1,["y"]=1})},
            new JObject{["holeId"]=0,["start"]=new JObject{["x"]=0,["y"]=0},["path"]=new JArray(new JObject{["x"]=0,["y"]=0},new JObject{["x"]=1,["y"]=0})})));
        var mystery=JObject.Parse(basic);mystery["holeInfos"][0]["numOfCats"][0]=2;
        mystery["catInfos"]=new JArray(new JObject{["position"]=new JObject{["x"]=1,["y"]=0},["colorId"]=0},
            new JObject{["position"]=new JObject{["x"]=2,["y"]=0},["colorId"]=0,["mysteryBoxId"]=0});
        var mysterySolved=Product.CatLevelSolver.SolveLevel(mystery.ToString());
        if(mysterySolved.status!=Product.SolverStatus.Solved)throw new Exception("Independent mystery reveal fixture failed.");
        var mysteryPlan=JArray.FromObject(mysterySolved.moves);V3IndependentReplay.Verify(mystery.ToString(),catalog,mysteryPlan);
        mystery["catInfos"][0]["mysteryBoxId"]=0;
        Reject(()=>V3IndependentReplay.Verify(mystery.ToString(),catalog,mysteryPlan));
        var unsupported=JObject.Parse(basic);unsupported["catInfos"][0]["scissorsColorId"]=0;
        bool reported=false;
        try {V3IndependentReplay.Verify(unsupported.ToString(),catalog,plan);}catch(NotSupportedException){reported=true;}
        if(!reported)throw new Exception("Independent verifier silently ignored active zero-ID scissors rule.");
        Console.WriteLine("PASS independent coordinate replay: basic, plug, expand; rejects teleport, wrong plug and missing rule");
        static void Reject(Action action)
        {
            bool rejected=false;try{action();}catch(Exception){rejected=true;}
            if(!rejected)throw new Exception("Independent verifier accepted mutated invalid plan/input.");
        }
    }
}



