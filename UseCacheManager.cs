using System;
using System.Collections.Generic;
using CacheManager.Core;

namespace EFCache.CacheManager
{
    /// <summary>
    ///     EFCache's ICache implementation which uses CacheManager as middleware
    /// </summary>
    public class UseCacheManager : ICache
    {
        public UseCacheManager(ICacheManager<object> valuesCacheManager, ICacheManager<ISet<string>> dependenciesCacheManager)
        {
            _valuesCacheManager = valuesCacheManager;
            _dependenciesCacheManager = dependenciesCacheManager;
        }

        private readonly ICacheManager<ISet<string>> _dependenciesCacheManager;
        private readonly ICacheManager<object> _valuesCacheManager;

        public bool GetItem(string key, out object value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            return _valuesCacheManager.TryGetOrAdd(key, v => default(object), out value);
        }

        private static CacheItem<object> CreateCacheItem(string key, object value, TimeSpan slidingExpiration,
            DateTimeOffset absoluteExpiration)
        {
            var hasSlidingExpiration = slidingExpiration != TimeSpan.MaxValue;
            var hasAbsoluteExpiration = absoluteExpiration != DateTimeOffset.MaxValue;
            if (!hasAbsoluteExpiration && !hasSlidingExpiration)
            {
                return new CacheItem<object>(key, value);
            }
            if (hasSlidingExpiration)
            {
                return new CacheItem<object>(key, value, ExpirationMode.Sliding, slidingExpiration);
            }
            return new CacheItem<object>(key, value, ExpirationMode.Absolute, absoluteExpiration.Offset);
        }

        public void PutItem(string key, object value, IEnumerable<string> dependentEntitySets, TimeSpan slidingExpiration,
            DateTimeOffset absoluteExpiration)
        {
            var entitySets = new HashSet<string>(dependentEntitySets);
            foreach (var entitySet in entitySets)
            {
                _dependenciesCacheManager.AddOrUpdate(entitySet, new HashSet<string> { key },
                    updateValue: set =>
                    {
                        set.Add(key);
                        return set;
                    });
            }
            _valuesCacheManager.Add(CreateCacheItem(key, value, slidingExpiration, absoluteExpiration));
        }

        public void InvalidateSets(IEnumerable<string> entitySets)
        {
            foreach (var entitySet in entitySets)
            {
                if (string.IsNullOrWhiteSpace(entitySet))
                {
                    continue;
                }

                InvalidateItem(entitySet);
                _dependenciesCacheManager.Remove(entitySet);
            }
        }

        public void InvalidateItem(string key)
        {
            var dependencyKeys = _dependenciesCacheManager.Get(key);
            if (dependencyKeys == null)
            {
                return;
            }

            foreach (var dependencyKey in dependencyKeys)
            {
                _valuesCacheManager.Remove(dependencyKey);
            }
        }

        public void Clear()
        {
            _dependenciesCacheManager.Clear();
            _valuesCacheManager.Clear();
        }
    }
}
