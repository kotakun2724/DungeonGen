using System.Collections.Generic;

namespace DungeonGen.Core
{
    // binary‑heap min queue
    public class PriorityQueue<T>
    {
        readonly List<(T item, int prio)> h = new();
        public int Count => h.Count;

        public void Enqueue(T item, int prio)
        {
            h.Add((item, prio));
            for (int i = h.Count - 1; i > 0;)
            {
                int p = (i - 1) / 2;
                if (h[p].prio <= h[i].prio) break;
                (h[p], h[i]) = (h[i], h[p]); i = p;
            }
        }

        public T Dequeue()
        {
            var root = h[0].item;
            h[0] = h[^1]; h.RemoveAt(h.Count - 1);
            Heapify(0);
            return root;
        }

        void Heapify(int i)
        {
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, s = i;
                if (l < h.Count && h[l].prio < h[s].prio) s = l;
                if (r < h.Count && h[r].prio < h[s].prio) s = r;
                if (s == i) break;
                (h[i], h[s]) = (h[s], h[i]); i = s;
            }
        }
    }
}
