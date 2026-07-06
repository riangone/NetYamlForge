namespace NetYamlForge.Services.AI.SlotFilling;

public interface IConversationFsmStore
{
    bool TryGet(string fsmKey, out IConversationFsm? fsm);
    void Set(string fsmKey, IConversationFsm fsm);
    void Remove(string fsmKey);
}
