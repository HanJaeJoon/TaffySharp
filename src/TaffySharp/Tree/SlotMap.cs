namespace TaffySharp;

/// <summary>
/// A minimal generational slot map (the role the <c>slotmap</c> crate plays upstream): stale ids of
/// removed entries are detected instead of aliasing a reused slot.
/// </summary>
internal sealed class SlotMap<T>
{
    private readonly List<T?> _values;
    private readonly List<uint> _versions;
    private readonly List<bool> _occupied;
    private readonly Stack<int> _free = new();

    public SlotMap(int capacity)
    {
        _values = new List<T?>(capacity);
        _versions = new List<uint>(capacity);
        _occupied = new List<bool>(capacity);
    }

    public int Count { get; private set; }

    private static int IndexOf(NodeId id) => (int)(id.Value & 0xFFFF_FFFF);

    private static uint VersionOf(NodeId id) => (uint)(id.Value >> 32);

    public NodeId Insert(T value)
    {
        int index;
        if (_free.Count > 0)
        {
            index = _free.Pop();
            _values[index] = value;
            _occupied[index] = true;
        }
        else
        {
            index = _values.Count;
            _values.Add(value);
            _versions.Add(1);
            _occupied.Add(true);
        }

        Count++;
        return new NodeId(((ulong)_versions[index] << 32) | (uint)index);
    }

    public bool Contains(NodeId id)
    {
        int index = IndexOf(id);
        return index < _values.Count && _occupied[index] && _versions[index] == VersionOf(id);
    }

    public T this[NodeId id]
    {
        get => Contains(id) ? _values[IndexOf(id)]! : throw InvalidKey(id);
        set
        {
            if (!Contains(id))
            {
                throw InvalidKey(id);
            }

            _values[IndexOf(id)] = value;
        }
    }

    public bool TryGetValue(NodeId id, out T value)
    {
        if (Contains(id))
        {
            value = _values[IndexOf(id)]!;
            return true;
        }

        value = default!;
        return false;
    }

    public bool Remove(NodeId id)
    {
        if (!Contains(id))
        {
            return false;
        }

        int index = IndexOf(id);
        _values[index] = default;
        _occupied[index] = false;
        _versions[index]++;
        _free.Push(index);
        Count--;
        return true;
    }

    public void Clear()
    {
        for (int index = 0; index < _values.Count; index++)
        {
            if (_occupied[index])
            {
                _values[index] = default;
                _occupied[index] = false;
                _versions[index]++;
                _free.Push(index);
            }
        }

        Count = 0;
    }

    private static KeyNotFoundException InvalidKey(NodeId id) => new($"{id} is not in the TaffyTree instance");
}
