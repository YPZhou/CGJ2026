using System;
using System.Collections.Generic;

using Godot;

public sealed class MoveCard
{
    public static readonly IReadOnlyList<MoveCardDefinition> Definitions =
        new List<MoveCardDefinition>
        {
            new("直航", 3, 1, 0, false),
            new("左满舵", 2, 1, +1, false),
            new("右满舵", 2, 1, -1, false),
            new("紧急加速", 1, 2, 0, false),
            new("漂移转向", 1, 2, 0, true),
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

            var turnText = TurnDelta > 0 ? "左转" : "右转";
            return $"{turnText}后移动 {TotalMove}";
        }
    }

    public MoveCard Clone()
    {
        return new MoveCard
        {
            Name = Name,
            Weight = Weight,
            BaseMove = BaseMove,
            TurnDelta = TurnDelta,
            MoveBonus = MoveBonus,
            IsDriftVariant = IsDriftVariant,
        };
    }

    public static MoveCard Draw(RandomNumberGenerator random)
    {
        var totalWeight = 0;
        foreach (var definition in Definitions)
        {
            totalWeight += definition.Weight;
        }

        var roll = random.RandiRange(1, totalWeight);
        foreach (var definition in Definitions)
        {
            roll -= definition.Weight;
            if (roll > 0)
            {
                continue;
            }

            if (definition.IsDrift)
            {
                var driftLeft = random.Randf() < 0.5f;
                return new MoveCard
                {
                    Name = driftLeft ? "左漂移" : "右漂移",
                    Weight = definition.Weight,
                    BaseMove = definition.BaseMove,
                    TurnDelta = driftLeft ? +1 : -1,
                    MoveBonus = 0,
                    IsDriftVariant = true,
                };
            }

            return new MoveCard
            {
                Name = definition.Name,
                Weight = definition.Weight,
                BaseMove = definition.BaseMove,
                TurnDelta = definition.TurnDelta,
                MoveBonus = 0,
                IsDriftVariant = false,
            };
        }

        throw new InvalidOperationException("Unable to draw a move card.");
    }
}

public readonly struct MoveCardDefinition
{
    public MoveCardDefinition(string name, int weight, int baseMove, int turnDelta, bool isDrift)
    {
        Name = name;
        Weight = weight;
        BaseMove = baseMove;
        TurnDelta = turnDelta;
        IsDrift = isDrift;
    }

    public string Name { get; }

    public int Weight { get; }

    public int BaseMove { get; }

    public int TurnDelta { get; }

    public bool IsDrift { get; }
}