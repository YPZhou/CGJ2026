using System;
using System.Collections.Generic;

using Godot;

public sealed class AnimationManager
{
    private readonly Queue<AnimationEvent> _queue = new();
    private readonly List<ActiveEffect> _effects = new();
    private readonly List<Vector2> _segmentPositions = new();
    private readonly List<object> _managedEntityKeys = new();
    private readonly EntityDisplayCoordinator _coordinator;
    private readonly Func<float> _getHexSize;
    private Tween? _tween;
    private AnimationEvent? _current;
    private float _currentT;

    public bool IsPlaying { get; private set; }

    public event Action? AllComplete;

    public event Action? RedrawRequested;

    public event Action<AnimKind>? PhaseEntered;

    public AnimationManager(EntityDisplayCoordinator coordinator, Func<float> getHexSize)
    {
        _coordinator = coordinator;
        _getHexSize = getHexSize;
    }

    public void EnqueueFleetStep(IReadOnlyList<Vector2> fromPositions, IReadOnlyList<Vector2> toPositions, float duration)
    {
        _queue.Enqueue(new AnimationEvent
        {
            Kind = AnimKind.FleetStep,
            Duration = duration,
            FleetStep = new FleetStepAnim
            {
                FromPositions = new List<Vector2>(fromPositions),
                ToPositions = new List<Vector2>(toPositions),
            },
        });
    }

    public void EnqueueFlash(Vector2 worldPosition, Color tint, float radius, float duration)
    {
        _queue.Enqueue(new AnimationEvent
        {
            Kind = AnimKind.Flash,
            Duration = duration,
            WorldPosition = worldPosition,
            Radius = radius,
            Tint = tint,
        });
    }

    public void EnqueueExplosion(Vector2 worldPosition, float radius, float duration)
    {
        _queue.Enqueue(new AnimationEvent
        {
            Kind = AnimKind.Explosion,
            Duration = duration,
            WorldPosition = worldPosition,
            Radius = radius,
        });
    }

    public void EnqueuePickup(Vector2 worldPosition, string floatingText, float duration)
    {
        _queue.Enqueue(new AnimationEvent
        {
            Kind = AnimKind.Pickup,
            Duration = duration,
            WorldPosition = worldPosition,
            FloatingText = floatingText,
        });
    }

    public void EnqueueParallelMove(IReadOnlyList<EntityMove> moves, float duration)
    {
        foreach (var move in moves)
        {
            _coordinator.SetOverride(move.EntityKey, move.FromWorld);
        }

        _queue.Enqueue(new AnimationEvent
        {
            Kind = AnimKind.ProjectileMove,
            Duration = duration,
            EntityMoves = new List<EntityMove>(moves),
        });
    }

    public void EnqueueCannonFire(Vector2 worldPos, int direction, float duration, float radius, Color tint, object? entityKey, int segmentIndex)
    {
        EnqueueCannonFire(worldPos, new[] { direction }, duration, radius, tint, entityKey, segmentIndex);
    }

    public void EnqueueCannonFire(Vector2 worldPos, IReadOnlyList<int> fireDirections, float duration, float radius, Color tint, object? entityKey, int segmentIndex)
    {
        _queue.Enqueue(new AnimationEvent
        {
            Kind = AnimKind.CannonFire,
            Duration = duration,
            WorldPosition = worldPos,
            Radius = radius,
            Tint = tint,
            Direction = fireDirections.Count > 0 ? fireDirections[0] : 0,
            FireDirections = new List<int>(fireDirections),
            CannonEntityKey = entityKey,
            SegmentIndex = segmentIndex,
        });
    }

    public void EnqueueCannonFireBatch(IReadOnlyList<AnimationEvent> fireEvents)
    {
        if (fireEvents.Count == 0)
        {
            return;
        }

        var duration = 0.0f;
        var batch = new List<AnimationEvent>(fireEvents.Count);

        foreach (var fireEvent in fireEvents)
        {
            batch.Add(fireEvent);
            duration = Mathf.Max(duration, fireEvent.Duration);
        }

        _queue.Enqueue(new AnimationEvent
        {
            Kind = AnimKind.CannonFire,
            Duration = duration,
            BatchedEvents = batch,
        });
    }

    public void Start(Node parent)
    {
        if (IsPlaying || _queue.Count == 0)
        {
            return;
        }

        IsPlaying = true;
        PlayNext(parent);
    }

