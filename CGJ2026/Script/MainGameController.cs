using System;
using System.Collections.Generic;

using Godot;

public partial class MainGameController : Node2D
{
    private const int InitialHandSize = 3;
    private const int MinimumReefClusters = 3;
    private const int MaximumReefClusters = 5;
    private const int MinimumReefSize = 1;
    private const int MaximumReefSize = 3;
    private const int ReefSafeDistance = 3;
    private const float BaseViewportWidth = 1280.0f;
    private const float BaseViewportHeight = 720.0f;
    private const float BoardMargin = 36.0f;
    private const float BoardTopInset = 96.0f;
    private const float BoardBottomInset = 220.0f;
    private const float MinimumUiScale = 0.35f;
    private const float MaximumUiScale = 1.5f;
    private const float MinimumHexSize = 4.0f;
    private const float MaximumHexSize = 34.0f;

    private static readonly HexCoord Origin = new(0, 0, 0);

    private readonly RandomNumberGenerator _random = new();
    private readonly List<Button> _cardButtons = new();
    private readonly List<HexCoord> _drawOrderedCells = new();
    private readonly List<MoveCard> _hand = new();
    private readonly List<EnemyState> _enemies = new();
    private readonly List<ProjectileState> _playerProjectiles = new();
    private readonly List<ProjectileState> _enemyProjectiles = new();

    private Texture2D _placeholder = null!;
    private HexGrid _grid = null!;
    private FleetState _fleet = null!;
    private HashSet<HexCoord> _reefCells = new();
    private SimulationResult? _preview;
    private MoveCard? _selectedCard;
    private int _selectedCardIndex = -1;
    private bool _isGameOver;
    private string _gameOverReason = string.Empty;
    private string _lastCombatEvent = string.Empty;
    private int _turnCounter = 1;
    private float _hexSize = 16.0f;
    private Vector2 _boardOrigin;
    private Vector2 _baseBoundsMin;
    private Vector2 _baseBoundsMax;
    private float _uiScale = 1.0f;

    private MarginContainer _hudMargin = null!;
    private VBoxContainer _hudVBox = null!;
    private HBoxContainer _cardsRow = null!;
    private Label _statusLabel = null!;
    private Label _hintLabel = null!;
    private Label _combatLabel = null!;
    private Button _confirmButton = null!;
    private float _baseHudMargin;
    private int _baseHudSeparation;
    private int _baseCardsRowSeparation;
    private Vector2 _baseCardButtonSize;
    private Vector2 _baseConfirmButtonSize;
    private int _baseStatusFontSize;
    private int _baseHintFontSize;
    private int _baseCombatFontSize;
    private int _baseCardFontSize;
    private int _baseConfirmFontSize;

    public override void _Ready()
    {
        _placeholder = GD.Load<Texture2D>("res://icon.svg");
        ResolveHudNodes();
        HookHudEvents();

        _random.Randomize();
        StartNewDemo();

        GetViewport().SizeChanged += OnViewportSizeChanged;
    }

    public override void _ExitTree()
    {
        if (IsInsideTree())
        {
            GetViewport().SizeChanged -= OnViewportSizeChanged;
        }
    }

    public override void _Draw()
    {
        if (_grid == null)
        {
            return;
        }

        DrawBoard();
        DrawReefs();
        DrawProjectiles(_enemyProjectiles, new Color(0.94f, 0.46f, 0.22f, 0.95f), 0.46f);
        DrawProjectiles(_playerProjectiles, new Color(0.98f, 0.86f, 0.32f, 0.95f), 0.42f);
        DrawEnemies();

        if (_preview != null)
        {
            DrawPreview(_preview);
        }

        DrawFleet(_fleet, isGhost: false, alpha: 1.0f);
    }

    private void ResolveHudNodes()
    {
        _hudMargin = GetNode<MarginContainer>("HudLayer/HudMargin");
        _hudVBox = GetNode<VBoxContainer>("HudLayer/HudMargin/HudVBox");
        _cardsRow = GetNode<HBoxContainer>("HudLayer/HudMargin/HudVBox/CardsRow");
        _statusLabel = GetNode<Label>("HudLayer/HudMargin/HudVBox/StatusLabel");
        _hintLabel = GetNode<Label>("HudLayer/HudMargin/HudVBox/HintLabel");
        _combatLabel = GetNode<Label>("HudLayer/HudMargin/HudVBox/CombatLabel");
        _confirmButton = GetNode<Button>("HudLayer/HudMargin/HudVBox/ConfirmButton");

        _cardButtons.Clear();
        _cardButtons.Add(GetNode<Button>("HudLayer/HudMargin/HudVBox/CardsRow/CardButton0"));
        _cardButtons.Add(GetNode<Button>("HudLayer/HudMargin/HudVBox/CardsRow/CardButton1"));
        _cardButtons.Add(GetNode<Button>("HudLayer/HudMargin/HudVBox/CardsRow/CardButton2"));

        _baseHudMargin = Mathf.Abs(_hudMargin.OffsetLeft);
        _baseHudSeparation = _hudVBox.GetThemeConstant("separation");
        _baseCardsRowSeparation = _cardsRow.GetThemeConstant("separation");
        _baseCardButtonSize = _cardButtons[0].CustomMinimumSize;
        _baseConfirmButtonSize = _confirmButton.CustomMinimumSize;
        _baseStatusFontSize = _statusLabel.GetThemeFontSize("font_size");
        _baseHintFontSize = _hintLabel.GetThemeFontSize("font_size");
        _baseCombatFontSize = _combatLabel.GetThemeFontSize("font_size");
        _baseCardFontSize = _cardButtons[0].GetThemeFontSize("font_size");
        _baseConfirmFontSize = _confirmButton.GetThemeFontSize("font_size");
    }

