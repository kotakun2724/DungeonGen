using System.Collections.Generic;

namespace DungeonGen.Utils
{
    // 優先度付きキュー（ヒープ実装）
    public class PriorityQueue<T>
    {
        readonly List<(T item, int prio)> heap = new List<(T item, int prio)>();
        public int Count => heap.Count;

        public void Enqueue(T item, int prio)
        {
            heap.Add((item, prio));
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].prio <= heap[i].prio) break;
                (heap[parent], heap[i]) = (heap[i], heap[parent]);
                i = parent;
            }
        }

        public T Dequeue()
        {
            var root = heap[0].item;
            heap[0] = heap[^1];
            heap.RemoveAt(heap.Count - 1);
            Heapify(0);
            return root;
        }

        void Heapify(int i)
        {
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < heap.Count && heap[l].prio < heap[smallest].prio) smallest = l;
                if (r < heap.Count && heap[r].prio < heap[smallest].prio) smallest = r;
                if (smallest == i) break;
                (heap[smallest], heap[i]) = (heap[i], heap[smallest]);
                i = smallest;
            }
        }
    }
}