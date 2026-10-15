using System;
using System.Collections.Generic;

namespace Sample.Unit__N__
{
    public struct CacheKey
    {
        public int Tenant;
        public string Name;
    }

    public class Cache<TValue>
    {
        private readonly Dictionary<CacheKey, TValue> _items = new Dictionary<CacheKey, TValue>();
        private readonly object _gate = new object();
        private long _hits;
        private long _misses;

        public bool TryGet(int tenant, string name, out TValue value)
        {
            var key = new CacheKey { Tenant = tenant, Name = name };
            lock (_gate)
            {
                if (_items.ContainsKey(key))
                {
                    value = _items[key];
                    _hits++;
                    return true;
                }
                _misses++;
                value = default(TValue);
                return false;
            }
        }

        public void Set(int tenant, string name, TValue value)
        {
            var key = new CacheKey { Tenant = tenant, Name = name };
            lock (_gate)
            {
                if (_items.ContainsKey(key))
                    _items.Remove(key);
                _items.Add(key, value);
            }
        }

        public List<TValue> Values()
        {
            var result = new List<TValue>();
            foreach (var kv in _items)
                result.Add(kv.Value);
            return result;
        }

        public double HitRatio()
        {
            long total = _hits + _misses;
            return total == 0 ? 0 : (double)_hits / total;
        }
    }
}
