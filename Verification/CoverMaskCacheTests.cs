using System.Reflection;
using Product=CatDom.CoreSolver;

internal static class CoverMaskCacheTests
{
    internal static void Run()
    {
        var board=new Product.BoardInput{width=4,height=1};
        board.holes.Add(new Product.HoleInput{id=0,remaining=1,footprint=new[]{new Product.Cell(0,0)}});
        board.covers.Add(new Product.CoverInput{id=0,barricade=true,remainingHits=3,cells=new[]{new Product.Cell(1,0),new Product.Cell(2,0),new Product.Cell(3,0)}});
        board.covers.Add(new Product.CoverInput{id=1,rope=true,remainingHits=2,cells=new[]{new Product.Cell(2,0)}});
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        var engine=Activator.CreateInstance(type,flags,null,new object[]{board,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
        var method=type.GetMethod("CoverMask",flags);
        for(int a=3;a>=0;a--)
            for(int b=2;b>=0;b--)
            {
                board.covers[0].remainingHits=a;board.covers[1].remainingHits=b;
                Product.CellMask expected=Product.CellMask.Zero;
                for(int cell=1;cell<=a;cell++)expected|=Product.CellMask.One<<cell;
                if(b>0)expected|=Product.CellMask.One<<2;
                for(int repeat=0;repeat<2;repeat++)
                    if(!expected.Equals((Product.CellMask)method.Invoke(engine,null)))throw new Exception("Cover cache stale after hit transition.");
            }
        board.covers[0].remainingHits=3;board.covers[1].remainingHits=2;
        if(!((Product.CellMask)method.Invoke(engine,null)).Equals((Product.CellMask.One<<1)|(Product.CellMask.One<<2)|(Product.CellMask.One<<3)))
            throw new Exception("Cover cache stale after rollback to original hits.");
        Console.WriteLine("PASS cover mask cache hit transitions, reuse and rollback");
    }
}