    private void HookHudEvents()
    {
        for (var index = 0; index < _cardButtons.Count; index++)
        {
            var cardIndex = index;
            _cardButtons[index].Pressed += () => SelectCard(cardIndex);
        }

        _confirmButton.Pressed += OnConfirmPressed;
    }

    private void StartNewDemo()
    {
        _grid = new HexGrid(HexGrid.DefaultRadius);
        BuildDrawOrder();

        FleetState? fleet = null;
        HashSet<HexCoord>? reefs = null;
        for (var attempt = 0; attempt < 96; attempt++)
        {
            var candidateFleet = CreateInitialFleet();
            var reservedCells = BuildInitialReserve(candidateFleet);

            try
            {
                var clusterCount = _random.RandiRange(MinimumReefClusters, MaximumReefClusters);
                var candidateReefs = _grid.CreateConnectedReefs(
                    _random,
                    reservedCells,
                    clusterCount,
                    MinimumReefSize,
                    MaximumReefSize);

                fleet = candidateFleet;
                reefs = candidateReefs;
                break;
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (fleet == null || reefs == null)
        {
            throw new InvalidOperationException("Unable to create a valid opening map state.");
        }

        _fleet = fleet;
        _reefCells = reefs;
        _enemies.Clear();
        _playerProjectiles.Clear();
        _enemyProjectiles.Clear();
        SeedCombatDemo();
        _hand.Clear();
        FillHand();
        ClearSelection();
        _isGameOver = false;
        _gameOverReason = string.Empty;
        _lastCombatEvent = "战斗就绪。";
        _turnCounter = 1;
        UpdateEnemyFireDirections();

        RefreshResponsiveLayout();
        UpdateHudState();
        QueueRedraw();
    }

    private FleetState CreateInitialFleet()
    {
        var nearbyCells = new List<HexCoord>();
        foreach (var cell in _grid.LegalCells)
        {
            if (cell.DistanceTo(Origin) <= 2)
            {
                nearbyCells.Add(cell);
            }
        }

        if (nearbyCells.Count == 0)
        {
            throw new InvalidOperationException("No valid near-center spawn cells were found.");
        }

        var head = nearbyCells[_random.RandiRange(0, nearbyCells.Count - 1)];
        var headDirection = _random.RandiRange(0, 5);
        var body = head.Step(HexCoord.WrapDirection(headDirection + 3));

        if (!_grid.Contains(body))
        {
            throw new InvalidOperationException("Initial body placement fell outside the board.");
        }

        var segments = new List<FleetSegmentState>
        {
            new(head)
            {
                EntryDirection = headDirection,
            },
            new(body)
            {
                EntryDirection = headDirection,
            },
        };

        return new FleetState(segments, headDirection);
    }

    private HashSet<HexCoord> BuildInitialReserve(FleetState fleet)
    {
        var reservedCells = new HashSet<HexCoord>();

        foreach (var cell in _grid.LegalCells)
        {
            foreach (var segment in fleet.Segments)
            {
                if (cell.DistanceTo(segment.Coord) <= ReefSafeDistance)
                {
                    reservedCells.Add(cell);
                    break;
                }
            }
        }

        return reservedCells;
    }

    private void BuildDrawOrder()
    {
        _drawOrderedCells.Clear();
        foreach (var cell in _grid.LegalCells)
        {
            _drawOrderedCells.Add(cell);
        }

        _drawOrderedCells.Sort((left, right) =>
        {
            var compareR = left.R.CompareTo(right.R);
            return compareR != 0 ? compareR : left.Q.CompareTo(right.Q);
        });

        _baseBoundsMin = new Vector2(float.MaxValue, float.MaxValue);
        _baseBoundsMax = new Vector2(float.MinValue, float.MinValue);

        foreach (var cell in _drawOrderedCells)
        {
            var position = _grid.ToWorld(cell, 1.0f);
            _baseBoundsMin = new Vector2(Mathf.Min(_baseBoundsMin.X, position.X), Mathf.Min(_baseBoundsMin.Y, position.Y));
            _baseBoundsMax = new Vector2(Mathf.Max(_baseBoundsMax.X, position.X), Mathf.Max(_baseBoundsMax.Y, position.Y));
        }
    }

    private void SeedCombatDemo()
    {
        var head = _fleet.Head;
        var headDirection = _fleet.HeadDirection;

        AddDemoEnemy(
            head + (HexCoord.Directions[headDirection] * 4),
            head + (HexCoord.Directions[HexCoord.WrapDirection(headDirection + 1)] * 4),
            head + (HexCoord.Directions[HexCoord.WrapDirection(headDirection - 1)] * 4));

        AddDemoEnemy(
            head + (HexCoord.Directions[HexCoord.WrapDirection(headDirection + 2)] * 5),
            head + (HexCoord.Directions[HexCoord.WrapDirection(headDirection - 2)] * 5),
            head + (HexCoord.Directions[HexCoord.WrapDirection(headDirection + 3)] * 6));
    }

    private void AddDemoEnemy(params HexCoord[] preferredCells)
    {
        foreach (var cell in preferredCells)
        {
            if (!IsCombatCellOpen(cell))
            {
                continue;
            }

            _enemies.Add(new EnemyState(cell, GetDirectionToward(cell, _fleet.Head)));
            return;
        }

        foreach (var cell in _drawOrderedCells)
        {
            var distance = cell.DistanceTo(_fleet.Head);
            if (distance < 4 || distance > 7 || !IsCombatCellOpen(cell))
            {
                continue;
            }

            _enemies.Add(new EnemyState(cell, GetDirectionToward(cell, _fleet.Head)));
            return;
        }
    }

    private bool IsCombatCellOpen(HexCoord cell)
    {
        return _grid.Contains(cell)
            && !_reefCells.Contains(cell)
            && !_fleet.Occupies(cell)
            && !HasEnemyAt(cell)
            && !HasAnyProjectileAt(cell);
    }

    private void UpdateEnemyFireDirections()
    {
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            enemy.FireDirection = GetDirectionToward(enemy.Coord, _fleet.Head);
        }
    }

    private int GetDirectionToward(HexCoord from, HexCoord target)
    {
        var bestDirection = 0;
        var bestDistance = int.MaxValue;

        for (var direction = 0; direction < HexCoord.Directions.Length; direction++)
        {
            var candidateDistance = from.Step(direction).DistanceTo(target);
            if (candidateDistance >= bestDistance)
            {
                continue;
            }

            bestDistance = candidateDistance;
            bestDirection = direction;
        }

        return bestDirection;
    }

    private void FillHand()
    {
        while (_hand.Count < InitialHandSize)
        {
            _hand.Add(MoveCard.Draw(_random));
        }
    }

    private void SelectCard(int index)
    {
        if (_isGameOver || index < 0 || index >= _hand.Count)
        {
            return;
        }

        _selectedCardIndex = index;
        _selectedCard = _hand[index];
        _preview = SimulateCard(_fleet, _selectedCard);

        UpdateHudState();
        QueueRedraw();
    }

    private void OnConfirmPressed()
    {
        if (_isGameOver || _selectedCard == null || _selectedCardIndex < 0)
        {
            return;
        }

        var result = SimulateCard(_fleet, _selectedCard);
        if (result.IsFatal)
        {
            _preview = result;
            _isGameOver = true;
            _gameOverReason = DescribeCollision(result.CollisionKind);
            _lastCombatEvent = "舰队在移动阶段沉没。";
            UpdateHudState();
            QueueRedraw();
            return;
        }

        _fleet = result.Fleet;
        _hand.RemoveAt(_selectedCardIndex);
        ClearSelection();

        var combatSummaries = new List<string>
        {
            ResolvePlayerFire(),
        };

        if (!_isGameOver)
        {
            combatSummaries.Add(FireDemoEnemies());
        }

        if (!_isGameOver)
        {
            combatSummaries.Add(AdvanceProjectiles());
        }

        _lastCombatEvent = BuildCombatSummary(combatSummaries);

        if (!_isGameOver)
        {
            _turnCounter++;
            if (_hand.Count == 0)
            {
                FillHand();
            }

            UpdateEnemyFireDirections();
        }

        UpdateHudState();
        QueueRedraw();
    }

    private void ClearSelection()
    {
        _preview = null;
        _selectedCard = null;
        _selectedCardIndex = -1;
    }

    private SimulationResult SimulateCard(FleetState source, MoveCard card)
    {
        var simulated = source.Clone();
        var headPath = new List<HexCoord>();

        for (var stepIndex = 0; stepIndex < card.TotalMove; stepIndex++)
        {
            if (stepIndex == 0 && card.TurnDelta != 0)
            {
                simulated.HeadDirection = _grid.RotateDirection(simulated.HeadDirection, card.TurnDelta);
            }

            var nextHead = simulated.Head.Step(simulated.HeadDirection);
            headPath.Add(nextHead);

            var collision = CheckHeadEntry(nextHead, simulated);
            if (collision != CollisionKind.None)
            {
                return SimulationResult.Fatal(simulated, headPath, collision, nextHead);
            }

            simulated.MoveOneStep(nextHead, _grid);
            if (simulated.HasDuplicateCoords())
            {
                return SimulationResult.Fatal(simulated, headPath, CollisionKind.Self, nextHead);
            }
        }

        return SimulationResult.Success(simulated, headPath);
    }

    private CollisionKind CheckHeadEntry(HexCoord target, FleetState fleet)
    {
        if (!_grid.Contains(target))
        {
            return CollisionKind.OutOfBounds;
        }

        if (_reefCells.Contains(target))
        {
            return CollisionKind.Reef;
        }

        if (HasEnemyAt(target))
        {
            return CollisionKind.EnemyShip;
        }

        if (HasEnemyProjectileAt(target))
        {
            return CollisionKind.EnemyProjectile;
        }

        if (fleet.Occupies(target))
        {
            return CollisionKind.Self;
        }

        return CollisionKind.None;
    }

    private string ResolvePlayerFire()
    {
        var directHits = 0;
        var cancelledProjectiles = 0;
        var spawnedProjectiles = 0;
        var enemyCoords = new HashSet<HexCoord>();
        var enemyProjectileCoords = new HashSet<HexCoord>();

        foreach (var enemy in _enemies)
        {
            if (enemy.IsAlive)
            {
                enemyCoords.Add(enemy.Coord);
            }
        }

        foreach (var projectile in _enemyProjectiles)
        {
            enemyProjectileCoords.Add(projectile.Coord);
        }

        for (var index = 0; index < _fleet.Segments.Count; index++)
        {
            var segment = _fleet.Segments[index];
            var baseDirection = index == 0 ? _fleet.HeadDirection : segment.EntryDirection;
            var fireDirections = BuildFireDirections(baseDirection, segment.ScatterLevel);

            foreach (var fireDirection in fireDirections)
            {
                switch (TryResolveImmediateShot(segment, fireDirection, enemyCoords, enemyProjectileCoords))
                {
                    case ShotResolution.DirectHit:
                        directHits++;
                        break;
                    case ShotResolution.CancelledProjectile:
                        cancelledProjectiles++;
                        break;
                    case ShotResolution.SpawnedProjectile:
                        spawnedProjectiles++;
                        break;
                }
            }
        }

        var destroyedEnemies = RemoveDefeatedEnemies();
        if (directHits == 0 && cancelledProjectiles == 0 && spawnedProjectiles == 0)
        {
            return "玩家齐射未形成有效火线。";
        }

        var destroyedText = destroyedEnemies > 0
            ? $" 击沉 {destroyedEnemies} 艘敌舰。"
            : string.Empty;
        return $"玩家齐射: 命中 {directHits}，拦截 {cancelledProjectiles}，出膛 {spawnedProjectiles}。{destroyedText}";
    }

    private ShotResolution TryResolveImmediateShot(FleetSegmentState segment, int fireDirection, HashSet<HexCoord> enemyCoords, HashSet<HexCoord> enemyProjectileCoords)
    {
        var origin = segment.Coord;
        var firstCell = origin.Step(fireDirection);

        if (!_grid.Contains(firstCell) || _reefCells.Contains(firstCell) || _fleet.Occupies(firstCell))
        {
            return ShotResolution.None;
        }

        var current = firstCell;
        for (var step = 1; step <= segment.Range; step++)
        {
            if (!_grid.Contains(current) || _reefCells.Contains(current))
            {
                return ShotResolution.None;
            }

            if (enemyCoords.Contains(current))
            {
                var target = FindEnemyAt(current, includeDefeated: true);
                if (target != null)
                {
                    ApplyDamageToEnemy(target, segment.Damage, segment.HasExplosive);
                }

                return ShotResolution.DirectHit;
            }

            if (enemyProjectileCoords.Contains(current))
            {
                enemyProjectileCoords.Remove(current);
                RemoveEnemyProjectilesAt(current);
                return ShotResolution.CancelledProjectile;
            }

            if (step == segment.Range)
            {
                _playerProjectiles.Add(new ProjectileState(firstCell, fireDirection, segment.Damage, ProjectileOwner.Player, segment.HasExplosive));
                return ShotResolution.SpawnedProjectile;
            }

            current = current.Step(fireDirection);
        }

        return ShotResolution.None;
    }

    private List<int> BuildFireDirections(int baseDirection, int scatterLevel)
    {
        var maxOffset = scatterLevel switch
        {
            >= 2 => 2,
            1 => 1,
            _ => 0,
        };
        var directions = new List<int>();

        for (var offset = -maxOffset; offset <= maxOffset; offset++)
        {
            directions.Add(HexCoord.WrapDirection(baseDirection + offset));
        }

        return directions;
    }

    private void ApplyDamageToEnemy(EnemyState enemy, int damage, bool hasExplosive)
    {
        enemy.Health -= damage;

        if (hasExplosive)
        {
            ApplySplashDamage(enemy.Coord);
        }
    }

    private void ApplySplashDamage(HexCoord center)
    {
        foreach (var cell in _grid.GetNeighbors(center))
        {
            var enemy = FindEnemyAt(cell);
            if (enemy == null)
            {
                continue;
            }

            enemy.Health -= 1;
        }
    }

    private string FireDemoEnemies()
    {
        var spawned = 0;

        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            var spawnCoord = enemy.Coord.Step(enemy.FireDirection);
            if (!_grid.Contains(spawnCoord) || _reefCells.Contains(spawnCoord) || HasEnemyAt(spawnCoord) || HasAnyProjectileAt(spawnCoord))
            {
                continue;
            }

            if (_fleet.Occupies(spawnCoord))
            {
                _isGameOver = true;
                _gameOverReason = "被敌方炮火击中";
                return "敌舰近距开火命中舰队。";
            }

            _enemyProjectiles.Add(new ProjectileState(spawnCoord, enemy.FireDirection, 1, ProjectileOwner.Enemy, false));
            spawned++;
        }

        return spawned == 0
            ? "敌舰本回合未形成有效射线。"
            : $"敌舰发射 {spawned} 枚炮弹。";
    }

