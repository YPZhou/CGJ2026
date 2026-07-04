public sealed class EnemyState
{
    public EnemyState(HexCoord coord, int fireDirection, int health = 1)
    {
        Coord = coord;
        FireDirection = HexCoord.WrapDirection(fireDirection);
        Health = health;
    }

    public HexCoord Coord { get; set; }

    public int FireDirection { get; set; }

    public int Health { get; set; }

    public bool IsAlive => Health > 0;
}