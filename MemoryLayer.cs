using System;
using System.Collections.Generic;

namespace SheetsQueryAgent
{
    public class CacheEntry
    {
        public List<Dictionary<string, string>> Data { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class HistoryEntry
    {
        public string Timestamp { get; set; } = "";
        public string Operation { get; set; } = "";
        public string Details { get; set; } = "";
    }

    public class AgentMemoryLayer
    {
        public Dictionary<string, CacheEntry> Cache { get; } = new();
        public List<HistoryEntry> History { get; } = new();
        public int CacheTtlSeconds { get; }

        public AgentMemoryLayer(int cacheTtlSeconds = 300)
        {
            CacheTtlSeconds = cacheTtlSeconds;
        }

        public List<Dictionary<string, string>>? GetCachedData(string key)
        {
            if (Cache.TryGetValue(key, out var entry))
            {
                var elapsed = (DateTime.UtcNow - entry.Timestamp).TotalSeconds;
                if (elapsed < CacheTtlSeconds)
                {
                    Console.WriteLine($"[Memory Layer] Cache hit for key '{key}'.");
                    // Return copy of cached data to prevent external mutation issues
                    return entry.Data.Select(d => new Dictionary<string, string>(d)).ToList();
                }
                else
                {
                    Console.WriteLine($"[Memory Layer] Cache expired for key '{key}'.");
                    Cache.Remove(key);
                }
            }
            return null;
        }

        public void SetCachedData(string key, List<Dictionary<string, string>> data)
        {
            Console.WriteLine($"[Memory Layer] Caching data for key '{key}'.");
            // Store a copy to avoid reference sharing problems
            var copy = data.Select(d => new Dictionary<string, string>(d)).ToList();
            Cache[key] = new CacheEntry
            {
                Data = copy,
                Timestamp = DateTime.UtcNow
            };
        }

        public void AddHistoryEntry(string operation, string details)
        {
            var entry = new HistoryEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Operation = operation,
                Details = details
            };
            string detailsSnippet = details.Length > 60 ? details.Substring(0, 60) + "..." : details;
            Console.WriteLine($"[Memory Layer] Logging action: {operation} - {detailsSnippet}");
            History.Add(entry);
        }

        public List<HistoryEntry> GetHistory()
        {
            return History;
        }

        public void ClearCache()
        {
            Cache.Clear();
            Console.WriteLine("[Memory Layer] Cache cleared.");
        }
    }

    // Singleton / Helper instance
    public static class MemoryLayer
    {
        private static AgentMemoryLayer? _memoryLayer;

        public static AgentMemoryLayer GetInstance()
        {
            return _memoryLayer ??= new AgentMemoryLayer();
        }

        public static string LogAgentAction(string operation, string details)
        {
            GetInstance().AddHistoryEntry(operation, details);
            return $"Logged action '{operation}' to memory history.";
        }

        public static List<HistoryEntry> GetAgentHistory()
        {
            return GetInstance().GetHistory();
        }

        public static string ClearMemoryCache()
        {
            GetInstance().ClearCache();
            return "Cache cleared successfully.";
        }
    }
}