    private string AdvanceProjectiles()
    {
        var movedPlayerProjectiles = new Dictionary<HexCoord, List<ProjectileState>>();
        var movedEnemyProjectiles = new Dictionary<HexCoord, List<ProjectileState>>();
        var expiredProjectiles = 0;
        var playerHits = 0;
        var projectileDuels = 0;

        foreach (var projectile in _playerProjectiles)
        {
            if (!TryAdvanceProjectile(projectile, movedPlayerProjectiles))
            {
                expiredProjectiles++;
            }
        }

        foreach (var projectile in _enemyProjectiles)
        {
            if (!TryAdvanceProjectile(projectile, movedEnemyProjectiles))
            {
                expiredProjectiles++;
            }
        }

        _playerProjectiles.Clear();
        _enemyProjectiles.Clear();

        var processedCells = new HashSet<HexCoord>();
        foreach (var cell in movedPlayerProjectiles.Keys)
        {
            processedCells.Add(cell);
        }

        foreach (var cell in movedEnemyProjectiles.Keys)
        {
            processedCells.Add(cell);
        }

        foreach (var cell in processedCells)
        {
            var hasPlayerProjectile = movedPlayerProjectiles.TryGetValue(cell, out var playerProjectilesAtCell);
            var hasEnemyProjectile = movedEnemyProjectiles.TryGetValue(cell, out var enemyProjectilesAtCell);

            if (hasPlayerProjectile && hasEnemyProjectile)
            {
                projectileDuels++;
                continue;
            }

            if (hasPlayerProjectile)
            {
                var enemy = FindEnemyAt(cell, includeDefeated: true);
                if (enemy != null)
                {
                    foreach (var projectile in playerProjectilesAtCell!)
                    {
                        ApplyDamageToEnemy(enemy, projectile.Damage, projectile.HasExplosive);
                        playerHits++;
                    }

                    continue;
                }

                foreach (var projectile in playerProjectilesAtCell!)
                {
                    _playerProjectiles.Add(projectile);
                }

                continue;
            }

            if (!hasEnemyProjectile)
            {
                continue;
            }

            if (_fleet.Occupies(cell))
            {
                _isGameOver = true;
                _gameOverReason = "被敌方炮火击中";
                continue;
            }

            foreach (var projectile in enemyProjectilesAtCell!)
            {
                _enemyProjectiles.Add(projectile);
            }
        }

        var destroyedEnemies = RemoveDefeatedEnemies();
        if (_isGameOver)
        {
            return "敌方炮弹命中舰队。";
        }

        if (playerHits == 0 && projectileDuels == 0 && expiredProjectiles == 0 && destroyedEnemies == 0)
        {
            return "炮弹阶段未发生额外命中。";
        }

        var destroyedText = destroyedEnemies > 0
            ? $" 击沉 {destroyedEnemies} 艘敌舰。"
            : string.Empty;
        return $"炮弹推进: 命中 {playerHits}，对撞 {projectileDuels}，消散 {expiredProjectiles}。{destroyedText}";
    }

