using System;

using Godot;

public readonly struct HexCoord : IEquatable<HexCoord>
{
    public static readonly HexCoord[] Directions =
    {
        new(0, -1, +1),
        new(+1, -1, 0),
        new(+1, 0, -1),
        new(0, +1, -1),
        new(-1, +1, 0),
        new(-1, 0, +1),
    };

    public int Q { get; }

    public int R { get; }

    public int S { get; }

    public HexCoord(int q, int r, int s)
    {
        if ((q + r + s) != 0)
        {
            throw new ArgumentException("Cube coordinates must satisfy q + r + s = 0.");
        }

        Q = q;
        R = r;
        S = s;
    }

    public HexCoord Step(int direction)
    {
        return this + Directions[WrapDirection(direction)];
    }

    public HexCoord Scale(int factor)
    {
        return new HexCoord(Q * factor, R * factor, S * factor);
    }

    public int DistanceTo(HexCoord other)
    {
        return Math.Max(Math.Abs(Q - other.Q), Math.Max(Math.Abs(R - other.R), Math.Abs(S - other.S)));
    }

    public static int WrapDirection(int direction)
    {
        return Mathf.PosMod(direction, Directions.Length);
    }

    public static HexCoord operator +(HexCoord left, HexCoord right)
    {
        return new HexCoord(left.Q + right.Q, left.R + right.R, left.S + right.S);
    }

    public static HexCoord operator -(HexCoord left, HexCoord right)
    {
        return new HexCoord(left.Q - right.Q, left.R - right.R, left.S - right.S);
    }

    public static HexCoord operator *(HexCoord value, int factor)
    {
        return value.Scale(factor);
    }

    public bool Equals(HexCoord other)
    {
        return Q == other.Q && R == other.R && S == other.S;
    }

    public override bool Equals(object? obj)
    {
        return obj is HexCoord other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Q, R, S);
    }

    public override string ToString()
    {
        return $"({Q}, {R}, {S})";
    }
}