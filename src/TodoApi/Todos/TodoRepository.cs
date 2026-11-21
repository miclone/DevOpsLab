using System.Collections.Concurrent;

namespace TodoApi.Todos;

public interface ITodoRepository
{
    IReadOnlyList<TodoItem> GetAll();
    TodoItem? Get(int id);
    TodoItem Add(string title);
    TodoItem? Update(int id, string title, bool isCompleted);
    bool Delete(int id);
}

/// <summary>
/// Stores to-dos in memory, so they are lost when the app restarts. A database
/// version replaces this class in stage 3; nothing else has to change, because
/// the rest of the app only knows about ITodoRepository.
///
/// A web server handles many requests at once, so this uses the thread-safe
/// patterns from ConcurrentCollectionsDemo.cs: ConcurrentDictionary for storage
/// and Interlocked for the ID counter.
/// </summary>
public sealed class InMemoryTodoRepository(TimeProvider clock) : ITodoRepository
{
    private readonly ConcurrentDictionary<int, TodoItem> _items = new();
    private int _lastId;

    public IReadOnlyList<TodoItem> GetAll() => _items.Values.OrderBy(t => t.Id).ToList();

    public TodoItem? Get(int id) => _items.TryGetValue(id, out var item) ? item : null;

    public TodoItem Add(string title)
    {
        var item = new TodoItem(Interlocked.Increment(ref _lastId), title, false, clock.GetUtcNow());
        _items[item.Id] = item;
        return item;
    }

    public TodoItem? Update(int id, string title, bool isCompleted)
    {
        // TryUpdate only succeeds if nobody else changed the item in the meantime;
        // if someone did, read it again and retry. This avoids the lost-update
        // problem that dict[key]++ showed in the concurrency demo.
        while (_items.TryGetValue(id, out var current))
        {
            var updated = current with { Title = title, IsCompleted = isCompleted };
            if (_items.TryUpdate(id, updated, current)) return updated;
        }
        return null;
    }

    public bool Delete(int id) => _items.TryRemove(id, out _);
}