    private bool TryAdvanceProjectile(ProjectileState projectile, Dictionary<HexCoord, List<ProjectileState>> destination)
    {
        var nextCoord = projectile.Coord.Step(projectile.Direction);
        if (!_grid.Contains(nextCoord) || _reefCells.Contains(nextCoord))
        {
            return false;
        }

        projectile.Coord = nextCoord;
        if (!destination.TryGetValue(nextCoord, out var bucket))
        {
            bucket = new List<ProjectileState>();
            destination.Add(nextCoord, bucket);
        }

        bucket.Add(projectile);
        return true;
    }

    private int RemoveDefeatedEnemies()
    {
        var defeated = 0;
        for (var index = _enemies.Count - 1; index >= 0; index--)
        {
            if (_enemies[index].IsAlive)
            {
                continue;
            }

            _enemies.RemoveAt(index);
            defeated++;
        }

        return defeated;
    }

    private EnemyState? FindEnemyAt(HexCoord coord, bool includeDefeated = false)
    {
        foreach (var enemy in _enemies)
        {
            if (!enemy.Coord.Equals(coord))
            {
                continue;
            }

            if (includeDefeated || enemy.IsAlive)
            {
                return enemy;
            }
        }

        return null;
    }

    private bool HasEnemyAt(HexCoord coord)
    {
        return FindEnemyAt(coord) != null;
    }

