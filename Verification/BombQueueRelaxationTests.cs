using System.Reflection;
using Product=CatDom.CoreSolver;

internal static class BombQueueRelaxationTests
{
    internal static void Run()
    {
        var method=typeof(Product.CatLevelSolver).Assembly.GetType("CatDom.CoreSolver.BombQueueRelaxation").GetMethod("Feasible",BindingFlags.Static|BindingFlags.NonPublic);
        bool Relax(int need,int allowed,int[] colors,int[] capacities,int[][] queues,int limit=4096)
            =>(bool)method.Invoke(null,new object[]{0,need,allowed,colors,capacities,queues,limit});
        // Independent token-by-token simulator: any front token can be consumed
        // by any matching unfinished hole. Counts actual other-hole finishes.
        bool Exact(int need,int allowed,int[] colors,int[] capacities,int[][] queues)
        {
            var counts=new[]{need}.Concat(capacities).ToArray();var allColors=new[]{0}.Concat(colors).ToArray();
            var offsets=new int[queues.Length];var seen=new HashSet<string>();
            bool Search(int left)
            {
                if(counts[0]==0)return true;
                string key=string.Join(',',counts)+"|"+string.Join(',',offsets)+"|"+left;
                if(!seen.Add(key))return false;
                for(int q=0;q<queues.Length;q++)
                {
                    if(offsets[q]>=queues[q].Length)continue;
                    int token=queues[q][offsets[q]];
                    for(int h=0;h<counts.Length;h++)
                    {
                        if(counts[h]==0 || allColors[h]!=token)continue;
                        counts[h]--;offsets[q]++;
                        int cost=h>0 && counts[h]==0?1:0;
                        bool solved=left>=cost && Search(left-cost);
                        counts[h]++;offsets[q]--;
                        if(solved)return true;
                    }
                }
                return false;
            }
            return Search(allowed);
        }
        var random=new Random(731);int proven=0;
        for(int test=0;test<2000;test++)
        {
            int need=random.Next(1,4),allowed=random.Next(0,3);
            var colors=Enumerable.Range(0,random.Next(1,4)).Select(_=>random.Next(0,3)).ToArray();
            var capacities=colors.Select(_=>random.Next(1,4)).ToArray();
            var queues=Enumerable.Range(0,random.Next(1,4)).Select(_=>Enumerable.Range(0,random.Next(0,6)).Select(_=>random.Next(0,3)).ToArray()).ToArray();
            bool relaxed=Relax(need,allowed,colors,capacities,queues);
            if(!relaxed)
            {
                if(Exact(need,allowed,colors,capacities,queues))throw new Exception("Bomb queue relaxation falsely pruned a feasible token schedule.");
                proven++;
            }
        }
        if(proven==0)throw new Exception("Bomb queue test exercised no rejected state.");
        if(!Relax(1,0,new[]{1},new[]{1},new[]{new[]{1,0}},0))throw new Exception("Bomb queue work exhaustion must fail open.");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var engineType=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        bool EngineDeadEnd(int countdown,bool visible=false,bool multiLayer=false,bool spawner=false)
        {
            var board=new Product.BoardInput{width=5,height=2};
            board.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,bombRemaining=countdown,
                layerColors=new[]{0},layerCounts=new[]{1},position=new(0,0),footprint=new[]{new Product.Cell(0,0)}});
            board.holes.Add(new Product.HoleInput{id=1,color=1,remaining=1,
                layerColors=multiLayer?new[]{1,2}:new[]{1},layerCounts=multiLayer?new[]{1,1}:new[]{1},
                position=new(2,0),footprint=new[]{new Product.Cell(0,0)}});
            board.boxes.Add(new Product.BoxInput{id=0,position=new(4,0),mouth=new(3,0),direction=1,colors=new[]{1,0},
                cats=new[]{new Product.CatInput{id=0,color=1},new Product.CatInput{id=1,color=0}}});
            if(visible)board.cats.Add(new Product.CatInput{id=0,color=0,numIced=2,position=new(0,1)});
            if(spawner)board.spawners.Add(new Product.SpawnerInput{position=new(4,1),direction=0,holeIds=Array.Empty<int>()});
            var engine=Activator.CreateInstance(engineType,flags,null,new object[]{board,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
            return (bool)engineType.GetMethod("BombQueueDeadEnd",flags).Invoke(engine,null);
        }
        if(!EngineDeadEnd(1))throw new Exception("Bomb queue guard missed a forced finish before a one-count bomb.");
        if(EngineDeadEnd(2))throw new Exception("Bomb queue guard rejected an allowed other-hole finish.");
        if(EngineDeadEnd(1,visible:true))throw new Exception("Bomb queue guard must optimistically count iced board cats.");
        if(EngineDeadEnd(1,multiLayer:true))throw new Exception("Bomb queue guard must skip future layers.");
        if(EngineDeadEnd(1,spawner:true))throw new Exception("Bomb queue guard must skip future spawned capacity.");
        Console.WriteLine($"PASS bomb queue relaxation: 2000 independent token schedules, {proven} rejected states checked; bounded work fails open");
    }
}
