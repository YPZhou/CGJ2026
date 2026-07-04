public enum ProjectileOwner
{
    Player,
    Enemy,
}

public sealed class ProjectileState
{
    public ProjectileState(HexCoord coord, int direction, int damage, ProjectileOwner owner, bool hasExplosive)
    {
        Coord = coord;
        Direction = HexCoord.WrapDirection(direction);
        Damage = damage;
        Owner = owner;
        HasExplosive = hasExplosive;
    }

    public HexCoord Coord { get; set; }

    public int Direction { get; set; }

    public int Damage { get; set; }

    public ProjectileOwner Owner { get; }

    public bool HasExplosive { get; }
}