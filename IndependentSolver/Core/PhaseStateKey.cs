using System.Collections.Generic;
namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private string cachedPhaseKey;
            private int cachedPhaseId=-1;
            // Exact canonical strings determine IDs; hash collisions are
            // resolved by Dictionary equality, never treated as state equality.
            private readonly Dictionary<string,int> phaseKeyIds=new Dictionary<string,int>();
            private void InvalidatePhaseKey(){cachedPhaseKey=null;cachedPhaseId=-1;}
        }
    }
}
