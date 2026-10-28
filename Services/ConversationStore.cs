using Microsoft.Extensions.AI;

namespace RagApi.Services;

// Holds conversation history in memory — one list of messages per conversationId.
// In production this would be Redis or a database so it survives restarts.
public interface IConversationStore
{
    List<ChatMessage> GetHistory(string conversationId);
    void              AddMessage(string conversationId, ChatMessage message);
    void              Clear(string conversationId);
}

public class ConversationStore : IConversationStore
{
    // How many messages (user + assistant pairs) to keep per conversation
    private const int MaxMessages = 6;

    private readonly Dictionary<string, List<ChatMessage>> _store = new();
    private readonly Lock _lock = new();

    public List<ChatMessage> GetHistory(string conversationId)
    {
        lock (_lock)
        {
            return _store.TryGetValue(conversationId, out var history)
                ? [.. history]   // return a copy
                : [];
        }
    }

    public void AddMessage(string conversationId, ChatMessage message)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue(conversationId, out var history))
            {
                history = [];
                _store[conversationId] = history;
            }

            history.Add(message);

            // Sliding window — keep only the last MaxMessages messages
            // This prevents the context window from growing forever
            while (history.Count > MaxMessages)
                history.RemoveAt(0);
        }
    }

    public void Clear(string conversationId)
    {
        lock (_lock)
            _store.Remove(conversationId);
    }
}