    public void SetPaused(bool paused)
    {
        if (_tween == null || !GodotObject.IsInstanceValid(_tween))
        {
            return;
        }

        if (paused)
        {
            _tween.Pause();
            return;
        }

        _tween.Play();
    }

    public void SkipAll()
    {
        KillTween();
        _queue.Clear();
        _effects.Clear();
        _coordinator.ClearAllOverrides();
        _managedEntityKeys.Clear();
        _segmentPositions.Clear();
        _current = null;
        _currentT = 0f;
        IsPlaying = false;
        AllComplete?.Invoke();
    }

    public void Reset()
    {
        KillTween();
        _queue.Clear();
        _effects.Clear();
        _coordinator.ClearAllOverrides();
        _managedEntityKeys.Clear();
        _segmentPositions.Clear();
        _current = null;
        _currentT = 0f;
        IsPlaying = false;
    }

    public bool TryGetSegmentPosition(int index, out Vector2 position)
    {
        if (index < 0 || index >= _segmentPositions.Count)
        {
            position = Vector2.Zero;
            return false;
        }

        position = _segmentPositions[index];
        return true;
    }

    public IReadOnlyList<ActiveEffect> GetActiveEffects()
    {
        return _effects;
    }

    public void Update(float delta)
    {
        for (var index = _effects.Count - 1; index >= 0; index--)
        {
            _effects[index].Elapsed += delta;

            if (_effects[index].Kind == AnimKind.CannonFire)
            {
                var effect = _effects[index];
                if (effect.RecoilEntityKey != null)
                {
                    var recoilOffset = CalculateRecoilOffset(effect.FireDirections, effect.Direction, effect.Progress);
                    _coordinator.SetOverride(effect.RecoilEntityKey, effect.Position + recoilOffset);
                }
            }

            if (_effects[index].IsExpired)
            {
                if (_effects[index].Kind == AnimKind.CannonFire && _effects[index].RecoilEntityKey is { } recoilKey)
                {
                    _coordinator.ClearOverride(recoilKey);
                }

                _effects.RemoveAt(index);
            }
        }
    }

    private void PlayNext(Node parent)
    {
        KillTween();

        foreach (var key in _managedEntityKeys)
        {
            _coordinator.ClearOverride(key);
        }

        _managedEntityKeys.Clear();

        if (_queue.Count == 0)
        {
            _effects.Clear();
            _coordinator.ClearAllOverrides();
            _managedEntityKeys.Clear();
            _segmentPositions.Clear();
            IsPlaying = false;
            AllComplete?.Invoke();
            return;
        }

        _current = _queue.Dequeue();
        _currentT = 0f;

        switch (_current.Kind)
        {
            case AnimKind.FleetStep:
                SpawnFleetEffect(_current);
                break;
            case AnimKind.EnemyMove:
            case AnimKind.ProjectileMove:
                SpawnParallelMoveEffect(_current);
                break;
            case AnimKind.Flash:
            case AnimKind.Explosion:
            case AnimKind.Pickup:
                SpawnEffect(_current);
                break;
            case AnimKind.CannonFire:
                SpawnCannonFireEffects(_current);
                break;
        }

        PhaseEntered?.Invoke(_current!.Kind);

        _tween = parent.CreateTween();

        if (_current.Kind is AnimKind.FleetStep or AnimKind.EnemyMove or AnimKind.ProjectileMove)
        {
            _tween.TweenMethod(Callable.From<float>(OnProgressTween), 0.0f, 1.0f, _current.Duration);
        }
        else
        {
            _tween.TweenMethod(Callable.From<float>(RedrawTick), 0.0f, 1.0f, _current.Duration);
        }

        _tween.TweenCallback(Callable.From(() => PlayNext(parent)));
    }

    private void OnProgressTween(float t)
    {
        _currentT = t;

        switch (_current?.Kind)
        {
            case AnimKind.FleetStep:
                UpdateSegmentInterpolations();
                break;
            case AnimKind.EnemyMove:
            case AnimKind.ProjectileMove:
                UpdateParallelMoveInterpolations();
                break;
        }

        RedrawRequested?.Invoke();
    }

    private void RedrawTick(float _)
    {
        RedrawRequested?.Invoke();
    }

    private void UpdateSegmentInterpolations()
    {
        if (_current?.FleetStep == null)
        {
            return;
        }

        var step = _current.FleetStep.Value;
        _segmentPositions.Clear();

        for (var index = 0; index < step.FromPositions.Count; index++)
        {
            var from = step.FromPositions[index];
            var to = step.ToPositions[index];
            _segmentPositions.Add(from.Lerp(to, _currentT));
        }
    }

