using AIM.Web.Models;
using System.Collections.Concurrent;

namespace AIM.Web.Services.Import;

public interface IImportCache
{
    string Store(List<BsaReport> rows);
    List<BsaReport>? Take(string uploadId);
}

public class InMemoryImportCache : IImportCache
{
    private readonly ConcurrentDictionary<string, (List<BsaReport> Rows, DateTime Expiry)> _cache = new();

    public string Store(List<BsaReport> rows)
    {
        Prune();
        var id = Guid.NewGuid().ToString("N")[..16];
        _cache[id] = (rows, DateTime.UtcNow.AddMinutes(15));
        return id;
    }

    public List<BsaReport>? Take(string uploadId)
    {
        Prune();
        return _cache.TryRemove(uploadId, out var v) ? v.Rows : null;
    }

    private void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var k in _cache.Where(kv => kv.Value.Expiry < now).Select(kv => kv.Key).ToList())
            _cache.TryRemove(k, out _);
    }
}
