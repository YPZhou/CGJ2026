using System;
using System.Collections.Generic;

using Godot;

public sealed class MoveCard
{
    public readonly struct SubPoolEntry
    {
        public SubPoolEntry(string name, int weight, int baseMove, int turnDelta)
        {
            Name = name;
            Weight = weight;
            BaseMove = baseMove;
            TurnDelta = turnDelta;
        }

        public string Name { get; }

        public int Weight { get; }

        public int BaseMove { get; }

        public int TurnDelta { get; }
    }

    public static readonly SubPoolEntry[] StraightPool =
    {
        new("直航", 3, 2, 0),
        new("紧急加速", 1, 3, 0),
    };

    public static readonly SubPoolEntry[] LeftPool =
    {
        new("左满舵", 4, 2, -1),
        new("左漂移", 1, 3, -1),
    };

    public static readonly SubPoolEntry[] RightPool =
    {
        new("右满舵", 4, 2, +1),
        new("右漂移", 1, 3, +1),
    };

    public string Name { get; init; } = string.Empty;

    public int Weight { get; init; }

    public int BaseMove { get; init; }

    public int TurnDelta { get; init; }

    public int MoveBonus { get; set; }

    public bool IsDriftVariant { get; init; }

    public int TotalMove => BaseMove + MoveBonus;

    public string Summary
    {
        get
        {
            if (TurnDelta == 0)
            {
                return $"移动 {TotalMove}";
            }

            var turnText = TurnDelta > 0 ? "右转" : "左转";
            return $"{turnText}后移动 {TotalMove}";
        }
    }

    public static MoveCard DrawFromPool(SubPoolEntry[] pool, int moveBonus, RandomNumberGenerator random)
    {
        var totalWeight = 0;
        foreach (var entry in pool)
        {
            totalWeight += entry.Weight;
        }

        var roll = random.RandiRange(1, totalWeight);
        foreach (var entry in pool)
        {
            roll -= entry.Weight;
            if (roll > 0)
            {
                continue;
            }

            return new MoveCard
            {
                Name = entry.Name,
                Weight = entry.Weight,
                BaseMove = entry.BaseMove,
                TurnDelta = entry.TurnDelta,
                MoveBonus = moveBonus,
                IsDriftVariant = entry.Name is "左漂移" or "右漂移",
            };
        }

        throw new InvalidOperationException("Unable to draw a move card from pool.");
    }
}