    private bool HasEnemyProjectileAt(HexCoord coord)
    {
        foreach (var projectile in _enemyProjectiles)
        {
            if (projectile.Coord.Equals(coord))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyProjectileAt(HexCoord coord)
    {
        foreach (var projectile in _playerProjectiles)
        {
            if (projectile.Coord.Equals(coord))
            {
                return true;
            }
        }

        return HasEnemyProjectileAt(coord);
    }

    private void RemoveEnemyProjectilesAt(HexCoord coord)
    {
        for (var index = _enemyProjectiles.Count - 1; index >= 0; index--)
        {
            if (_enemyProjectiles[index].Coord.Equals(coord))
            {
                _enemyProjectiles.RemoveAt(index);
            }
        }
    }

    private static string BuildCombatSummary(List<string> summaries)
    {
        var filtered = new List<string>();
        foreach (var summary in summaries)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            filtered.Add(summary);
        }

        return filtered.Count == 0
            ? "本回合没有发生交火。"
            : string.Join(" ", filtered);
    }

    private void UpdateHudState()
    {
        UpdateStatusLabel();
        UpdateHintLabel();
        UpdateCombatLabel();
        UpdateCardButtons();
        _confirmButton.Disabled = _isGameOver || _selectedCard == null;
    }

    private void UpdateStatusLabel()
    {
        if (_isGameOver)
        {
            _statusLabel.Text = $"回合 {_turnCounter}  舰队长度 {_fleet.Segments.Count}/9\n舰队沉没: {_gameOverReason}";
            return;
        }

        var detail = "选择一张指令卡查看预览。";
        if (_selectedCard != null && _preview != null)
        {
            detail = _preview.IsFatal
                ? $"预览危险: {DescribeCollision(_preview.CollisionKind)}"
                : $"已选择 {_selectedCard.Name}: {_selectedCard.Summary}";
        }

        _statusLabel.Text = $"回合 {_turnCounter}  舰队长度 {_fleet.Segments.Count}/9\n{detail}";
    }

    private void UpdateHintLabel()
    {
        _hintLabel.Text = "当前演示实现碰撞与战斗核心。敌舰、敌弹、礁石、边界、自撞都会造成致命结果。";
    }

    private void UpdateCombatLabel()
    {
        var playerState = _isGameOver
            ? $"舰队状态: {_gameOverReason}"
            : "舰队状态: 作战中";
        _combatLabel.Text = $"敌舰 {_enemies.Count}  敌弹 {_enemyProjectiles.Count}  己弹 {_playerProjectiles.Count}\n{playerState}\n{_lastCombatEvent}";
    }

    private void UpdateCardButtons()
    {
        for (var index = 0; index < _cardButtons.Count; index++)
        {
            var button = _cardButtons[index];
            if (index >= _hand.Count)
            {
                button.Visible = false;
                continue;
            }

            var card = _hand[index];
            button.Visible = true;
            button.Disabled = _isGameOver;
            button.Icon = _placeholder;
            button.Text = BuildCardLabel(card, isSelected: index == _selectedCardIndex);
            button.Modulate = index == _selectedCardIndex
                ? new Color(1.0f, 0.92f, 0.75f, 1.0f)
                : Colors.White;
        }
    }

    private static string BuildCardLabel(MoveCard card, bool isSelected)
    {
        var prefix = isSelected ? "> " : string.Empty;
        return $"{prefix}{card.Name}\n{card.Summary}";
    }

    private void RefreshResponsiveLayout()
    {
        var viewportSize = GetViewportRect().Size;
        var viewportScale = Mathf.Min(viewportSize.X / BaseViewportWidth, viewportSize.Y / BaseViewportHeight);
        var rowWidthAtScaleOne = (_baseHudMargin * 2.0f)
            + (_baseCardButtonSize.X * _cardButtons.Count)
            + (_baseCardsRowSeparation * (_cardButtons.Count - 1));
        var rowScaleLimit = rowWidthAtScaleOne > 0.0f
            ? viewportSize.X / rowWidthAtScaleOne
            : 1.0f;

        _uiScale = Mathf.Clamp(Mathf.Min(viewportScale, rowScaleLimit), MinimumUiScale, MaximumUiScale);

        var hudMargin = Mathf.Round(_baseHudMargin * _uiScale);
        var hudSeparation = Mathf.Max(4, Mathf.RoundToInt(_baseHudSeparation * _uiScale));
        var cardsRowSeparation = Mathf.Max(4, Mathf.RoundToInt(_baseCardsRowSeparation * _uiScale));

        _hudMargin.OffsetLeft = hudMargin;
        _hudMargin.OffsetTop = hudMargin;
        _hudMargin.OffsetRight = -hudMargin;
        _hudMargin.OffsetBottom = -hudMargin;

        _hudVBox.AddThemeConstantOverride("separation", hudSeparation);
        _cardsRow.AddThemeConstantOverride("separation", cardsRowSeparation);

        var availableCardsWidth = Mathf.Max(
            _baseCardButtonSize.X * MinimumUiScale,
            (viewportSize.X - (hudMargin * 2.0f) - (cardsRowSeparation * (_cardButtons.Count - 1))) / _cardButtons.Count);
        var cardWidth = Mathf.Min(_baseCardButtonSize.X * _uiScale, availableCardsWidth);
        var cardHeight = Mathf.Max(36.0f, _baseCardButtonSize.Y * _uiScale);
        var cardFontSize = Mathf.Max(10, Mathf.RoundToInt(_baseCardFontSize * _uiScale));
        var cardIconWidth = Mathf.Max(12, Mathf.RoundToInt(cardHeight * 0.42f));

        foreach (var button in _cardButtons)
        {
            button.CustomMinimumSize = new Vector2(cardWidth, cardHeight);
            button.AddThemeFontSizeOverride("font_size", cardFontSize);
            button.AddThemeConstantOverride("icon_max_width", cardIconWidth);
            button.ExpandIcon = true;
        }

        _statusLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(12, Mathf.RoundToInt(_baseStatusFontSize * _uiScale)));
        _hintLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(10, Mathf.RoundToInt(_baseHintFontSize * _uiScale)));
    _combatLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(10, Mathf.RoundToInt(_baseCombatFontSize * _uiScale)));

        var confirmWidth = Mathf.Min(_baseConfirmButtonSize.X * _uiScale, viewportSize.X - (hudMargin * 2.0f));
        var confirmHeight = Mathf.Max(32.0f, _baseConfirmButtonSize.Y * _uiScale);
        _confirmButton.CustomMinimumSize = new Vector2(confirmWidth, confirmHeight);
        _confirmButton.AddThemeFontSizeOverride("font_size", Mathf.Max(10, Mathf.RoundToInt(_baseConfirmFontSize * _uiScale)));

        RecalculateBoardLayout();
    }

    private void RecalculateBoardLayout()
    {
        var viewportSize = GetViewportRect().Size;
        var scaledMargin = BoardMargin * _uiScale;
        var scaledTopInset = BoardTopInset * _uiScale;
        var scaledBottomInset = BoardBottomInset * _uiScale;

        var availableRect = new Rect2(
            scaledMargin,
            scaledTopInset,
            Mathf.Max(1.0f, viewportSize.X - (scaledMargin * 2.0f)),
            Mathf.Max(1.0f, viewportSize.Y - scaledTopInset - scaledBottomInset));

        var unitWidth = (_baseBoundsMax.X - _baseBoundsMin.X) + Mathf.Sqrt(3.0f);
        var unitHeight = (_baseBoundsMax.Y - _baseBoundsMin.Y) + 2.0f;
        var sizeX = availableRect.Size.X / unitWidth;
        var sizeY = availableRect.Size.Y / unitHeight;
        _hexSize = Mathf.Clamp(Mathf.Min(sizeX, sizeY), MinimumHexSize, MaximumHexSize);

        var baseCenter = (_baseBoundsMin + _baseBoundsMax) * 0.5f;
        _boardOrigin = availableRect.Position + (availableRect.Size * 0.5f) - (baseCenter * _hexSize);
    }

    private void OnViewportSizeChanged()
    {
        RefreshResponsiveLayout();
        QueueRedraw();
    }

    private void DrawBoard()
    {
        foreach (var cell in _drawOrderedCells)
        {
            var center = GetCellCenter(cell);
            var points = BuildHexPoints(center, _hexSize * 0.96f);
            var fillColor = _grid.IsEdge(cell)
                ? new Color(0.16f, 0.19f, 0.26f, 1.0f)
                : new Color(0.12f, 0.15f, 0.21f, 1.0f);
            var outlineColor = _grid.IsEdge(cell)
                ? new Color(0.48f, 0.75f, 0.94f, 0.95f)
                : new Color(0.23f, 0.31f, 0.41f, 0.85f);
            var outlineWidth = _grid.IsEdge(cell)
                ? Mathf.Max(1.2f, _hexSize * 0.08f)
                : Mathf.Max(0.9f, _hexSize * 0.04f);

            DrawColoredPolygon(points, fillColor);
            DrawHexOutline(points, outlineColor, outlineWidth);
        }
    }

    private void DrawReefs()
    {
        foreach (var reef in _reefCells)
        {
            var center = GetCellCenter(reef);
            DrawMarker(center, _hexSize * 1.15f, new Color(0.74f, 0.41f, 0.27f, 0.92f), 0.0f);
        }
    }

    private void DrawEnemies()
    {
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            DrawCombatIcon(enemy.Coord, 0.94f, new Color(0.90f, 0.34f, 0.28f, 0.96f), enemy.FireDirection);
            DrawCombatIcon(enemy.Coord, 0.32f, new Color(1.0f, 1.0f, 1.0f, 0.82f), enemy.FireDirection);
        }
    }

    private void DrawProjectiles(IReadOnlyList<ProjectileState> projectiles, Color tint, float scale)
    {
        foreach (var projectile in projectiles)
        {
            DrawCombatIcon(projectile.Coord, scale, tint, projectile.Direction);
        }
    }

    private void DrawCombatIcon(HexCoord coord, float scale, Color tint, int direction)
    {
        DrawMarker(GetCellCenter(coord), _hexSize * scale, tint, DirectionToAngle(direction));
    }

    private void DrawPreview(SimulationResult preview)
    {
        var previous = GetCellCenter(_fleet.Head);

        for (var index = 0; index < preview.HeadPath.Count; index++)
        {
            var coord = preview.HeadPath[index];
            var center = GetCellCenter(coord);
            var isFatalStep = preview.IsFatal && index == preview.HeadPath.Count - 1;
            var pathColor = isFatalStep
                ? new Color(0.97f, 0.39f, 0.33f, 0.95f)
                : new Color(0.80f, 0.91f, 1.0f, 0.82f);

            DrawLine(previous, center, pathColor, Mathf.Max(1.2f, _hexSize * 0.12f), true);
            DrawMarker(center, _hexSize * 0.55f, pathColor, 0.0f);

            if (isFatalStep)
            {
                DrawHexOutline(BuildHexPoints(center, _hexSize * 0.96f), pathColor, Mathf.Max(1.2f, _hexSize * 0.10f));
            }

            previous = center;
        }

        if (!preview.IsFatal)
        {
            DrawFleet(preview.Fleet, isGhost: true, alpha: 0.38f);
        }
    }

    private void DrawFleet(FleetState fleet, bool isGhost, float alpha)
    {
        for (var index = fleet.Segments.Count - 1; index >= 1; index--)
        {
            var segment = fleet.Segments[index];
            var center = GetCellCenter(segment.Coord);
            var tint = isGhost
                ? new Color(0.65f, 0.85f, 1.0f, alpha)
                : new Color(0.38f, 0.72f, 0.96f, alpha);

            DrawMarker(center, _hexSize * 0.95f, tint, 0.0f);
            DrawDirectionIndicator(center, segment.EntryDirection, new Color(1.0f, 1.0f, 1.0f, alpha * 0.9f));
        }

        var head = fleet.Segments[0];
        var headCenter = GetCellCenter(head.Coord);
        var headTint = isGhost
            ? new Color(1.0f, 0.84f, 0.39f, alpha)
            : new Color(1.0f, 0.78f, 0.25f, alpha);
        var rotation = DirectionToAngle(fleet.HeadDirection);

        DrawMarker(headCenter, _hexSize * 1.08f, headTint, rotation);
        DrawDirectionIndicator(headCenter, fleet.HeadDirection, new Color(0.12f, 0.16f, 0.24f, alpha));
    }

    private Vector2 GetCellCenter(HexCoord coord)
    {
        return _boardOrigin + _grid.ToWorld(coord, _hexSize);
    }

    private static Vector2[] BuildHexPoints(Vector2 center, float radius)
    {
        var points = new Vector2[6];
        for (var index = 0; index < 6; index++)
        {
            var angle = Mathf.DegToRad((60.0f * index) - 30.0f);
            points[index] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        return points;
    }

    private void DrawHexOutline(Vector2[] points, Color color, float width)
    {
        for (var index = 0; index < points.Length; index++)
        {
            var nextIndex = (index + 1) % points.Length;
            DrawLine(points[index], points[nextIndex], color, width, true);
        }
    }

    private void DrawMarker(Vector2 center, float pixelSize, Color tint, float rotationRadians)
    {
        var size = new Vector2(pixelSize, pixelSize);
        DrawSetTransform(center, rotationRadians, Vector2.One);
        DrawTextureRect(_placeholder, new Rect2(-size * 0.5f, size), false, tint);
        DrawSetTransform(Vector2.Zero, 0.0f, Vector2.One);
    }

    private void DrawDirectionIndicator(Vector2 center, int direction, Color color)
    {
        var directionVector = _grid.ToWorld(HexCoord.Directions[HexCoord.WrapDirection(direction)], 1.0f).Normalized();
        var tip = center + (directionVector * _hexSize * 0.52f);
        DrawLine(center, tip, color, Mathf.Max(1.0f, _hexSize * 0.08f), true);
        DrawCircle(tip, Mathf.Max(2.0f, _hexSize * 0.11f), color);
    }

    private float DirectionToAngle(int direction)
    {
        return _grid.ToWorld(HexCoord.Directions[HexCoord.WrapDirection(direction)], 1.0f).Angle() + Mathf.Pi * 0.5f;
    }

    private static string DescribeCollision(CollisionKind collisionKind)
    {
        return collisionKind switch
        {
            CollisionKind.OutOfBounds => "撞上边界",
            CollisionKind.Reef => "撞上礁石",
            CollisionKind.EnemyShip => "撞上敌舰",
            CollisionKind.EnemyProjectile => "撞上敌方炮弹",
            CollisionKind.Self => "撞上己方舰队",
            _ => "未知原因",
        };
    }

    private enum CollisionKind
    {
        None,
        OutOfBounds,
        Reef,
        EnemyShip,
        EnemyProjectile,
        Self,
    }

    private enum ShotResolution
    {
        None,
        DirectHit,
        CancelledProjectile,
        SpawnedProjectile,
    }

    private sealed class SimulationResult
    {
        private SimulationResult(FleetState fleet, List<HexCoord> headPath, CollisionKind collisionKind, HexCoord? collisionCoord)
        {
            Fleet = fleet.Clone();
            HeadPath = headPath.AsReadOnly();
            CollisionKind = collisionKind;
            CollisionCoord = collisionCoord;
        }

        public FleetState Fleet { get; }

        public IReadOnlyList<HexCoord> HeadPath { get; }

        public CollisionKind CollisionKind { get; }

        public HexCoord? CollisionCoord { get; }

        public bool IsFatal => CollisionKind != CollisionKind.None;

        public static SimulationResult Success(FleetState fleet, List<HexCoord> headPath)
        {
            return new SimulationResult(fleet, headPath, CollisionKind.None, null);
        }

        public static SimulationResult Fatal(FleetState fleet, List<HexCoord> headPath, CollisionKind collisionKind, HexCoord collisionCoord)
        {
            return new SimulationResult(fleet, headPath, collisionKind, collisionCoord);
        }
    }
}