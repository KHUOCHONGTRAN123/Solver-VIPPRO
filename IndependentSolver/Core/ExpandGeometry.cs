namespace CatDom.CoreSolver
{
    internal static class ExpandGeometry
    {
        internal static bool Enabled(HoleInput hole) => hole.expandCell.x >= 0 && hole.expandCell.y >= 0;
        internal static Cell[] Footprint(HoleInput hole, bool picked)
            => Enabled(hole) && !picked ? new[] { hole.expandCell } : hole.footprint;
    }
}
