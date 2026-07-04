using System;
using System.Collections.Generic;

using Godot;

public sealed class EntityDisplayCoordinator
{
    private readonly Dictionary<object, Vector2> _overrides = new();
    private readonly Func<object, Vector2>? _defaultProvider;

    public EntityDisplayCoordinator(Func<object, Vector2>? defaultProvider = null)
    {
        _defaultProvider = defaultProvider;
    }

    public void SetOverride(object key, Vector2 position)
    {
        _overrides[key] = position;
    }

    public void ClearOverride(object key)
    {
        _overrides.Remove(key);
    }

    public Vector2 GetPosition(object key)
    {
        if (_overrides.TryGetValue(key, out var pos))
            return pos;
        if (_defaultProvider != null)
            return _defaultProvider(key);
        return Vector2.Zero;
    }

    public bool HasOverride(object key)
    {
        return _overrides.ContainsKey(key);
    }

    public void ClearAllOverrides()
    {
        _overrides.Clear();
    }
}
