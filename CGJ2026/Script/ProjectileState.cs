public enum ProjectileOwner
{
    Player,
    Enemy,
}

public sealed class ProjectileState
{
    public ProjectileState(HexCoord coord, int direction, int damage, ProjectileOwner owner, bool hasExplosive,
        float realtimeMoveIntervalSeconds = 0.0f)
    {
        Coord = coord;
        Direction = HexCoord.WrapDirection(direction);
        Damage = damage;
        Owner = owner;
        HasExplosive = hasExplosive;
        RealtimeMoveIntervalSeconds = realtimeMoveIntervalSeconds;
        RealtimeMoveCooldownSeconds = realtimeMoveIntervalSeconds;
    }

    public HexCoord Coord { get; set; }

    public int Direction { get; set; }

    public int Damage { get; set; }

    public ProjectileOwner Owner { get; }

    public bool HasExplosive { get; }

    public float RealtimeMoveIntervalSeconds { get; }

    public float RealtimeMoveCooldownSeconds { get; set; }
}