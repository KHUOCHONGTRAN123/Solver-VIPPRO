#if CATDOM_PROFILE
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private readonly Dictionary<string,long[]> searchProfile=new Dictionary<string,long[]>();
            private ProfileScope Profile(string name)=>new ProfileScope(this,name);
            private readonly struct ProfileScope : IDisposable
            {
                private readonly Engine owner;
                private readonly string name;
                private readonly long start;
                internal ProfileScope(Engine owner,string name)
                {this.owner=owner;this.name=name;start=Stopwatch.GetTimestamp();}
                public void Dispose()
                {
                    long elapsed=Stopwatch.GetTimestamp()-start;
                    if(!owner.searchProfile.TryGetValue(name,out var row))owner.searchProfile[name]=row=new long[2];
                    row[0]+=elapsed;row[1]++;
                }
            }
        }
    }
}
#endif