    private void UpdateParallelMoveInterpolations()
    {
        if (_current?.EntityMoves == null)
        {
            return;
        }

        foreach (var move in _current.EntityMoves)
        {
            _coordinator.SetOverride(move.EntityKey, move.FromWorld.Lerp(move.ToWorld, _currentT));
        }
    }

    private void SpawnFleetEffect(AnimationEvent anim)
    {
        if (anim.FleetStep == null)
        {
            return;
        }

        var step = anim.FleetStep.Value;
        _segmentPositions.Clear();
        for (var index = 0; index < step.FromPositions.Count; index++)
        {
            _segmentPositions.Add(step.FromPositions[index]);
        }
    }

    private void SpawnParallelMoveEffect(AnimationEvent anim)
    {
        if (anim.EntityMoves == null)
        {
            return;
        }

        foreach (var move in anim.EntityMoves)
        {
            _coordinator.SetOverride(move.EntityKey, move.FromWorld);
            _managedEntityKeys.Add(move.EntityKey);
        }

        RedrawRequested?.Invoke();
    }

    private void SpawnCannonFireEffects(AnimationEvent anim)
    {
        if (anim.BatchedEvents == null)
        {
            SpawnEffect(anim);
            return;
        }

        foreach (var fireEvent in anim.BatchedEvents)
        {
            SpawnEffect(fireEvent);
        }
    }

    private void SpawnEffect(AnimationEvent anim)
    {
        var cannonKey = anim.Kind == AnimKind.CannonFire ? GetCannonEntityKey(anim) : null;
        var fireDirections = anim.FireDirections ?? new List<int>();

        _effects.Add(new ActiveEffect
        {
            Kind = anim.Kind,
            Position = anim.WorldPosition,
            Duration = anim.Duration,
            Radius = anim.Radius,
            Tint = anim.Tint,
            FloatingText = anim.FloatingText,
            Direction = anim.Direction,
            FireDirections = fireDirections,
            RecoilEntityKey = cannonKey,
        });

        if (cannonKey != null)
        {
            var recoilOffset = CalculateRecoilOffset(fireDirections, anim.Direction, 0f);
            _coordinator.SetOverride(cannonKey, anim.WorldPosition + recoilOffset);
        }

        RedrawRequested?.Invoke();
    }

    private static object? GetCannonEntityKey(AnimationEvent anim)
    {
        if (anim.CannonEntityKey != null)
        {
            return anim.CannonEntityKey;
        }

        if (anim.SegmentIndex >= 0)
        {
            return $"fleet_{anim.SegmentIndex}";
        }

        return null;
    }

    private Vector2 CalculateRecoilOffset(IReadOnlyList<int> fireDirections, int fallbackDirection, float progress)
    {
        var directionVector = Vector2.Zero;

        if (fireDirections.Count == 0)
        {
            directionVector = HexDirectionToVector(fallbackDirection);
        }
        else
        {
            foreach (var direction in fireDirections)
            {
                directionVector += HexDirectionToVector(direction);
            }

            if (directionVector.LengthSquared() <= 0.0001f)
            {
                return Vector2.Zero;
            }

            directionVector = directionVector.Normalized();
        }

        var maxRecoil = _getHexSize() * 0.2f;

        float recoilFraction;
        if (progress < 0.15f)
        {
            recoilFraction = -(1.0f - (progress / 0.15f));
        }
        else if (progress < 0.35f)
        {
            recoilFraction = -1.0f + ((progress - 0.15f) / 0.2f);
        }
        else if (progress < 0.5f)
        {
            recoilFraction = 0.0f + (0.06f * (progress - 0.35f) / 0.15f);
        }
        else
        {
            recoilFraction = 0.06f * (1.0f - (progress - 0.5f) / 0.5f);
        }

        return directionVector * maxRecoil * recoilFraction;
    }

    private static Vector2 HexDirectionToVector(int direction)
    {
        var wrapped = HexCoord.WrapDirection(direction);
        var coord = HexCoord.Directions[wrapped];
        var x = Mathf.Sqrt(3.0f) * (coord.Q + (coord.R * 0.5f));
        var y = 1.5f * coord.R;
        return new Vector2(x, y).Normalized();
    }

    private void KillTween()
    {
        if (_tween != null && GodotObject.IsInstanceValid(_tween))
        {
            _tween.Kill();
        }

        _tween = null;
    }
}
