using System;
using System.Collections.Generic;

namespace AlterOS.Dialogue
{
    public static class GreetingTopicSelection
    {
        public static List<T> Select<T>(IEnumerable<T> suggested, IEnumerable<T> authored,
            Func<T, string> key, int limit, Func<int, int> nextIndex)
        {
            var result = new List<T>();
            if (limit <= 0) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (T item in suggested ?? new T[0])
            {
                string identity = key(item);
                if (!string.IsNullOrWhiteSpace(identity) && seen.Add(identity)) result.Add(item);
                if (result.Count == limit) return result;
            }
            var pool = new List<T>();
            foreach (T item in authored ?? new T[0])
            {
                string identity = key(item);
                if (!string.IsNullOrWhiteSpace(identity) && seen.Add(identity)) pool.Add(item);
            }
            while (result.Count < limit && pool.Count != 0)
            {
                int index = nextIndex(pool.Count);
                if (index < 0 || index >= pool.Count) throw new ArgumentOutOfRangeException("nextIndex");
                result.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return result;
        }
    }
}
