namespace Temp.Schema.FuchsiaTraceFormat;

sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    readonly int _capacity = capacity;
    readonly Dictionary<TKey, (TValue value, LinkedListNode<TKey> node)> _map = new(capacity);
    readonly LinkedList<TKey> _order = new();

    public int Count => _map.Count;
    public int Capacity => _capacity;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var entry))
        {
            _order.Remove(entry.node);
            _order.AddFirst(entry.node);
            value = entry.value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
            _order.Remove(existing.node);

        var node = new LinkedListNode<TKey>(key);
        _order.AddFirst(node);
        _map[key] = (value, node);

        if (_map.Count > _capacity) PopLru();
    }

    public KeyValuePair<TKey, TValue> PopLru()
    {
        var last = _order.Last ?? throw new InvalidOperationException("LRU empty");
        var key = last.Value;
        var value = _map[key].value;
        _order.RemoveLast();
        _map.Remove(key);
        return new KeyValuePair<TKey, TValue>(key, value);
    }
}
