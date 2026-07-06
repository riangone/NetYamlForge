using System.Collections.Generic;

namespace NetYamlForge.Services.AI.SlotFilling;

public interface ISlotSessionStore
{
    bool TryGet(string sessionKey, out SlotSession? session);
    void Set(string sessionKey, SlotSession session);
    void Remove(string sessionKey);
    IEnumerable<KeyValuePair<string, SlotSession>> GetAllSessions();
}
