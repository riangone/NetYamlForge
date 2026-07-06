using System;
using System.Collections.Concurrent;

namespace NetYamlForge.Services.AI.SlotFilling;

public class InMemoryConversationFsmStore : IConversationFsmStore
{
    private readonly ConcurrentDictionary<string, IConversationFsm> _fsmStates = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string fsmKey, out IConversationFsm? fsm)
    {
        var result = _fsmStates.TryGetValue(fsmKey, out var f);
        fsm = f;
        return result;
    }

    public void Set(string fsmKey, IConversationFsm fsm)
    {
        _fsmStates[fsmKey] = fsm;
    }

    public void Remove(string fsmKey)
    {
        _fsmStates.TryRemove(fsmKey, out _);
    }
}
