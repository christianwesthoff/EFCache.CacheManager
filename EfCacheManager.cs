using System;
using System.Collections.Generic;
using System.Configuration;
using CacheManager.Core;
using Newtonsoft.Json;
using StackExchange.Redis;
using ConfigurationBuilder = CacheManager.Core.ConfigurationBuilder;

namespace EFCache.CacheManager
{
    public static class EfCacheManager
    {
        private const string CacheValueHandlerKey = "EFCache_ValueHandler";
        private const string CacheEntityHandlerKey = "EFCache_EntityHandler";

        /// <summary>
        ///     Initializes the cache
        /// </summary>
        /// <param name="connectionStr"></param>
        /// <param name="delegateAction"></param>
        public static void SetupCache(string connectionStr, Action<ICache> delegateAction)
        {
            ICache InitInMemoryCache()
            {
                var inMemoryConfiguration = new ConfigurationBuilder()
                    .WithDictionaryHandle()
                    .Build();
                return new UseCacheManager(CacheFactory.FromConfiguration<object>(CacheValueHandlerKey, inMemoryConfiguration),
                    CacheFactory.FromConfiguration<ISet<string>>(CacheEntityHandlerKey, inMemoryConfiguration));
            }

            var redisConfigString = ConfigurationManager.ConnectionStrings[connectionStr]?.ToString();
            if (string.IsNullOrWhiteSpace(redisConfigString))
            {
                delegateAction(InitInMemoryCache());
                return;
            }
            var redisConfig = ConfigurationOptions.Parse(redisConfigString);
            redisConfig.AbortOnConnectFail = false;
            redisConfig.AllowAdmin = true;
            var multiplexer = ConnectionMultiplexer.Connect(redisConfig);

            ICache InitRedisCache()
            {
                var jss = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                };
                var redisConfiguration = new ConfigurationBuilder().WithDictionaryHandle()
                    .And
                    .WithJsonSerializer(serializationSettings: jss, deserializationSettings: jss)
                    .WithUpdateMode(CacheUpdateMode.Up)
                    .WithRedisConfiguration(connectionStr, multiplexer)
                    .WithMaxRetries(100)
                    .WithRetryTimeout(50)
                    .WithRedisCacheHandle(connectionStr)
                    .Build();
                return new UseCacheManager(CacheFactory.FromConfiguration<object>(CacheValueHandlerKey, redisConfiguration),
                    CacheFactory.FromConfiguration<ISet<string>>(CacheEntityHandlerKey, redisConfiguration));
            }

            var cachingMiddleware = new RedisCacheResilentMiddleware(InitRedisCache, InitInMemoryCache, multiplexer);
            delegateAction(cachingMiddleware);
        }
    }
}
