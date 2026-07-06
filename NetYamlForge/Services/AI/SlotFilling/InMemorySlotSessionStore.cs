using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NetYamlForge.Services.AI.SlotFilling;

public class InMemorySlotSessionStore : ISlotSessionStore
{
    private readonly ConcurrentDictionary<string, SlotSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string sessionKey, out SlotSession? session)
    {
        var result = _sessions.TryGetValue(sessionKey, out var s);
        session = s;
        return result;
    }

    public void Set(string sessionKey, SlotSession session)
    {
        _sessions[sessionKey] = session;
    }

    public void Remove(string sessionKey)
    {
        _sessions.TryRemove(sessionKey, out _);
    }

    public IEnumerable<KeyValuePair<string, SlotSession>> GetAllSessions()
    {
        return _sessions.ToList();
    }
}
