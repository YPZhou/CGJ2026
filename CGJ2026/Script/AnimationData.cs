using System;
using System.Collections.Generic;

using Godot;

public enum AnimKind
{
    FleetStep,
    EnemyMove,
    Pickup,
    CannonFire,
    ProjectileMove,
    Flash,
    Explosion,
}

public struct FleetStepAnim
{
    public List<Vector2> FromPositions;
    public List<Vector2> ToPositions;
}

public struct EntityMove
{
    public object EntityKey;
    public Vector2 FromWorld;
    public Vector2 ToWorld;
    public float Scale;
    public Color Tint;
}

public sealed class ActiveEffect
{
    public AnimKind Kind;
    public Vector2 Position;
    public float Elapsed;
    public float Duration;
    public float Radius;
    public Color Tint;
    public string FloatingText;
    public int Direction;
    public object? RecoilEntityKey;

    public float Progress => Mathf.Clamp(Elapsed / Duration, 0.0f, 1.0f);

    public bool IsExpired => Elapsed >= Duration;
}

public sealed class AnimationEvent
{
    public AnimKind Kind;
    public float Duration;
    public FleetStepAnim? FleetStep;
    public Vector2 WorldPosition;
    public float Radius;
    public Color Tint;
    public string FloatingText = string.Empty;
    public List<EntityMove>? EntityMoves;
    public int Direction;
    public object? CannonEntityKey;
    public int SegmentIndex = -1;
}
