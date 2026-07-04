public enum EnemyType
{
    Charger,
    Artillery,
    Mine,
    Splitter,
}

public sealed class EnemyState
{
    public EnemyState(EnemyType type, HexCoord coord, int spawnTurn)
    {
        Type = type;
        Coord = coord;
        SpawnTurn = spawnTurn;
        MaxHealth = GetDefaultHealth(type);
        Health = MaxHealth;
    }

    public EnemyType Type { get; }

    public HexCoord Coord { get; set; }

    public int SpawnTurn { get; }

    public int MaxHealth { get; }

    public int Health { get; set; }

    public int? MoveIntentDirection { get; set; }

    public int? AttackIntentDirection { get; set; }

    public bool HasRadialAttackIntent { get; set; }

    public bool IsAlive => Health > 0;

    private static int GetDefaultHealth(EnemyType type)
    {
        return type switch
        {
            EnemyType.Artillery => 2,
            EnemyType.Splitter => 3,
            _ => 1,
        };
    }
}