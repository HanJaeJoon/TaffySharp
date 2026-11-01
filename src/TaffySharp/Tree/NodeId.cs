namespace TaffySharp;

/// <summary>A type representing the id of a single node in a tree of nodes.</summary>
public readonly struct NodeId : IEquatable<NodeId>
{
    public NodeId(ulong value) => Value = value;

    public ulong Value { get; }

    public static explicit operator NodeId(ulong value) => new(value);

    public static explicit operator ulong(NodeId id) => id.Value;

    public bool Equals(NodeId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is NodeId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(NodeId a, NodeId b) => a.Value == b.Value;

    public static bool operator !=(NodeId a, NodeId b) => a.Value != b.Value;

    public override string ToString() => $"NodeId({Value})";
}
