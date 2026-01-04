using System;
using System.Collections.Generic;

namespace Kneeboard_Server.Navigraph
{
    /// <summary>
    /// Simple in-memory cache for Navigraph database queries
    /// </summary>
    public class NavigraphDbCache
    {
        private readonly Dictionary<string, CacheEntry<AirportInfo>> _airportCache;
        private readonly Dictionary<string, CacheEntry<List<RunwayInfo>>> _runwayCache;
        private readonly Dictionary<string, CacheEntry<WaypointInfo>> _waypointCache;
        private readonly Dictionary<string, CacheEntry<NavaidInfo>> _navaidCache;

        private readonly TimeSpan _defaultExpiry = TimeSpan.FromMinutes(30);
        private readonly int _maxCacheSize = 500;

        public NavigraphDbCache()
        {
            _airportCache = new Dictionary<string, CacheEntry<AirportInfo>>();
            _runwayCache = new Dictionary<string, CacheEntry<List<RunwayInfo>>>();
            _waypointCache = new Dictionary<string, CacheEntry<WaypointInfo>>();
            _navaidCache = new Dictionary<string, CacheEntry<NavaidInfo>>();
        }

        #region Airport Cache

        public AirportInfo GetAirport(string icao)
        {
            var key = icao.ToUpperInvariant();
            if (_airportCache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                return entry.Value;
            }
            return null;
        }

        public void SetAirport(string icao, AirportInfo airport)
        {
            var key = icao.ToUpperInvariant();
            CleanupIfNeeded(_airportCache);
            _airportCache[key] = new CacheEntry<AirportInfo>(airport, _defaultExpiry);
        }

        #endregion

        #region Runway Cache

        public List<RunwayInfo> GetRunways(string icao)
        {
            var key = icao.ToUpperInvariant();
            if (_runwayCache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                return entry.Value;
            }
            return null;
        }

        public void SetRunways(string icao, List<RunwayInfo> runways)
        {
            var key = icao.ToUpperInvariant();
            CleanupIfNeeded(_runwayCache);
            _runwayCache[key] = new CacheEntry<List<RunwayInfo>>(runways, _defaultExpiry);
        }

        #endregion

        #region Waypoint Cache

        public WaypointInfo GetWaypoint(string ident)
        {
            var key = ident.ToUpperInvariant();
            if (_waypointCache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                return entry.Value;
            }
            return null;
        }

        public void SetWaypoint(string ident, WaypointInfo waypoint)
        {
            var key = ident.ToUpperInvariant();
            CleanupIfNeeded(_waypointCache);
            _waypointCache[key] = new CacheEntry<WaypointInfo>(waypoint, _defaultExpiry);
        }

        #endregion

        #region Navaid Cache

        public NavaidInfo GetNavaid(string ident)
        {
            var key = ident.ToUpperInvariant();
            if (_navaidCache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                return entry.Value;
            }
            return null;
        }

        public void SetNavaid(string ident, NavaidInfo navaid)
        {
            var key = ident.ToUpperInvariant();
            CleanupIfNeeded(_navaidCache);
            _navaidCache[key] = new CacheEntry<NavaidInfo>(navaid, _defaultExpiry);
        }

        #endregion

        #region Cache Management

        public void Clear()
        {
            _airportCache.Clear();
            _runwayCache.Clear();
            _waypointCache.Clear();
            _navaidCache.Clear();
        }

        private void CleanupIfNeeded<T>(Dictionary<string, CacheEntry<T>> cache)
        {
            if (cache.Count < _maxCacheSize)
                return;

            // Remove expired entries
            var keysToRemove = new List<string>();
            foreach (var kvp in cache)
            {
                if (kvp.Value.IsExpired)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                cache.Remove(key);
            }

            // If still too large, remove oldest entries
            if (cache.Count >= _maxCacheSize)
            {
                var oldestKeys = new List<string>();
                DateTime oldest = DateTime.MaxValue;

                foreach (var kvp in cache)
                {
                    if (kvp.Value.CreatedAt < oldest)
                    {
                        oldest = kvp.Value.CreatedAt;
                        oldestKeys.Clear();
                        oldestKeys.Add(kvp.Key);
                    }
                }

                foreach (var key in oldestKeys)
                {
                    cache.Remove(key);
                }
            }
        }

        #endregion

        #region Cache Entry

        private class CacheEntry<T>
        {
            public T Value { get; }
            public DateTime CreatedAt { get; }
            public DateTime ExpiresAt { get; }
            public bool IsExpired => DateTime.UtcNow > ExpiresAt;

            public CacheEntry(T value, TimeSpan expiry)
            {
                Value = value;
                CreatedAt = DateTime.UtcNow;
                ExpiresAt = CreatedAt.Add(expiry);
            }
        }

        #endregion
    }
}
