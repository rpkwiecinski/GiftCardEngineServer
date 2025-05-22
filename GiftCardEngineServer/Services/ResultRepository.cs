using System.Collections.Concurrent;
using System.Text.Json;
using GiftCardEngine.Models;

namespace GiftCardEngine.Services
{
    public class ResultRepository : IResultRepository
    {
        private const string HISTORY_PATH = "history/";
        private ConcurrentQueue<EngineRunResult> _last = new();

        public ResultRepository()
        {
            if (!Directory.Exists(HISTORY_PATH)) Directory.CreateDirectory(HISTORY_PATH);
        }

        public void SaveResult(EngineRunResult result)
        {
            _last.Enqueue(result);
            if (_last.Count > 100) _last.TryDequeue(out _);

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText($"{HISTORY_PATH}{result.Timestamp:yyyyMMddHHmmssfff}_{result.Job.JobName}.json", json);
        }

        public IEnumerable<EngineRunResult> GetLastResults() => _last.ToList();
        public EngineRunResult? GetLastResult() => _last.LastOrDefault();
    }
}
