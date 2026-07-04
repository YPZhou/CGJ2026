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
    private const int MaximumRewardsOnField = 3;
    private const int RewardAutoSpawnInterval = 6;
    private const int FleetLengthGoal = 9;
    private const int MaximumRange = 6;
    private const int MaximumCardMoveBonus = 2;
    private const float BaseViewportWidth = 1280.0f;
    private const float BaseViewportHeight = 720.0f;
    private const float BoardMargin = 36.0f;
    private const float BoardTopInset = 96.0f;
    private const float BoardBottomInset = 220.0f;
    private const float MinimumUiScale = 0.35f;
    private const float MaximumUiScale = 1.5f;
    private const float MinimumHexSize = 4.0f;
    private const float MaximumHexSize = 34.0f;
    private const float HoverTooltipOffsetX = 18.0f;
    private const float HoverTooltipOffsetY = 14.0f;
    private const float HoverTooltipViewportMargin = 8.0f;

    private static readonly List<AnimKind> AnimOrder = new()
    {
        AnimKind.FleetStep,
        AnimKind.EnemyMove,
        AnimKind.Pickup,
        AnimKind.CannonFire,
        AnimKind.ProjectileMove,
        AnimKind.Flash,
        AnimKind.Explosion,
    };

    private static readonly HexCoord Origin = new(0, 0, 0);

    private readonly RandomNumberGenerator _random = new();
    private readonly List<Button> _cardButtons = new();
    private readonly List<HexCoord> _drawOrderedCells = new();
    private readonly List<MoveCard> _hand = new();
    private readonly List<EnemyState> _enemies = new();
    private readonly List<ProjectileState> _playerProjectiles = new();
    private readonly List<ProjectileState> _enemyProjectiles = new();
    private readonly List<ProjectileState> _pendingPlayerProjectiles = new();
    private readonly List<ProjectileState> _pendingEnemyProjectiles = new();
    private readonly HashSet<ProjectileState> _spawnedThisTurn = new();
    private readonly Dictionary<HexCoord, RewardType> _rewards = new();

    private Texture2D _placeholder = null!;
    private HexGrid _grid = null!;
    private FleetState _fleet = null!;
    private HashSet<HexCoord> _reefCells = new();
    private SimulationResult? _preview;
    private MoveCard? _selectedCard;
    private int _selectedCardIndex = -1;
    private bool _isGameOver;
    private bool _isVictory;
    private string _gameOverReason = string.Empty;
    private string _lastCombatEvent = string.Empty;
    private string _lastRewardEvent = string.Empty;
    private int _turnCounter = 1;
    private int _straightPoolUpgrades;
    private int _leftPoolUpgrades;
    private int _rightPoolUpgrades;
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
    private Label _rewardLabel = null!;
    private PanelContainer _hoverTooltip = null!;
    private Label _hoverTooltipLabel = null!;
    private Button _confirmButton = null!;
    private float _baseHudMargin;
    private int _baseHudSeparation;
    private int _baseCardsRowSeparation;
    private Vector2 _baseCardButtonSize;
    private Vector2 _baseConfirmButtonSize;
    private int _baseStatusFontSize;
    private int _baseHintFontSize;
    private int _baseCombatFontSize;
    private int _baseRewardFontSize;
    private int _baseHoverTooltipFontSize;
    private ColorRect _gameOverOverlay = null!;
    private Label _gameOverTitleLabel = null!;
    private Label _gameOverReasonLabel = null!;
    private Label _gameOverStatsLabel = null!;
    private Button _gameOverRestartButton = null!;
    private int _baseGameOverTitleFontSize;
    private int _baseGameOverReasonFontSize;
    private int _baseGameOverStatsFontSize;
    private int _baseCardFontSize;
    private int _baseConfirmFontSize;
    private AnimationManager _animManager = null!;
    private EntityDisplayCoordinator _coordinator = null!;
    private bool _inputLocked;

    public override void _Ready()
    {
        _placeholder = GD.Load<Texture2D>("res://icon.svg");
        ResolveHudNodes();
        HookHudEvents();

        _coordinator = new EntityDisplayCoordinator(DefaultDisplayPosition);
        _animManager = new AnimationManager(_coordinator, () => _hexSize);
        _animManager.AllComplete += OnAnimationsComplete;
        _animManager.RedrawRequested += QueueRedraw;
        _animManager.PhaseEntered += OnPhaseEntered;

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

    public override void _Input(InputEvent @event)
    {
        if (_inputLocked && @event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode is Key.Space or Key.Enter)
        {
            _animManager.SkipAll();
        }
    }

    public override void _Process(double delta)
    {
        if (_grid == null)
        {
            return;
        }

        if (_inputLocked)
        {
            _animManager.Update((float)delta);
            return;
        }

        if (_isGameOver)
        {
            return;
        }

        UpdateHoverTooltip(GetLocalMousePosition(), GetViewport().GetMousePosition());
    }

    public override void _Draw()
    {
        if (_grid == null)
        {
            return;
        }

        DrawBoard();
        DrawReefs();
        DrawRewards();
        DrawEnemies();

        if (_inputLocked && _animManager.TryGetSegmentPosition(0, out _))
        {
            DrawAnimatedFleet();
        }
        else
        {
            DrawFleet(_fleet, isGhost: false, alpha: 1.0f);
        }

        DrawProjectiles(_enemyProjectiles, new Color(0.94f, 0.46f, 0.22f, 0.95f), 0.46f);
        DrawProjectiles(_playerProjectiles, new Color(0.98f, 0.86f, 0.32f, 0.95f), 0.42f);

        if (_preview != null)
        {
            DrawPreview(_preview);
        }

        DrawActiveEffects();
    }

    private void ResolveHudNodes()
    {
        _hudMargin = GetNode<MarginContainer>("HudLayer/HudMargin");
        _hudVBox = GetNode<VBoxContainer>("HudLayer/HudMargin/HudVBox");
        _cardsRow = GetNode<HBoxContainer>("HudLayer/HudMargin/HudVBox/CardsRow");
        _statusLabel = GetNode<Label>("HudLayer/HudMargin/HudVBox/StatusLabel");
        _hintLabel = GetNode<Label>("HudLayer/HudMargin/HudVBox/HintLabel");
        _combatLabel = GetNode<Label>("HudLayer/HudMargin/HudVBox/CombatLabel");
        _rewardLabel = GetNode<Label>("HudLayer/HudMargin/HudVBox/RewardLabel");
        _hoverTooltip = GetNode<PanelContainer>("HudLayer/HoverTooltip");
        _hoverTooltipLabel = GetNode<Label>("HudLayer/HoverTooltip/HoverTooltipLabel");
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
        _baseRewardFontSize = _rewardLabel.GetThemeFontSize("font_size");
        _baseHoverTooltipFontSize = _hoverTooltipLabel.GetThemeFontSize("font_size");
        _baseCardFontSize = _cardButtons[0].GetThemeFontSize("font_size");
        _baseConfirmFontSize = _confirmButton.GetThemeFontSize("font_size");

        _gameOverOverlay = GetNode<ColorRect>("HudLayer/GameOverOverlay");
        _gameOverTitleLabel = GetNode<Label>("HudLayer/GameOverOverlay/GameOverPanel/GameOverVBox/GameOverTitleLabel");
        _gameOverReasonLabel = GetNode<Label>("HudLayer/GameOverOverlay/GameOverPanel/GameOverVBox/GameOverReasonLabel");
        _gameOverStatsLabel = GetNode<Label>("HudLayer/GameOverOverlay/GameOverPanel/GameOverVBox/GameOverStatsLabel");
        _gameOverRestartButton = GetNode<Button>("HudLayer/GameOverOverlay/GameOverPanel/GameOverVBox/GameOverRestartButton");

        _baseGameOverTitleFontSize = _gameOverTitleLabel.GetThemeFontSize("font_size");
        _baseGameOverReasonFontSize = _gameOverReasonLabel.GetThemeFontSize("font_size");
        _baseGameOverStatsFontSize = _gameOverStatsLabel.GetThemeFontSize("font_size");
    }

    private void HookHudEvents()
    {
        for (var index = 0; index < _cardButtons.Count; index++)
        {
            var cardIndex = index;
            _cardButtons[index].Pressed += () => SelectCard(cardIndex);
        }

        _confirmButton.Pressed += OnConfirmPressed;
        _gameOverRestartButton.Pressed += StartNewDemo;
    }

    private void StartNewDemo()
    {
        _animManager.Reset();
        _inputLocked = false;
        _spawnedThisTurn.Clear();
        _pendingPlayerProjectiles.Clear();
        _pendingEnemyProjectiles.Clear();

        _gameOverOverlay.Visible = false;

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
        _rewards.Clear();
        _hand.Clear();
        _straightPoolUpgrades = 0;
        _leftPoolUpgrades = 0;
        _rightPoolUpgrades = 0;
        _turnCounter = 1;
        _isGameOver = false;
        _isVictory = false;
        _gameOverReason = string.Empty;

        SpawnOpeningEnemies();
        SpawnInitialRewards();
        FillHand();
        ClearSelection();

        _lastCombatEvent = "敌军已进入海域，敌方意图已标示。";
        _lastRewardEvent = "海面上已部署 2 处补给。";

        RefreshEnemyIntents();
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

    private void SpawnOpeningEnemies()
    {
        for (var index = 0; index < 2; index++)
        {
            TrySpawnOpeningCharger();
        }
    }

    private void SpawnInitialRewards()
    {
        TrySpawnRandomReward(minimumDistanceFromExistingRewards: 0);
        TrySpawnRandomReward(minimumDistanceFromExistingRewards: 4);
    }

    private bool TrySpawnOpeningCharger()
    {
        return TrySpawnEnemyWithFallbacks(
            EnemyType.Charger,
            cell =>
            {
                var boundaryDistance = GetBoundaryDistance(cell);
                return boundaryDistance >= 1 && boundaryDistance <= 2 && cell.DistanceTo(_fleet.Head) >= 8;
            },
            cell =>
            {
                var boundaryDistance = GetBoundaryDistance(cell);
                return boundaryDistance <= 2 && cell.DistanceTo(_fleet.Head) >= 7;
            },
            cell => cell.DistanceTo(_fleet.Head) >= 6);
    }

    private bool TrySpawnEdgeCharger()
    {
        return TrySpawnEnemyWithFallbacks(
            EnemyType.Charger,
            cell => GetBoundaryDistance(cell) <= 1 && cell.DistanceTo(_fleet.Head) >= 6,
            cell => GetBoundaryDistance(cell) <= 2 && cell.DistanceTo(_fleet.Head) >= 5,
            cell => cell.DistanceTo(_fleet.Head) >= 5);
    }

    private bool TrySpawnArtillery()
    {
        return TrySpawnEnemyWithFallbacks(
            EnemyType.Artillery,
            cell =>
            {
                var boundaryDistance = GetBoundaryDistance(cell);
                var fleetDistance = cell.DistanceTo(_fleet.Head);
                return boundaryDistance >= 1 && boundaryDistance <= 3 && fleetDistance >= 5 && fleetDistance <= 9;
            },
            cell => cell.DistanceTo(_fleet.Head) >= 5);
    }

    private bool TrySpawnMine()
    {
        return TrySpawnEnemyWithFallbacks(
            EnemyType.Mine,
            cell =>
            {
                var boundaryDistance = GetBoundaryDistance(cell);
                var fleetDistance = cell.DistanceTo(_fleet.Head);
                return boundaryDistance >= 3 && boundaryDistance <= 7 && fleetDistance >= 5 && fleetDistance <= 9;
            },
            cell => GetBoundaryDistance(cell) >= 2 && cell.DistanceTo(_fleet.Head) >= 4);
    }

    private bool TrySpawnSplitter()
    {
        return TrySpawnEnemyWithFallbacks(
            EnemyType.Splitter,
            cell =>
            {
                var boundaryDistance = GetBoundaryDistance(cell);
                return boundaryDistance >= 1 && boundaryDistance <= 4 && cell.DistanceTo(_fleet.Head) >= 6;
            },
            cell => cell.DistanceTo(_fleet.Head) >= 6);
    }

    private bool TrySpawnEnemyWithFallbacks(EnemyType type, params Predicate<HexCoord>[] predicates)
    {
        foreach (var predicate in predicates)
        {
            if (TrySpawnEnemy(type, predicate))
            {
                return true;
            }
        }

        return false;
    }

    private bool TrySpawnEnemy(EnemyType type, Predicate<HexCoord> predicate)
    {
        var candidates = new List<HexCoord>();
        foreach (var cell in _drawOrderedCells)
        {
            if (!predicate(cell) || !IsEnemySpawnCellOpen(cell))
            {
                continue;
            }

            candidates.Add(cell);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var coord = candidates[_random.RandiRange(0, candidates.Count - 1)];
        _enemies.Add(new EnemyState(type, coord, _turnCounter));
        return true;
    }

    private bool IsEnemySpawnCellOpen(HexCoord cell)
    {
        return _grid.Contains(cell)
            && !_reefCells.Contains(cell)
            && !_fleet.Occupies(cell)
            && !HasEnemyAt(cell)
            && !HasAnyProjectileAt(cell)
            && !_rewards.ContainsKey(cell);
    }

    private int GetBoundaryDistance(HexCoord cell)
    {
        var edgeDistance = Math.Max(Math.Abs(cell.Q), Math.Max(Math.Abs(cell.R), Math.Abs(cell.S)));
        return _grid.Radius - edgeDistance;
    }

    private void FillHand()
    {
        _hand.Clear();

        var cards = new List<MoveCard>
        {
            MoveCard.DrawFromPool(MoveCard.LeftPool, _leftPoolUpgrades, _random),
            MoveCard.DrawFromPool(MoveCard.StraightPool, _straightPoolUpgrades, _random),
            MoveCard.DrawFromPool(MoveCard.RightPool, _rightPoolUpgrades, _random),
        };

        _hand.AddRange(cards);
    }

    private void SelectCard(int index)
    {
        if (_isGameOver || _inputLocked || index < 0 || index >= _hand.Count)
        {
            return;
        }

        _selectedCardIndex = index;
        _selectedCard = _hand[index];
        _preview = ResolveCardMovement(_fleet, _selectedCard, previewOnly: true);

        UpdateHudState();
        QueueRedraw();
    }

    private void OnConfirmPressed()
    {
        if (_isGameOver || _inputLocked || _selectedCard == null || _selectedCardIndex < 0 || _selectedCardIndex >= _hand.Count)
        {
            return;
        }

        var activeCard = _hand[_selectedCardIndex];
        _hand.RemoveAt(_selectedCardIndex);

        var animEvents = new List<AnimationEvent>();
        var rewardEvents = new List<string>();
        var movementResult = ResolveCardMovement(_fleet, activeCard, previewOnly: false, animEvents);
        AppendRewardEvents(rewardEvents, movementResult.RewardEvents);

        if (movementResult.IsFatal)
        {
            _preview = movementResult;
            _isGameOver = true;
            _isVictory = false;
            _gameOverReason = DescribeCollision(movementResult.CollisionKind);
            _lastCombatEvent = "舰队在移动阶段沉没。";
            _lastRewardEvent = BuildRewardSummary(rewardEvents);
            PlayAnimations(animEvents);
            return;
        }

        _fleet = movementResult.Fleet;
        ClearSelection();

        var combatSummaries = new List<string>
        {
            ResolvePlayerFire(rewardEvents, animEvents),
        };

        if (!_isGameOver)
        {
            combatSummaries.Add(ResolveEnemyPhase(rewardEvents, animEvents));
        }

        _lastCombatEvent = BuildCombatSummary(combatSummaries);

        if (!_isGameOver)
        {
            CompleteTurn(rewardEvents);
        }

        _lastRewardEvent = BuildRewardSummary(rewardEvents);
        animEvents.Sort((a, b) => AnimOrder.IndexOf(a.Kind).CompareTo(AnimOrder.IndexOf(b.Kind)));
        PlayAnimations(animEvents);
    }

    private void PlayAnimations(List<AnimationEvent> animEvents)
    {
        if (animEvents.Count == 0)
        {
            OnAnimationsComplete();
            return;
        }

        foreach (var anim in animEvents)
        {
            switch (anim.Kind)
            {
                case AnimKind.FleetStep when anim.FleetStep != null:
                    var step = anim.FleetStep.Value;
                    _animManager.EnqueueFleetStep(step.FromPositions, step.ToPositions, anim.Duration);
                    break;
                case AnimKind.Flash:
                    _animManager.EnqueueFlash(anim.WorldPosition, anim.Tint, anim.Radius, anim.Duration);
                    break;
                case AnimKind.Explosion:
                    _animManager.EnqueueExplosion(anim.WorldPosition, anim.Radius, anim.Duration);
                    break;
                case AnimKind.Pickup:
                    _animManager.EnqueuePickup(anim.WorldPosition, anim.FloatingText, anim.Duration);
                    break;
                case AnimKind.EnemyMove:
                case AnimKind.ProjectileMove when anim.EntityMoves != null:
                    _animManager.EnqueueParallelMove(anim.EntityMoves, anim.Duration);
                    break;
                case AnimKind.CannonFire:
                    _animManager.EnqueueCannonFire(anim.WorldPosition, anim.Direction, anim.Duration, anim.Radius, anim.Tint, anim.CannonEntityKey, anim.SegmentIndex);
                    break;
            }
        }

        _inputLocked = true;
        _animManager.Start(this);
    }

    private void OnAnimationsComplete()
    {
        _inputLocked = false;
        UpdateHudState();
        QueueRedraw();
    }

    private void OnPhaseEntered(AnimKind kind)
    {
        if (kind != AnimKind.ProjectileMove)
        {
            return;
        }

        _playerProjectiles.AddRange(_pendingPlayerProjectiles);
        _enemyProjectiles.AddRange(_pendingEnemyProjectiles);
        _pendingPlayerProjectiles.Clear();
        _pendingEnemyProjectiles.Clear();
    }

    private void CompleteTurn(List<string> rewardEvents)
    {
        if (_fleet.Segments.Count >= FleetLengthGoal)
        {
            _isGameOver = true;
            _isVictory = true;
            _gameOverReason = "舰队扩编完成";
            return;
        }

        _turnCounter++;
        SpawnScheduledEnemiesForTurn(_turnCounter);
        TryAutoSpawnRewardForTurn(_turnCounter, rewardEvents);

        FillHand();
        RefreshEnemyIntents();
    }

    private void SpawnScheduledEnemiesForTurn(int turn)
    {
        switch (turn)
        {
            case 3:
                TrySpawnMine();
                TrySpawnMine();
                break;
            case 4:
                TrySpawnEdgeCharger();
                TrySpawnEdgeCharger();
                break;
            case 5:
                TrySpawnArtillery();
                break;
            case 7:
                TrySpawnEdgeCharger();
                TrySpawnArtillery();
                break;
            case 8:
                if (_fleet.Segments.Count >= 5)
                {
                    TrySpawnSplitter();
                }

                break;
            case 9:
                TrySpawnMine();
                break;
        }

        if (turn >= 10 && ((turn - 10) % 4) == 0)
        {
            var waveCount = 2 + ((turn - 10) / 4);
            for (var spawnIndex = 0; spawnIndex < waveCount; spawnIndex++)
            {
                if (_random.Randf() < 0.5f)
                {
                    TrySpawnEdgeCharger();
                }
                else
                {
                    TrySpawnArtillery();
                }
            }
        }
    }

    private void RefreshEnemyIntents()
    {
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            enemy.MoveIntentDirection = null;
            enemy.AttackIntentDirection = null;
            enemy.HasRadialAttackIntent = false;

            switch (enemy.Type)
            {
                case EnemyType.Charger:
                case EnemyType.Splitter:
                    if (TryGetGreedyPlayerStep(enemy.Coord, out var moveDirection, out _))
                    {
                        enemy.MoveIntentDirection = moveDirection;
                    }

                    break;
                case EnemyType.Artillery:
                    var originAfterMove = enemy.Coord;
                    if (TryGetArtilleryMove(enemy.Coord, out var artilleryMoveDirection, out var artilleryMoveTarget))
                    {
                        enemy.MoveIntentDirection = artilleryMoveDirection;
                        originAfterMove = artilleryMoveTarget;
                    }

                    enemy.AttackIntentDirection = GetDirectionToward(originAfterMove, GetPredictedPlayerCoord());
                    break;
                case EnemyType.Mine:
                    enemy.HasRadialAttackIntent = ShouldMineFireThisTurn(enemy);
                    break;
            }
        }
    }

    private HexCoord GetPredictedPlayerCoord()
    {
        return _fleet.Head.Step(_fleet.HeadDirection);
    }

    private SimulationResult ResolveCardMovement(FleetState source, MoveCard card, bool previewOnly, List<AnimationEvent>? animEvents = null)
    {
        var simulated = source.Clone();
        var headPath = new List<HexCoord>();
        var rewardEvents = new List<string>();
        var rewardMap = previewOnly
            ? new Dictionary<HexCoord, RewardType>(_rewards)
            : _rewards;

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
                return SimulationResult.Fatal(simulated, headPath, rewardEvents, collision, nextHead);
            }

            var rewardAtStep = rewardMap.TryGetValue(nextHead, out var rewardType);
            if (rewardAtStep)
            {
                rewardMap.Remove(nextHead);
                ApplyRewardPickup(simulated, rewardType, previewOnly, rewardEvents, rewardMap);

                if (animEvents != null)
                {
                    animEvents.Add(new AnimationEvent
                    {
                        Kind = AnimKind.Pickup,
                        Duration = 0.6f,
                        WorldPosition = GetCellCenter(nextHead),
                        FloatingText = DescribeRewardType(rewardType),
                    });
                }
            }

            if (animEvents != null)
            {
                RecordFleetStepAnimation(simulated, nextHead, animEvents);
            }

            simulated.MoveOneStep(nextHead, _grid);
            if (simulated.HasDuplicateCoords())
            {
                return SimulationResult.Fatal(simulated, headPath, rewardEvents, CollisionKind.Self, nextHead);
            }
        }

        return SimulationResult.Success(simulated, headPath, rewardEvents);
    }

    private void RecordFleetStepAnimation(FleetState simulated, HexCoord nextHead, List<AnimationEvent> animEvents)
    {
        var fromPositions = new List<Vector2>();
        var toPositions = new List<Vector2>();

        toPositions.Add(GetCellCenter(nextHead));
        fromPositions.Add(GetCellCenter(simulated.Head));

        for (var index = 1; index < simulated.Segments.Count; index++)
        {
            toPositions.Add(GetCellCenter(simulated.Segments[index - 1].Coord));
            fromPositions.Add(GetCellCenter(simulated.Segments[index].Coord));
        }

        animEvents.Add(new AnimationEvent
        {
            Kind = AnimKind.FleetStep,
            Duration = 0.2f,
            FleetStep = new FleetStepAnim
            {
                FromPositions = fromPositions,
                ToPositions = toPositions,
            },
        });
    }

    private void ApplyRewardPickup(FleetState fleet, RewardType rewardType, bool previewOnly, List<string> rewardEvents, IDictionary<HexCoord, RewardType> rewardMap)
    {
        if (previewOnly)
        {
            if (rewardType == RewardType.GrowthModule)
            {
                TryGrowFleet(fleet, rewardMap);
            }

            return;
        }

        switch (rewardType)
        {
            case RewardType.GrowthModule:
                if (TryGrowFleet(fleet, rewardMap))
                {
                    rewardEvents.Add("拾取增殖模块，舰队长度 +1。");
                }
                else
                {
                    rewardEvents.Add("拾取增殖模块，但尾部周围没有可用空位。");
                }

                break;
            case RewardType.CommandReload:
                _hand.Clear();
                FillHand();
                rewardEvents.Add("拾取指令重载，当前手牌已重抽。");
                break;
            case RewardType.FirepowerUpgrade:
                rewardEvents.Add(ApplyRandomFirepowerUpgrade(fleet));
                break;
            case RewardType.CardCalibration:
                rewardEvents.Add(ApplyCardCalibration());
                break;
        }
    }

    private bool TryGrowFleet(FleetState fleet, IDictionary<HexCoord, RewardType> rewardMap)
    {
        return fleet.TryAddSegment(_grid, cell =>
            _reefCells.Contains(cell)
            || HasEnemyAt(cell)
            || HasAnyProjectileAt(cell)
            || rewardMap.ContainsKey(cell));
    }

    private string ApplyRandomFirepowerUpgrade(FleetState fleet)
    {
        var segmentIndex = _random.RandiRange(0, fleet.Segments.Count - 1);
        var segment = fleet.Segments[segmentIndex];

        while (true)
        {
            switch (_random.RandiRange(0, 3))
            {
                case 0:
                    if (segment.Range >= MaximumRange)
                    {
                        continue;
                    }

                    segment.Range++;
                    return $"火力强化：{DescribeSegment(segmentIndex)}射程提升至 {segment.Range}。";
                case 1:
                    segment.Damage++;
                    return $"火力强化：{DescribeSegment(segmentIndex)}伤害提升至 {segment.Damage}。";
                case 2:
                    if (segment.ScatterLevel >= 2)
                    {
                        continue;
                    }

                    segment.ScatterLevel++;
                    return $"火力强化：{DescribeSegment(segmentIndex)}获得{DescribeScatter(segment.ScatterLevel)}。";
                case 3:
                    if (segment.HasExplosive)
                    {
                        continue;
                    }

                    segment.HasExplosive = true;
                    return $"火力强化：{DescribeSegment(segmentIndex)}获得爆炸弹头。";
            }
        }
    }

    private string ApplyCardCalibration()
    {
        var poolIndices = new List<int>();
        if (_straightPoolUpgrades < MaximumCardMoveBonus)
        {
            poolIndices.Add(0);
        }

        if (_leftPoolUpgrades < MaximumCardMoveBonus)
        {
            poolIndices.Add(1);
        }

        if (_rightPoolUpgrades < MaximumCardMoveBonus)
        {
            poolIndices.Add(2);
        }

        if (poolIndices.Count == 0)
        {
            _hand.Clear();
            FillHand();
            return "拾取指令校准，但所有方向池均已满级，效果转为指令重载。";
        }

        var selected = poolIndices[_random.RandiRange(0, poolIndices.Count - 1)];
        var poolName = selected switch
        {
            0 => "直行池",
            1 => "左转池",
            _ => "右转池",
        };
        var newLevel = selected switch
        {
            0 => ++_straightPoolUpgrades,
            1 => ++_leftPoolUpgrades,
            _ => ++_rightPoolUpgrades,
        };

        return $"拾取指令校准，{poolName}升级至 +{newLevel} 移动距离。";
    }

    private static string DescribeSegment(int segmentIndex)
    {
        return segmentIndex == 0 ? "蛇头" : $"第 {segmentIndex + 1} 节舰船";
    }

    private static string DescribeScatter(int scatterLevel)
    {
        return scatterLevel switch
        {
            1 => "散射 I",
            2 => "散射 II",
            _ => "散射",
        };
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

    private string ResolvePlayerFire(List<string> rewardEvents, List<AnimationEvent>? animEvents = null)
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

            if (animEvents != null && fireDirections.Count > 0)
            {
                animEvents.Add(new AnimationEvent
                {
                    Kind = AnimKind.CannonFire,
                    Duration = 0.3f,
                    WorldPosition = GetCellCenter(segment.Coord),
                    Radius = _hexSize * 0.4f,
                    Tint = new Color(0.98f, 0.86f, 0.32f, 0.9f),
                    Direction = baseDirection,
                    SegmentIndex = index,
                });
            }

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

        var destroyedEnemies = ResolveDefeatedEnemies(rewardEvents, animEvents);
        if (directHits == 0 && cancelledProjectiles == 0 && spawnedProjectiles == 0)
        {
            return destroyedEnemies == 0
                ? "玩家齐射未形成有效火线。"
                : $"玩家齐射击沉 {destroyedEnemies} 艘敌舰。";
        }

        var destroyedText = destroyedEnemies > 0
            ? $" 击沉 {destroyedEnemies} 艘敌舰。"
            : string.Empty;
        return $"玩家齐射：命中 {directHits}，拦截 {cancelledProjectiles}，出膛 {spawnedProjectiles}。{destroyedText}";
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
                var projectile = new ProjectileState(firstCell, fireDirection, segment.Damage, ProjectileOwner.Player, segment.HasExplosive);
                _playerProjectiles.Add(projectile);
                _spawnedThisTurn.Add(projectile);
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

    private string ResolveEnemyPhase(List<string> rewardEvents, List<AnimationEvent>? animEvents = null)
    {
        var summaries = new List<string>
        {
            MoveEnemies(animEvents),
        };

        if (_isGameOver)
        {
            return BuildCombatSummary(summaries);
        }

        summaries.Add(AttackEnemies(animEvents));
        if (_isGameOver)
        {
            return BuildCombatSummary(summaries);
        }

        summaries.Add(AdvanceProjectiles(rewardEvents, animEvents));
        return BuildCombatSummary(summaries);
    }

    private string MoveEnemies(List<AnimationEvent>? animEvents = null)
    {
        var enemyMoves = new List<EntityMove>();
        var movedSteps = 0;

        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            var startCoord = enemy.Coord;
            var maxSteps = GetEnemyMoveSpeed(enemy.Type);
            for (var stepIndex = 0; stepIndex < maxSteps; stepIndex++)
            {
                if (!TryGetEnemyNextStep(enemy, out var nextCoord))
                {
                    break;
                }

                if (_fleet.Occupies(nextCoord))
                {
                    if (animEvents != null)
                    {
                        animEvents.Add(new AnimationEvent
                        {
                            Kind = AnimKind.Explosion,
                            Duration = 0.45f,
                            WorldPosition = GetCellCenter(nextCoord),
                            Radius = _hexSize * 1.2f,
                        });
                    }

                    _isGameOver = true;
                    _isVictory = false;
                    _gameOverReason = "被敌舰撞击";
                    return $"{DescribeEnemyType(enemy.Type)}直接撞入舰队。";
                }

                enemy.Coord = nextCoord;
                movedSteps++;
            }

            if (!enemy.Coord.Equals(startCoord))
            {
                enemyMoves.Add(new EntityMove
                {
                    EntityKey = enemy,
                    FromWorld = GetCellCenter(startCoord),
                    ToWorld = GetCellCenter(enemy.Coord),
                    Scale = GetEnemyBodyScale(enemy.Type),
                    Tint = GetEnemyColor(enemy.Type),
                });
            }
        }

        if (animEvents != null && enemyMoves.Count > 0)
        {
            animEvents.Add(new AnimationEvent
            {
                Kind = AnimKind.EnemyMove,
                Duration = 0.15f,
                EntityMoves = enemyMoves,
            });
        }

        return movedSteps == 0
            ? "敌军机动未能改变阵位。"
            : $"敌军机动 {movedSteps} 步。";
    }

    private static int GetEnemyMoveSpeed(EnemyType type)
    {
        return type switch
        {
            EnemyType.Charger => 2,
            EnemyType.Artillery => 1,
            EnemyType.Splitter => 1,
            _ => 0,
        };
    }

    private bool TryGetEnemyNextStep(EnemyState enemy, out HexCoord nextCoord)
    {
        nextCoord = enemy.Coord;

        switch (enemy.Type)
        {
            case EnemyType.Charger:
            case EnemyType.Splitter:
                return TryGetGreedyPlayerStep(enemy.Coord, out _, out nextCoord);
            case EnemyType.Artillery:
                return TryGetArtilleryMove(enemy.Coord, out _, out nextCoord);
            default:
                return false;
        }
    }

    private bool TryGetGreedyPlayerStep(HexCoord origin, out int direction, out HexCoord nextCoord)
    {
        direction = 0;
        nextCoord = origin;

        var immediateContact = new List<(int Direction, HexCoord Coord)>();
        var bestDistance = int.MaxValue;
        var candidates = new List<(int Direction, HexCoord Coord)>();

        for (var candidateDirection = 0; candidateDirection < HexCoord.Directions.Length; candidateDirection++)
        {
            var candidate = origin.Step(candidateDirection);
            if (!_grid.Contains(candidate) || _reefCells.Contains(candidate) || _rewards.ContainsKey(candidate))
            {
                continue;
            }

            if (_fleet.Occupies(candidate))
            {
                immediateContact.Add((candidateDirection, candidate));
                continue;
            }

            var candidateDistance = candidate.DistanceTo(_fleet.Head);
            if (candidateDistance < bestDistance)
            {
                bestDistance = candidateDistance;
                candidates.Clear();
                candidates.Add((candidateDirection, candidate));
            }
            else if (candidateDistance == bestDistance)
            {
                candidates.Add((candidateDirection, candidate));
            }
        }

        if (immediateContact.Count > 0)
        {
            var selected = immediateContact[_random.RandiRange(0, immediateContact.Count - 1)];
            direction = selected.Direction;
            nextCoord = selected.Coord;
            return true;
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var choice = candidates[_random.RandiRange(0, candidates.Count - 1)];
        direction = choice.Direction;
        nextCoord = choice.Coord;
        return true;
    }

    private bool TryGetArtilleryMove(HexCoord origin, out int direction, out HexCoord nextCoord)
    {
        direction = 0;
        nextCoord = origin;

        var currentDistance = origin.DistanceTo(_fleet.Head);
        if (currentDistance >= 4 && currentDistance <= 6)
        {
            return false;
        }

        var moveAway = currentDistance < 4;
        var bestDistance = moveAway ? int.MinValue : int.MaxValue;
        var candidates = new List<(int Direction, HexCoord Coord)>();

        for (var candidateDirection = 0; candidateDirection < HexCoord.Directions.Length; candidateDirection++)
        {
            var candidate = origin.Step(candidateDirection);
            if (!_grid.Contains(candidate) || _reefCells.Contains(candidate) || _rewards.ContainsKey(candidate) || _fleet.Occupies(candidate))
            {
                continue;
            }

            var candidateDistance = candidate.DistanceTo(_fleet.Head);
            if (moveAway)
            {
                if (candidateDistance > bestDistance)
                {
                    bestDistance = candidateDistance;
                    candidates.Clear();
                    candidates.Add((candidateDirection, candidate));
                }
                else if (candidateDistance == bestDistance)
                {
                    candidates.Add((candidateDirection, candidate));
                }
            }
            else
            {
                if (candidateDistance < bestDistance)
                {
                    bestDistance = candidateDistance;
                    candidates.Clear();
                    candidates.Add((candidateDirection, candidate));
                }
                else if (candidateDistance == bestDistance)
                {
                    candidates.Add((candidateDirection, candidate));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        if ((moveAway && bestDistance <= currentDistance) || (!moveAway && bestDistance >= currentDistance))
        {
            return false;
        }

        var selected = candidates[_random.RandiRange(0, candidates.Count - 1)];
        direction = selected.Direction;
        nextCoord = selected.Coord;
        return true;
    }

    private string AttackEnemies(List<AnimationEvent>? animEvents = null)
    {
        var spawnedProjectiles = 0;
        var firingMines = 0;

        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            switch (enemy.Type)
            {
                case EnemyType.Artillery:
                    var attackDirection = GetDirectionToward(enemy.Coord, GetPredictedPlayerCoord());
                    if (animEvents != null)
                    {
                        animEvents.Add(new AnimationEvent
                        {
                            Kind = AnimKind.CannonFire,
                            Duration = 0.3f,
                            WorldPosition = GetCellCenter(enemy.Coord),
                            Radius = _hexSize * 0.48f,
                            Tint = new Color(0.94f, 0.46f, 0.22f, 0.92f),
                            Direction = attackDirection,
                            CannonEntityKey = enemy,
                        });
                    }

                    if (TrySpawnEnemyProjectile(enemy.Coord, attackDirection, out var hitFleet))
                    {
                        spawnedProjectiles++;
                    }

                    if (hitFleet)
                    {
                        _isGameOver = true;
                        _isVictory = false;
                        _gameOverReason = "被敌方炮火击中";
                        return "炮击哨近距开火命中舰队。";
                    }

                    break;
                case EnemyType.Mine:
                    if (!ShouldMineFireThisTurn(enemy))
                    {
                        continue;
                    }

                    firingMines++;
                    for (var direction = 0; direction < HexCoord.Directions.Length; direction++)
                    {
                        if (TrySpawnEnemyProjectile(enemy.Coord, direction, out hitFleet))
                        {
                            spawnedProjectiles++;
                        }

                        if (animEvents != null)
                        {
                            animEvents.Add(new AnimationEvent
                            {
                                Kind = AnimKind.CannonFire,
                                Duration = 0.2f,
                                WorldPosition = GetCellCenter(enemy.Coord),
                                Radius = _hexSize * 0.38f,
                                Tint = new Color(0.85f, 0.84f, 0.82f, 0.92f),
                                Direction = direction,
                                CannonEntityKey = enemy,
                            });
                        }

                        if (!hitFleet)
                        {
                            continue;
                        }

                        _isGameOver = true;
                        _isVictory = false;
                        _gameOverReason = "被敌方炮火击中";
                        return "断脊水雷齐射命中舰队。";
                    }

                    break;
            }
        }

        if (spawnedProjectiles == 0)
        {
            return firingMines == 0
                ? "敌军本回合未形成有效射线。"
                : "敌军发动攻击，但火线被地形或占位阻断。";
        }

        return firingMines == 0
            ? $"敌军发射 {spawnedProjectiles} 枚炮弹。"
            : $"敌军发射 {spawnedProjectiles} 枚炮弹，其中 {firingMines} 枚来自水雷齐射。";
    }

    private bool TrySpawnEnemyProjectile(HexCoord origin, int direction, out bool hitFleet)
    {
        hitFleet = false;

        var spawnCoord = origin.Step(direction);
        if (!_grid.Contains(spawnCoord) || _reefCells.Contains(spawnCoord) || HasEnemyAt(spawnCoord) || HasAnyProjectileAt(spawnCoord))
        {
            return false;
        }

        if (_fleet.Occupies(spawnCoord))
        {
            hitFleet = true;
            return false;
        }

        var projectile = new ProjectileState(spawnCoord, direction, 1, ProjectileOwner.Enemy, false);
        _enemyProjectiles.Add(projectile);
        _spawnedThisTurn.Add(projectile);
        return true;
    }

    private bool ShouldMineFireThisTurn(EnemyState enemy)
    {
        return _turnCounter >= enemy.SpawnTurn + 3 && ((_turnCounter - enemy.SpawnTurn) % 3) == 0;
    }

    private string AdvanceProjectiles(List<string> rewardEvents, List<AnimationEvent>? animEvents = null)
    {
        var projectileMoves = new List<EntityMove>();
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
                if (animEvents != null)
                {
                    animEvents.Add(new AnimationEvent
                    {
                        Kind = AnimKind.Flash,
                        Duration = 0.3f,
                        WorldPosition = GetCellCenter(cell),
                        Radius = _hexSize * 0.36f,
                        Tint = new Color(1.0f, 1.0f, 0.9f, 0.92f),
                    });
                }

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

                    if (animEvents != null)
                    {
                        animEvents.Add(new AnimationEvent
                        {
                            Kind = AnimKind.Explosion,
                            Duration = 0.4f,
                            WorldPosition = GetCellCenter(cell),
                            Radius = _hexSize * 1.1f,
                        });
                    }

                    continue;
                }

                foreach (var projectile in playerProjectilesAtCell!)
                {
                    if (animEvents != null)
                    {
                        var prevCoord = projectile.Coord.Step(HexCoord.WrapDirection(projectile.Direction + 3));
                        projectileMoves.Add(new EntityMove
                        {
                            EntityKey = projectile,
                            FromWorld = GetCellCenter(prevCoord),
                            ToWorld = GetCellCenter(projectile.Coord),
                            Scale = 0.42f,
                            Tint = new Color(0.98f, 0.86f, 0.32f, 0.95f),
                        });
                    }

                    if (_spawnedThisTurn.Contains(projectile))
                    {
                        _pendingPlayerProjectiles.Add(projectile);
                    }
                    else
                    {
                        _playerProjectiles.Add(projectile);
                    }
                }

                continue;
            }

            if (!hasEnemyProjectile)
            {
                continue;
            }

            if (_fleet.Occupies(cell))
            {
                if (animEvents != null)
                {
                    animEvents.Add(new AnimationEvent
                    {
                        Kind = AnimKind.Explosion,
                        Duration = 0.45f,
                        WorldPosition = GetCellCenter(cell),
                        Radius = _hexSize * 1.3f,
                    });
                }

                _isGameOver = true;
                _isVictory = false;
                _gameOverReason = "被敌方炮火击中";
                continue;
            }

            foreach (var projectile in enemyProjectilesAtCell!)
            {
                if (animEvents != null)
                {
                    var prevCoord = projectile.Coord.Step(HexCoord.WrapDirection(projectile.Direction + 3));
                    projectileMoves.Add(new EntityMove
                    {
                        EntityKey = projectile,
                        FromWorld = GetCellCenter(prevCoord),
                        ToWorld = GetCellCenter(projectile.Coord),
                        Scale = 0.46f,
                        Tint = new Color(0.94f, 0.46f, 0.22f, 0.95f),
                    });
                }

                if (_spawnedThisTurn.Contains(projectile))
                {
                    _pendingEnemyProjectiles.Add(projectile);
                }
                else
                {
                    _enemyProjectiles.Add(projectile);
                }
            }
        }

        if (animEvents != null && projectileMoves.Count > 0)
        {
            animEvents.Add(new AnimationEvent
            {
                Kind = AnimKind.ProjectileMove,
                Duration = 0.15f,
                EntityMoves = projectileMoves,
            });
        }

        _spawnedThisTurn.Clear();

        if (_isGameOver)
        {
            return "敌方炮弹命中舰队。";
        }

        var destroyedEnemies = ResolveDefeatedEnemies(rewardEvents, animEvents);
        if (playerHits == 0 && projectileDuels == 0 && expiredProjectiles == 0 && destroyedEnemies == 0)
        {
            return "炮弹阶段未发生额外命中。";
        }

        var destroyedText = destroyedEnemies > 0
            ? $" 击沉 {destroyedEnemies} 艘敌舰。"
            : string.Empty;
        return $"炮弹推进：命中 {playerHits}，对撞 {projectileDuels}，消散 {expiredProjectiles}。{destroyedText}";
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

    private int ResolveDefeatedEnemies(List<string> rewardEvents, List<AnimationEvent>? animEvents = null)
    {
        var defeated = 0;
        for (var index = _enemies.Count - 1; index >= 0; index--)
        {
            if (_enemies[index].IsAlive)
            {
                continue;
            }

            var enemy = _enemies[index];
            var deathCoord = enemy.Coord;
            _enemies.RemoveAt(index);
            defeated++;

            if (animEvents != null)
            {
                animEvents.Add(new AnimationEvent
                {
                    Kind = AnimKind.Explosion,
                    Duration = 0.5f,
                    WorldPosition = GetCellCenter(deathCoord),
                    Radius = _hexSize * 1.1f,
                });
            }

            if (TryDropRewardAt(deathCoord, out var rewardType))
            {
                rewardEvents.Add($"{DescribeEnemyType(enemy.Type)}掉落了 {DescribeRewardType(rewardType)}。");
            }

            if (enemy.Type == EnemyType.Splitter)
            {
                var spawnedChildren = SpawnSplitChildren(deathCoord);
                if (spawnedChildren > 0)
                {
                    rewardEvents.Add($"分裂冲角解体，分裂出 {spawnedChildren} 艘突击梭。");
                }
            }
        }

        return defeated;
    }

    private bool TryDropRewardAt(HexCoord origin, out RewardType rewardType)
    {
        rewardType = RewardType.GrowthModule;
        if (_rewards.Count >= MaximumRewardsOnField || _random.Randf() >= 0.5f)
        {
            return false;
        }

        rewardType = RollRewardType();
        return TryPlaceRewardAtOrAdjacent(origin, rewardType);
    }

    private int SpawnSplitChildren(HexCoord origin)
    {
        var candidates = new List<HexCoord>();
        foreach (var cell in _grid.GetNeighbors(origin))
        {
            if (IsEnemySpawnCellOpen(cell))
            {
                candidates.Add(cell);
            }
        }

        var spawned = 0;
        while (spawned < 2 && candidates.Count > 0)
        {
            var selectedIndex = _random.RandiRange(0, candidates.Count - 1);
            var coord = candidates[selectedIndex];
            candidates.RemoveAt(selectedIndex);
            _enemies.Add(new EnemyState(EnemyType.Charger, coord, _turnCounter));
            spawned++;
        }

        return spawned;
    }

    private RewardType RollRewardType()
    {
        var roll = _random.RandiRange(1, 100);
        if (roll <= 15)
        {
            return RewardType.GrowthModule;
        }

        if (roll <= 35)
        {
            return RewardType.CommandReload;
        }

        if (roll <= 65)
        {
            return RewardType.FirepowerUpgrade;
        }

        return RewardType.CardCalibration;
    }

    private bool TryAutoSpawnRewardForTurn(int turn, List<string> rewardEvents)
    {
        if ((turn % RewardAutoSpawnInterval) != 0 || _rewards.Count >= MaximumRewardsOnField)
        {
            return false;
        }

        if (!TrySpawnRandomReward())
        {
            return false;
        }

        rewardEvents.Add("海面刷新了一件新的补给。");
        return true;
    }

    private bool TrySpawnRandomReward(int minimumDistanceFromExistingRewards = 0)
    {
        if (_rewards.Count >= MaximumRewardsOnField)
        {
            return false;
        }

        var preferred = new List<HexCoord>();
        var fallback = new List<HexCoord>();
        foreach (var cell in _drawOrderedCells)
        {
            if (!IsRewardSpawnCellOpen(cell))
            {
                continue;
            }

            fallback.Add(cell);
            if (minimumDistanceFromExistingRewards <= 0 || IsFarEnoughFromRewards(cell, minimumDistanceFromExistingRewards))
            {
                preferred.Add(cell);
            }
        }

        var candidates = preferred.Count > 0 ? preferred : fallback;
        if (candidates.Count == 0)
        {
            return false;
        }

        var coord = candidates[_random.RandiRange(0, candidates.Count - 1)];
        _rewards[coord] = RollRewardType();
        return true;
    }

    private bool IsFarEnoughFromRewards(HexCoord cell, int minimumDistance)
    {
        foreach (var reward in _rewards.Keys)
        {
            if (cell.DistanceTo(reward) < minimumDistance)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsRewardSpawnCellOpen(HexCoord cell)
    {
        return _grid.Contains(cell)
            && !_reefCells.Contains(cell)
            && !_fleet.Occupies(cell)
            && !HasEnemyAt(cell)
            && !HasAnyProjectileAt(cell)
            && !_rewards.ContainsKey(cell);
    }

    private bool TryPlaceRewardAtOrAdjacent(HexCoord origin, RewardType rewardType)
    {
        if (IsRewardSpawnCellOpen(origin))
        {
            _rewards[origin] = rewardType;
            return true;
        }

        var candidates = new List<HexCoord>();
        foreach (var neighbor in _grid.GetNeighbors(origin))
        {
            if (IsRewardSpawnCellOpen(neighbor))
            {
                candidates.Add(neighbor);
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var coord = candidates[_random.RandiRange(0, candidates.Count - 1)];
        _rewards[coord] = rewardType;
        return true;
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

    private void ClearSelection()
    {
        _preview = null;
        _selectedCard = null;
        _selectedCardIndex = -1;
    }

    private void ShowGameOver()
    {
        _gameOverOverlay.Visible = true;
        _gameOverTitleLabel.Text = _isVictory ? "胜利" : "舰队沉没";
        _gameOverReasonLabel.Text = _gameOverReason;
        _gameOverStatsLabel.Text = $"第 {_turnCounter} 回合  舰队长度 {_fleet.Segments.Count}/{FleetLengthGoal}";
        _gameOverRestartButton.GrabFocus();
    }

    private void UpdateHudState()
    {
        UpdateStatusLabel();
        UpdateHintLabel();
        UpdateCombatLabel();
        UpdateRewardLabel();
        UpdateCardButtons();
        _confirmButton.Disabled = _isGameOver || _selectedCard == null;

        if (_isGameOver && !_gameOverOverlay.Visible)
        {
            ShowGameOver();
        }
    }

    private void UpdateStatusLabel()
    {
        if (_isGameOver)
        {
            var resultText = _isVictory ? "胜利" : "失败";
            _statusLabel.Text = $"回合 {_turnCounter}  舰队长度 {_fleet.Segments.Count}/{FleetLengthGoal}\n{resultText}: {_gameOverReason}";
            return;
        }

        var detail = "选择一张指令卡查看预览。";
        if (_selectedCard != null && _preview != null)
        {
            detail = _preview.IsFatal
                ? $"预览危险: {DescribeCollision(_preview.CollisionKind)}"
                : $"已选择 {_selectedCard.Name}: {_selectedCard.Summary}";
        }

        _statusLabel.Text = $"回合 {_turnCounter}  舰队长度 {_fleet.Segments.Count}/{FleetLengthGoal}\n{detail}";
    }

    private void UpdateHintLabel()
    {
        _hintLabel.Text = "敌军会显示移动/开火意图。拾取补给会在移动中立即生效。";
    }

    private void UpdateHoverTooltip(Vector2 localMousePosition, Vector2 screenMousePosition)
    {
        if (!TryGetHoveredCell(localMousePosition, out var hoveredCell))
        {
            _hoverTooltip.Visible = false;
            return;
        }

        var tooltipText = BuildHoverTooltipText(hoveredCell);
        if (string.IsNullOrWhiteSpace(tooltipText))
        {
            _hoverTooltip.Visible = false;
            return;
        }

        _hoverTooltipLabel.Text = tooltipText;
        PlaceHoverTooltip(screenMousePosition);
        _hoverTooltip.Visible = true;
    }

    private bool TryGetHoveredCell(Vector2 localMousePosition, out HexCoord hoveredCell)
    {
        foreach (var cell in _drawOrderedCells)
        {
            if (!Geometry2D.IsPointInPolygon(localMousePosition, BuildHexPoints(GetCellCenter(cell), _hexSize * 0.96f)))
            {
                continue;
            }

            hoveredCell = cell;
            return true;
        }

        hoveredCell = default;
        return false;
    }

    private string BuildHoverTooltipText(HexCoord coord)
    {
        var lines = new List<string>();

        if (_fleet.Occupies(coord))
        {
            lines.Add($"我方舰队：指令卡决定航向与步数，当前长度 {_fleet.Segments.Count}。" );
        }

        var enemy = FindEnemyAt(coord);
        if (enemy != null)
        {
            lines.Add(BuildEnemyTooltipLine(enemy));
        }

        AppendProjectileTooltipLines(lines, coord);

        if (_rewards.TryGetValue(coord, out var rewardType))
        {
            lines.Add(BuildRewardTooltipLine(rewardType));
        }

        if (_reefCells.Contains(coord))
        {
            lines.Add("礁石：阻挡舰队移动，也会拦下炮弹。" );
        }

        if (lines.Count == 0)
        {
            lines.Add(_grid.IsEdge(coord)
                ? "边缘海域：仍可航行，但再向外就会驶离战场。"
                : "空海域：可航行，也可能成为交火路径。");
        }

        return string.Join("\n", lines);
    }

    private string BuildEnemyTooltipLine(EnemyState enemy)
    {
        var behavior = enemy.Type switch
        {
            EnemyType.Charger => "会朝舰队冲锋。",
            EnemyType.Artillery => "会保持距离并发射炮弹。",
            EnemyType.Mine => "会定期向六个方向齐射。",
            EnemyType.Splitter => "被击毁后会分裂出突击梭。",
            _ => "会威胁舰队航线。",
        };

        return $"{DescribeEnemyType(enemy.Type)}：耐久 {enemy.Health}/{enemy.MaxHealth}，{behavior}";
    }

    private void AppendProjectileTooltipLines(List<string> lines, HexCoord coord)
    {
        var playerProjectileCount = CountProjectilesAt(_playerProjectiles, coord);
        if (playerProjectileCount > 0)
        {
            lines.Add(playerProjectileCount == 1
                ? "己方炮弹：每回合前进 1 格，可拦截敌方炮弹。"
                : $"己方炮弹 x{playerProjectileCount}：每回合前进 1 格，可拦截敌方炮弹。");
        }

        var enemyProjectileCount = CountProjectilesAt(_enemyProjectiles, coord);
        if (enemyProjectileCount > 0)
        {
            lines.Add(enemyProjectileCount == 1
                ? "敌方炮弹：每回合前进 1 格，命中舰队即失败。"
                : $"敌方炮弹 x{enemyProjectileCount}：每回合前进 1 格，命中舰队即失败。");
        }
    }

    private int CountProjectilesAt(IReadOnlyList<ProjectileState> projectiles, HexCoord coord)
    {
        var count = 0;
        foreach (var projectile in projectiles)
        {
            if (projectile.Coord.Equals(coord))
            {
                count++;
            }
        }

        return count;
    }

    private string BuildRewardTooltipLine(RewardType rewardType)
    {
        var effect = rewardType switch
        {
            RewardType.GrowthModule => "驶入后会让舰队增长 1 节。",
            RewardType.CommandReload => "驶入后会立刻补满手牌。",
            RewardType.FirepowerUpgrade => "驶入后会强化一节舰船的火力。",
            RewardType.CardCalibration => "驶入后会提升后续抽到的指令卡步数。",
            _ => "驶入后会立刻生效。",
        };

        return $"{DescribeRewardType(rewardType)}：{effect}";
    }

    private void PlaceHoverTooltip(Vector2 screenMousePosition)
    {
        _hoverTooltip.Visible = true;

        var desiredPosition = screenMousePosition + new Vector2(HoverTooltipOffsetX, HoverTooltipOffsetY);
        var tooltipSize = _hoverTooltip.GetCombinedMinimumSize();
        var viewportSize = GetViewportRect().Size;

        desiredPosition.X = Mathf.Clamp(
            desiredPosition.X,
            HoverTooltipViewportMargin,
            Mathf.Max(HoverTooltipViewportMargin, viewportSize.X - tooltipSize.X - HoverTooltipViewportMargin));
        desiredPosition.Y = Mathf.Clamp(
            desiredPosition.Y,
            HoverTooltipViewportMargin,
            Mathf.Max(HoverTooltipViewportMargin, viewportSize.Y - tooltipSize.Y - HoverTooltipViewportMargin));

        _hoverTooltip.Position = desiredPosition;
    }

    private void UpdateCombatLabel()
    {
        var playerState = _isGameOver
            ? (_isVictory ? "舰队状态: 胜利" : $"舰队状态: {_gameOverReason}")
            : "舰队状态: 作战中";
        _combatLabel.Text = $"敌舰 {_enemies.Count}  敌弹 {_enemyProjectiles.Count}  己弹 {_playerProjectiles.Count}\n{playerState}\n{_lastCombatEvent}";
    }

    private void UpdateRewardLabel()
    {
        _rewardLabel.Text = $"奖励 {_rewards.Count}/{MaximumRewardsOnField}  池 +{_straightPoolUpgrades}/{_leftPoolUpgrades}/{_rightPoolUpgrades}\n{_lastRewardEvent}";
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
        _rewardLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(10, Mathf.RoundToInt(_baseRewardFontSize * _uiScale)));
        _hoverTooltipLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(10, Mathf.RoundToInt(_baseHoverTooltipFontSize * _uiScale)));

        var confirmWidth = Mathf.Min(_baseConfirmButtonSize.X * _uiScale, viewportSize.X - (hudMargin * 2.0f));
        var confirmHeight = Mathf.Max(32.0f, _baseConfirmButtonSize.Y * _uiScale);
        _confirmButton.CustomMinimumSize = new Vector2(confirmWidth, confirmHeight);
        _confirmButton.AddThemeFontSizeOverride("font_size", Mathf.Max(10, Mathf.RoundToInt(_baseConfirmFontSize * _uiScale)));

        _gameOverTitleLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(18, Mathf.RoundToInt(_baseGameOverTitleFontSize * _uiScale * 2.0f)));
        _gameOverReasonLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(12, Mathf.RoundToInt(_baseGameOverReasonFontSize * _uiScale)));
        _gameOverStatsLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(12, Mathf.RoundToInt(_baseGameOverStatsFontSize * _uiScale)));

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

    private void DrawRewards()
    {
        foreach (var reward in _rewards)
        {
            var center = GetCellCenter(reward.Key);
            DrawCircle(center, Mathf.Max(2.0f, _hexSize * 0.16f), new Color(0.18f, 0.12f, 0.05f, 0.36f));
            DrawMarker(center, _hexSize * 0.58f, GetRewardColor(reward.Value), 0.0f);
        }
    }

    private Color GetRewardColor(RewardType rewardType)
    {
        return rewardType switch
        {
            RewardType.GrowthModule => new Color(0.90f, 0.83f, 0.28f, 0.96f),
            RewardType.CommandReload => new Color(0.88f, 0.67f, 0.21f, 0.96f),
            RewardType.FirepowerUpgrade => new Color(1.0f, 0.55f, 0.22f, 0.96f),
            RewardType.CardCalibration => new Color(0.93f, 0.74f, 0.31f, 0.96f),
            _ => new Color(0.90f, 0.83f, 0.28f, 0.96f),
        };
    }

    private Vector2 DefaultDisplayPosition(object key)
    {
        return key switch
        {
            EnemyState enemy => GetCellCenter(enemy.Coord),
            ProjectileState projectile => GetCellCenter(projectile.Coord),
            string s when s.StartsWith("fleet_") && int.TryParse(s.AsSpan(6), out var idx) && idx < _fleet.Segments.Count
                => GetCellCenter(_fleet.Segments[idx].Coord),
            _ => Vector2.Zero,
        };
    }

    private void DrawEnemies()
    {
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            DrawEnemyBody(enemy);
            DrawEnemyIntent(enemy);
        }
    }

    private void DrawEnemyBody(EnemyState enemy)
    {
        var center = _coordinator.GetPosition(enemy);
        var rotation = 0.0f;
        if (enemy.AttackIntentDirection.HasValue)
        {
            rotation = DirectionToAngle(enemy.AttackIntentDirection.Value);
        }
        else if (enemy.MoveIntentDirection.HasValue)
        {
            rotation = DirectionToAngle(enemy.MoveIntentDirection.Value);
        }

        DrawMarker(center, _hexSize * GetEnemyBodyScale(enemy.Type), GetEnemyColor(enemy.Type), rotation);
        DrawEnemyHealth(enemy, center);
    }

    private float GetEnemyBodyScale(EnemyType type)
    {
        return type switch
        {
            EnemyType.Mine => 0.80f,
            EnemyType.Splitter => 1.02f,
            EnemyType.Artillery => 0.92f,
            _ => 0.88f,
        };
    }

    private Color GetEnemyColor(EnemyType type)
    {
        return type switch
        {
            EnemyType.Charger => new Color(0.90f, 0.31f, 0.29f, 0.95f),
            EnemyType.Artillery => new Color(0.95f, 0.54f, 0.25f, 0.95f),
            EnemyType.Mine => new Color(0.70f, 0.76f, 0.80f, 0.95f),
            EnemyType.Splitter => new Color(0.72f, 0.24f, 0.20f, 0.95f),
            _ => new Color(0.90f, 0.31f, 0.29f, 0.95f),
        };
    }

    private void DrawEnemyHealth(EnemyState enemy, Vector2 center)
    {
        for (var pipIndex = 0; pipIndex < enemy.Health; pipIndex++)
        {
            var offset = new Vector2((pipIndex - ((enemy.Health - 1) * 0.5f)) * _hexSize * 0.18f, -_hexSize * 0.45f);
            DrawCircle(center + offset, Mathf.Max(2.0f, _hexSize * 0.08f), new Color(1.0f, 0.96f, 0.90f, 0.92f));
        }
    }

    private void DrawEnemyIntent(EnemyState enemy)
    {
        var basePos = _coordinator.GetPosition(enemy);
        var center = basePos + new Vector2(0.0f, -_hexSize * 0.78f);

        if (enemy.MoveIntentDirection.HasValue)
        {
            DrawMarker(
                center + new Vector2(-_hexSize * 0.24f, 0.0f),
                _hexSize * 0.30f,
                new Color(0.78f, 0.90f, 1.0f, 0.94f),
                DirectionToAngle(enemy.MoveIntentDirection.Value));
        }

        if (enemy.AttackIntentDirection.HasValue)
        {
            DrawMarker(
                center + new Vector2(_hexSize * 0.24f, 0.0f),
                _hexSize * 0.32f,
                new Color(1.0f, 0.86f, 0.44f, 0.94f),
                DirectionToAngle(enemy.AttackIntentDirection.Value));
        }

        if (!enemy.HasRadialAttackIntent)
        {
            return;
        }

        for (var direction = 0; direction < HexCoord.Directions.Length; direction++)
        {
            var radialOffset = _grid.ToWorld(HexCoord.Directions[direction], _hexSize * 0.34f);
            DrawMarker(
                basePos + radialOffset,
                _hexSize * 0.18f,
                new Color(1.0f, 0.92f, 0.52f, 0.88f),
                DirectionToAngle(direction));
        }
    }

    private void DrawProjectiles(IReadOnlyList<ProjectileState> projectiles, Color tint, float scale)
    {
        foreach (var projectile in projectiles)
        {
            var center = _coordinator.GetPosition(projectile);
            DrawCombatIconAt(center, scale, tint, projectile.Direction);
        }
    }

    private void DrawCombatIconAt(Vector2 center, float scale, Color tint, int direction)
    {
        DrawMarker(center, _hexSize * scale, tint, DirectionToAngle(direction));
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

    private void DrawAnimatedFleet()
    {
        for (var index = _fleet.Segments.Count - 1; index >= 1; index--)
        {
            var segmentKey = $"fleet_{index}";
            Vector2 center;
            if (_coordinator.HasOverride(segmentKey))
            {
                center = _coordinator.GetPosition(segmentKey);
            }
            else if (!_animManager.TryGetSegmentPosition(index, out center))
            {
                continue;
            }

            var segment = _fleet.Segments[index];
            DrawMarker(center, _hexSize * 0.95f, new Color(0.38f, 0.72f, 0.96f, 1.0f), 0.0f);
            DrawDirectionIndicator(center, segment.EntryDirection, new Color(1.0f, 1.0f, 1.0f, 0.9f));
        }

        const string headKey = "fleet_0";
        Vector2 headCenter;
        if (_coordinator.HasOverride(headKey))
        {
            headCenter = _coordinator.GetPosition(headKey);
        }
        else if (!_animManager.TryGetSegmentPosition(0, out headCenter))
        {
            return;
        }

        var rotation = DirectionToAngle(_fleet.HeadDirection);
        DrawMarker(headCenter, _hexSize * 1.08f, new Color(1.0f, 0.78f, 0.25f, 1.0f), rotation);
        DrawDirectionIndicator(headCenter, _fleet.HeadDirection, new Color(0.12f, 0.16f, 0.24f, 1.0f));
    }

    private void DrawActiveEffects()
    {
        foreach (var effect in _animManager.GetActiveEffects())
        {
            switch (effect.Kind)
            {
                case AnimKind.Flash:
                    DrawFlashEffect(effect);
                    break;
                case AnimKind.Explosion:
                    DrawExplosionEffect(effect);
                    break;
                case AnimKind.Pickup:
                    DrawPickupEffect(effect);
                    break;
                case AnimKind.CannonFire:
                    DrawCannonFireEffect(effect);
                    break;
            }
        }
    }

    private void DrawFlashEffect(ActiveEffect effect)
    {
        var progress = effect.Progress;
        var radius = effect.Radius * (0.5f + (progress * 0.5f));
        var alpha = 1.0f - progress;
        DrawCircle(effect.Position, radius, new Color(effect.Tint.R, effect.Tint.G, effect.Tint.B, alpha));
    }

    private void DrawExplosionEffect(ActiveEffect effect)
    {
        var progress = effect.Progress;
        var radius = effect.Radius * progress;
        var alpha = 1.0f - progress;
        var color = new Color(0.97f, 0.45f, 0.22f, alpha * 0.8f);
        DrawCircle(effect.Position, radius, color);
        DrawCircle(effect.Position, radius * 0.6f, new Color(1.0f, 0.85f, 0.35f, alpha * 0.6f));
    }

    private void DrawPickupEffect(ActiveEffect effect)
    {
        var progress = effect.Progress;
        var alpha = 1.0f - (progress * progress);
        var offsetY = -_hexSize * 0.8f * progress;
        if (ThemeDB.FallbackFont != null)
        {
            DrawString(ThemeDB.FallbackFont, effect.Position + new Vector2(0, offsetY), effect.FloatingText,
                HorizontalAlignment.Center, -1, Mathf.Max(10, Mathf.RoundToInt(_baseCardFontSize * _uiScale)),
                new Color(0.98f, 0.86f, 0.32f, alpha));
        }
    }

    private void DrawCannonFireEffect(ActiveEffect effect)
    {
        var progress = effect.Progress;
        var dirIndex = HexCoord.WrapDirection(effect.Direction);
        var dirVec = _grid.ToWorld(HexCoord.Directions[dirIndex], 1.0f).Normalized();
        var perpendicular = new Vector2(-dirVec.Y, dirVec.X);

        var beamLength = _hexSize * 1.4f;
        var beamProgress = Mathf.Min(progress * 2.0f, 1.0f);
        var beamCurrentLength = beamLength * beamProgress;
        var beamEnd = effect.Position + (dirVec * beamCurrentLength);
        var beamAlpha = (1.0f - progress) * 0.75f;
        DrawLine(effect.Position, beamEnd, new Color(effect.Tint.R, effect.Tint.G, effect.Tint.B, beamAlpha),
            Mathf.Max(1.5f, _hexSize * 0.07f), true);

        var flashRadius = effect.Radius * (0.6f + (progress * 0.4f));
        var flashAlpha = (1.0f - (progress * progress)) * 0.8f;
        var fanHalfAngle = 0.55f;
        var fanSegments = 6;
        var fanPoints = new Vector2[fanSegments + 2];
        fanPoints[0] = effect.Position;
        for (var i = 0; i <= fanSegments; i++)
        {
            var t = -fanHalfAngle + ((fanHalfAngle * 2.0f * i) / fanSegments);
            fanPoints[i + 1] = effect.Position + dirVec.Rotated(t) * flashRadius;
        }

        DrawColoredPolygon(fanPoints, new Color(effect.Tint.R, effect.Tint.G, effect.Tint.B, flashAlpha));

        var coreRadius = flashRadius * 0.4f;
        var coreAlpha = (1.0f - progress) * 0.9f;
        DrawCircle(effect.Position, coreRadius, new Color(1.0f, 1.0f, 1.0f, coreAlpha * 0.7f));
    }

    private void DrawFleet(FleetState fleet, bool isGhost, float alpha)
    {
        for (var index = fleet.Segments.Count - 1; index >= 1; index--)
        {
            var segment = fleet.Segments[index];
            var segmentKey = $"fleet_{index}";
            var center = _coordinator.GetPosition(segmentKey);
            var tint = isGhost
                ? new Color(0.65f, 0.85f, 1.0f, alpha)
                : new Color(0.38f, 0.72f, 0.96f, alpha);

            DrawMarker(center, _hexSize * 0.95f, tint, 0.0f);
            DrawDirectionIndicator(center, segment.EntryDirection, new Color(1.0f, 1.0f, 1.0f, alpha * 0.9f));
        }

        var head = fleet.Segments[0];
        var headKey = "fleet_0";
        var headCenter = _coordinator.GetPosition(headKey);
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

    private static string DescribeEnemyType(EnemyType type)
    {
        return type switch
        {
            EnemyType.Charger => "突击梭",
            EnemyType.Artillery => "炮击哨",
            EnemyType.Mine => "断脊水雷",
            EnemyType.Splitter => "分裂冲角",
            _ => "敌舰",
        };
    }

    private static string DescribeRewardType(RewardType type)
    {
        return type switch
        {
            RewardType.GrowthModule => "增殖模块",
            RewardType.CommandReload => "指令重载",
            RewardType.FirepowerUpgrade => "火力强化",
            RewardType.CardCalibration => "指令校准",
            _ => "补给",
        };
    }

    private static void AppendRewardEvents(List<string> destination, IReadOnlyList<string> source)
    {
        foreach (var item in source)
        {
            destination.Add(item);
        }
    }

    private static string BuildRewardSummary(List<string> rewardEvents)
    {
        return rewardEvents.Count == 0
            ? "本回合未发生补给变化。"
            : string.Join(" ", rewardEvents);
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

    private enum RewardType
    {
        GrowthModule,
        CommandReload,
        FirepowerUpgrade,
        CardCalibration,
    }

    private sealed class SimulationResult
    {
        private SimulationResult(FleetState fleet, List<HexCoord> headPath, List<string> rewardEvents, CollisionKind collisionKind, HexCoord? collisionCoord)
        {
            Fleet = fleet.Clone();
            HeadPath = headPath.AsReadOnly();
            RewardEvents = rewardEvents.AsReadOnly();
            CollisionKind = collisionKind;
            CollisionCoord = collisionCoord;
        }

        public FleetState Fleet { get; }

        public IReadOnlyList<HexCoord> HeadPath { get; }

        public IReadOnlyList<string> RewardEvents { get; }

        public CollisionKind CollisionKind { get; }

        public HexCoord? CollisionCoord { get; }

        public bool IsFatal => CollisionKind != CollisionKind.None;

        public static SimulationResult Success(FleetState fleet, List<HexCoord> headPath, List<string> rewardEvents)
        {
            return new SimulationResult(fleet, headPath, rewardEvents, CollisionKind.None, null);
        }

        public static SimulationResult Fatal(FleetState fleet, List<HexCoord> headPath, List<string> rewardEvents, CollisionKind collisionKind, HexCoord collisionCoord)
        {
            return new SimulationResult(fleet, headPath, rewardEvents, collisionKind, collisionCoord);
        }
    }
}
