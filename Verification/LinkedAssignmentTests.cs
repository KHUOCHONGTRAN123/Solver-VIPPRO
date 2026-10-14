using System.Reflection;
using Product=CatDom.CoreSolver;

internal static class LinkedAssignmentTests
{
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(Product.CatLevelSolver).GetNestedType("Engine",BindingFlags.NonPublic);
        bool Possible(bool sand,bool releasesAxis)
        {
            var b=new Product.BoardInput{width=releasesAxis?4:3,height=2};
            b.holes.Add(new Product.HoleInput{id=0,color=0,remaining=1,position=new(0,0),footprint=new[]{new Product.Cell(0,0)}});
            b.holes.Add(new Product.HoleInput{id=1,color=sand?1000:1,remaining=1,position=new(1,0),
                movementType=releasesAxis?1:0,movementTypeLimit=releasesAxis?1:0,
                footprint=new[]{new Product.Cell(0,0),new Product.Cell(0,1)}});
            if(!releasesAxis)b.obstacles.Add(new(2,1));
            b.links.Add(new Product.LinkInput{holeId1=0,holeId2=1});
            b.cats.Add(new Product.CatInput{id=0,color=0,position=new(2,0)});
            if(!sand)b.cats.Add(new Product.CatInput{id=1,color=1,position=new(1,1)});
            var engine=Activator.CreateInstance(type,flags,null,new object[]{b,new Product.EngineContext(),CancellationToken.None,new Product.EngineResult()},null);
            return (bool)type.GetMethod("HasRelaxedCatAssignment",flags).Invoke(engine,null);
        }
        if(Possible(true,false))throw new Exception("Permanent sand link was allowed through a static obstacle.");
        if(!Possible(false,false))throw new Exception("A finishable partner must permit eventual link release.");
        if(!Possible(true,true))throw new Exception("Movement-limit release must remain optimistic for linked partners.");
        Console.WriteLine("PASS linked assignment: permanent obstacles, finishable link and future axis release");
    }
}
