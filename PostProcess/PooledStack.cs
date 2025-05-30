#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PostProcess;

/// <summary>
/// A stack with snapshot capability, reference-counted nodes, and weak-ref pooling to reduce GC pressure.
/// </summary>
public class PooledStack<T>
{
    private Node? _top;

    /// <summary>
    /// Pushes a value onto the stack.
    /// </summary>
    public void Push(T value)
    {
        var next = _top;
        var node = Node.Rent(value, next);
        if (next is not null)
            next.AddRef();
        _top = node;
    }

    /// <summary>
    /// Pops a value from the stack.
    /// </summary>
    public T Pop()
    {
        if (_top is null)
            throw new InvalidOperationException("Stack is empty.");

        var node = _top;
        var result = node.Value!;
        var next = node.Next;

        node.Release();
        if (next is not null)
            next.Release();

        _top = next;
        return result;
    }

    /// <summary>
    /// Creates a snapshot of the current stack state.
    /// </summary>
    public Snapshot CreateSnapshot()
    {
        if (_top is null)
            return new Snapshot(null);

        // Check if we already have a cached snapshot for this node
        var cachedSnapshot = _top.GetCachedSnapshot();
        if (cachedSnapshot is not null && !cachedSnapshot._released)
        {
            cachedSnapshot.AddRef();
            return cachedSnapshot;
        }

        // Create new snapshot and cache it
        var newSnapshot = new Snapshot(_top);
        _top.CacheSnapshot(newSnapshot);
        return newSnapshot;
    }

    /// <summary>
    /// Represents a reference-counted snapshot of the stack.
    /// Call Release() when done to allow pooling.
    /// </summary>
    public class Snapshot
    {
        internal Node? _root;
        internal bool _released;
        private int _refCount;

        internal Snapshot(Node? root)
        {
            _root = root;
            _refCount = 1;
            if (_root is not null)
                _root.AddRef();
        }

        /// <summary>
        /// Increment reference count for shared snapshot usage.
        /// </summary>
        internal void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        /// <summary>
        /// Gets an enumerator over snapshot values (top-first).
        /// </summary>
        public IEnumerable<T> Values
        {
            get
            {
                var current = _root;
                while (current is not null)
                {
                    yield return current.Value!;
                    current = current.Next;
                }
            }
        }

        /// <summary>
        /// Release the snapshot, decrementing ref counts and pooling nodes when possible.
        /// </summary>
        public void Release()
        {
            if (_released)
                return;

            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                if (_root is not null)
                    _root.Release();

                _root = null;
                _released = true;
            }
        }
    }

    /// <summary>
    /// Internal node class with reference counting and weak-ref pooling.
    /// </summary>
    internal class Node
    {
        private static readonly ConcurrentBag<WeakReference<Node>> _pool = new();
        private int _refCount;
        private WeakReference<Snapshot>? _cachedSnapshot;

        public T? Value;
        public Node? Next;

        private Node() { }

        /// <summary>
        /// Rent a node from the pool or create a new one.
        /// </summary>
        public static Node Rent(T value, Node? next)
        {
            Node? node = null;
            while (_pool.TryTake(out var weak))
            {
                if (weak.TryGetTarget(out node))
                    break;
                node = null;
            }
            node ??= new Node();

            node.Value = value;
            node.Next = next;
            node._refCount = 1;
            return node;
        }

        /// <summary>
        /// Increment reference count.
        /// </summary>
        public void AddRef() => Interlocked.Increment(ref _refCount);

        /// <summary>
        /// Decrement reference count and recycle when zero.
        /// </summary>
        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
                Reclaim();
        }

        /// <summary>
        /// Get or create cached snapshot for this node.
        /// </summary>
        public Snapshot? GetCachedSnapshot()
        {
            if (_cachedSnapshot?.TryGetTarget(out var snapshot) == true)
                return snapshot;
            return null;
        }

        /// <summary>
        /// Cache a snapshot for this node.
        /// </summary>
        public void CacheSnapshot(Snapshot snapshot)
        {
            _cachedSnapshot = new WeakReference<Snapshot>(snapshot);
        }

        /// <summary>
        /// Recursively reclaim this node and its successors if they have no other references.
        /// </summary>
        private void Reclaim()
        {
            var next = Next;
            Next = null;
            Value = default;
            _cachedSnapshot = null;

            if (next is not null)
                next.Release();

            _pool.Add(new WeakReference<Node>(this));
        }
    }
}
