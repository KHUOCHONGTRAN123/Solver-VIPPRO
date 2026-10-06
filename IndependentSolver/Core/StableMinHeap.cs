using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    internal sealed class StableMinHeap<T>
    {
        private readonly List<(long score, int serial, T node)> items = new List<(long, int, T)>(128);
        internal int Count => items.Count;
        private static int Compare((long score, int serial, T node) a, (long score, int serial, T node) b)
        { int order = a.score.CompareTo(b.score); return order != 0 ? order : a.serial.CompareTo(b.serial); }
        internal void Add((long score, int serial, T node) item)
        {
            int at = items.Count; items.Add(item);
            while (at > 0)
            {
                int parent = (at - 1) / 2;
                if (Compare(items[parent], item) <= 0) break;
                items[at] = items[parent]; at = parent;
            }
            items[at] = item;
        }
        internal (long score, int serial, T node) Pop()
        {
            if (items.Count == 0) throw new InvalidOperationException("The heap is empty.");
            var result = items[0]; var last = items[items.Count - 1]; items.RemoveAt(items.Count - 1);
            if (items.Count == 0) return result;
            int at = 0;
            while (at * 2 + 1 < items.Count)
            {
                int child = at * 2 + 1;
                if (child + 1 < items.Count && Compare(items[child + 1], items[child]) < 0) child++;
                if (Compare(last, items[child]) <= 0) break;
                items[at] = items[child]; at = child;
            }
            items[at] = last; return result;
        }
    }
}
