using System.Reflection;
using Newtonsoft.Json;
using Product = CatDom.CoreSolver;

internal static class CheckpointTests
{
    internal static void Run(string root)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var catalog=Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root,"IndependentSolver/IO/HoleShapes.json")));
        var board=Product.IO.SolverJson.ReadLevel(File.ReadAllText(Path.Combine(root,"Levels/Level00206.json")),catalog);
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        var engine=Activator.CreateInstance(type,flags,null,new object[]{board,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        type.GetMethod("Prepare",flags).Invoke(engine,null);
        var fields=new[]{"capacities","layers","boxOffsets","catsAlive","current","valid","goals","distances","goalAnchors","targetDistances","catEntryBlocks","routeFixedMask"};
        var original=fields.ToDictionary(name=>name,name=>type.GetField(name,flags).GetValue(engine));
        var arrays=original.Where(pair=>pair.Value is Array).ToDictionary(pair=>pair.Key,pair=>JsonConvert.SerializeObject(pair.Value));
        string originalBoard=JsonConvert.SerializeObject(board);
        var checkpoint=type.GetMethod("SaveCheckpoint",flags).Invoke(engine,null);
        try
        {
            foreach(var name in new[]{"capacities","layers","boxOffsets"})
            {var values=(int[])original[name];for(int i=0;i<values.Length;i++)values[i]+=7;}
            var alive=(bool[])original["catsAlive"];for(int i=0;i<alive.Length;i++)alive[i]=!alive[i];
            foreach(var hole in board.holes){hole.numIced+=2;hole.hiddenCount+=3;hole.locked=!hole.locked;}
            foreach(var cat in board.cats)cat.numIced+=2;
            foreach(var box in board.boxes){box.requiredHolesToUnlock+=3;foreach(var cat in box.cats)cat.numIced+=4;}
            foreach(var cover in board.covers)cover.remainingHits+=5;
            type.GetMethod("Prepare",flags).Invoke(engine,null);
            throw new OperationCanceledException();
        }
        catch(OperationCanceledException){}
        finally {checkpoint.GetType().GetMethod("Restore",flags).Invoke(checkpoint,new[]{engine});}
        if(JsonConvert.SerializeObject(board)!=originalBoard)throw new Exception("Checkpoint board restore mismatch");
        foreach(var pair in arrays)
            if(JsonConvert.SerializeObject(type.GetField(pair.Key,flags).GetValue(engine))!=pair.Value)throw new Exception("Checkpoint array mismatch: "+pair.Key);
        foreach(var name in fields.Where(name=>name!="capacities"&&name!="layers"&&name!="boxOffsets"&&name!="catsAlive"))
            if(!Equals(type.GetField(name,flags).GetValue(engine),original[name]))throw new Exception("Checkpoint reference mismatch: "+name);
        checkpoint=type.GetMethod("SaveCheckpoint",flags).Invoke(engine,null);
        checkpoint.GetType().GetMethod("Restore",flags).Invoke(checkpoint,new[]{engine});
        type.GetField("stats",flags).SetValue(engine,new Product.SubproblemStats());
        CompareCachedMoves();
        var oldPhase=type.GetField("valid",flags).GetValue(engine);
        board.holes[0].numIced++;
        type.GetMethod("Prepare",flags).Invoke(engine,null);
        if(ReferenceEquals(oldPhase,type.GetField("valid",flags).GetValue(engine)))throw new Exception("Cache phase did not change");
        CompareCachedMoves();
        void CompareCachedMoves()
        {
            var state=type.GetField("current",flags).GetValue(engine);
            var fresh=(System.Collections.IList)type.GetMethod("GenerateUncached",flags).Invoke(engine,new[]{state});
            type.GetMethod("Generate",flags).Invoke(engine,new[]{state});
            var cached=(System.Collections.IList)type.GetMethod("Generate",flags).Invoke(engine,new[]{state});
            if(fresh.Count!=cached.Count)throw new Exception("Cached move count mismatch");
            for(int i=0;i<fresh.Count;i++)
            {
                foreach(var name in new[]{"hole","destination","state","pathLength","terminal"})
                {var field=fresh[i].GetType().GetField(name,flags);if(!Equals(field.GetValue(fresh[i]),field.GetValue(cached[i])))throw new Exception("Cached move mismatch: "+name);}
                var parents=fresh[i].GetType().GetField("pathParents",flags);
                if(!((int[])parents.GetValue(fresh[i])).SequenceEqual((int[])parents.GetValue(cached[i])))throw new Exception("Cached path mismatch");
            }
        }
        Console.WriteLine("PASS checkpoint mutation/phase restoration after cancellation and reuse");
    }
}
