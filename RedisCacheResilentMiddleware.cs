using System;
using System.Collections.Generic;
using System.Linq;
using MoreLinq;
using StackExchange.Redis;

namespace EFCache.CacheManager
{
    /// <summary>
    ///     EFCache's ICache implementation for a caching middleware which uses a distributed
    ///     as well as a^fallback in-memory cache and automatically syncs states
    /// </summary>
    public class RedisCacheResilentMiddleware : ICache
    {
        private readonly IConnectionMultiplexer _connection;
        private readonly Func<ICache> _inMemoryInitFunc;
        private readonly Func<ICache> _distributedCacheInitFunc;

        private readonly ISet<string> _changedEntities;
        private readonly ISet<string> _changedKeys;
        private ICache _distributed;
        private ICache _inMemory;
        private bool _distributedEnabled;

        /// <summary>
        ///     Constructor
        /// </summary>
        /// <param name="distributedCacheInitFunc">Function which initializes the distributed cache</param>
        /// <param name="inMemoryInitFunc">Function which initializes the in-memory cache</param>
        /// <param name="connection">Connection for distributed cache</param>
        public RedisCacheResilentMiddleware(Func<ICache> distributedCacheInitFunc, Func<ICache> inMemoryInitFunc, IConnectionMultiplexer connection)
        {
            _connection = connection;
            _connection.ConnectionFailed += OnConnectionFailed;
            _connection.ConnectionRestored += OnConnectionRestored;

            _distributedCacheInitFunc = distributedCacheInitFunc;
            _inMemoryInitFunc = inMemoryInitFunc;

            _changedEntities = new HashSet<string>();
            _changedKeys = new HashSet<string>();

            InitInMemory();
            InitDistributed();
        }

        #region Init
        private void InitInMemory()
        {
            if (_inMemory != null) return; 
            _inMemory = _inMemoryInitFunc();
        }

        private void InitDistributed()
        {
            if (!_connection.IsConnected || _distributed != null) return;
            _distributed = _distributedCacheInitFunc();
            _distributedEnabled = true;
        }
        #endregion

        /// <summary>
        ///     Returns the active caching instance
        /// </summary>
        private ICache Cache
        {
            get
            {
                if (_distributedEnabled)
                {
                    return _distributed;
                }

                return _inMemory;
            }
        }
        /// <summary>
        ///     Invalidates entity sets and keys if updated locally
        /// </summary>
        private void SyncLocalCacheState()
        {
            if (_distributedEnabled && (_changedEntities.Count > 0 || _changedKeys.Count > 0))
            {
                // Invalidate old cache state
                _distributed.InvalidateSets(_changedEntities);
                _changedKeys.ForEach(k => _distributed.InvalidateItem(k));
                // Clear state
                _changedEntities.Clear();
                _changedKeys.Clear();
            }
            _inMemory.Clear();
        }

        #region EventHandlers
        private void OnConnectionFailed(object sender, ConnectionFailedEventArgs args)
        {
            if (args.ConnectionType != ConnectionType.Interactive) return;
            _distributedEnabled = false;
            InitInMemory();
            Console.WriteLine("Connection failed, disabling Redis...");
        }

        private void OnConnectionRestored(object sender, ConnectionFailedEventArgs args)
        {
            if (args.ConnectionType != ConnectionType.Interactive) return;
            _distributedEnabled = true;
            InitDistributed();
            SyncLocalCacheState();
            Console.WriteLine("Connection restored, Redis is back online...");
        }
        #endregion

        #region ICache
        public bool GetItem(string key, out object value)
        {
            return Cache.GetItem(key, out value);
        }

        public void PutItem(string key, object value, IEnumerable<string> dependentEntitySets, TimeSpan slidingExpiration,
            DateTimeOffset absoluteExpiration)
        {
            var entitySets = dependentEntitySets.ToList();
            if (!_distributedEnabled)
            {
                entitySets.ForEach(e => _changedEntities.Add(e));
                _changedKeys.Add(key);
            }
            Cache.PutItem(key, value, entitySets, slidingExpiration, absoluteExpiration);
        }

        public void InvalidateSets(IEnumerable<string> entitySets)
        {
            var entitySetsList = entitySets.ToList();
            if (!_distributedEnabled)
            {
                entitySetsList.ForEach(e => _changedEntities.Add(e));
            }
            Cache.InvalidateSets(entitySetsList);
        }

        public void InvalidateItem(string key)
        {
            if (!_distributedEnabled)
            {
               _changedKeys.Add(key);
            }
            Cache.InvalidateItem(key);
        }

        public void Clear()
        {
            Cache.Clear();
        }
        #endregion
    }
}
