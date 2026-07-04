using System;
using System.Collections.Generic;

public sealed class FleetState
{
    private readonly List<FleetSegmentState> _segments;

    public FleetState(IEnumerable<FleetSegmentState> segments, int headDirection)
    {
        _segments = new List<FleetSegmentState>();
        foreach (var segment in segments)
        {
            _segments.Add(segment.Clone());
        }

        if (_segments.Count == 0)
        {
            throw new ArgumentException("Fleet must have at least one segment.");
        }

        HeadDirection = HexCoord.WrapDirection(headDirection);
    }

    public IReadOnlyList<FleetSegmentState> Segments => _segments;

    public int HeadDirection { get; set; }

    public HexCoord Head => _segments[0].Coord;

    public FleetState Clone()
    {
        return new FleetState(_segments, HeadDirection);
    }

    public bool Occupies(HexCoord coord)
    {
        foreach (var segment in _segments)
        {
            if (segment.Coord.Equals(coord))
            {
                return true;
            }
        }

        return false;
    }

    public void MoveOneStep(HexCoord nextHead, HexGrid grid)
    {
        var previousCoords = new HexCoord[_segments.Count];

        for (var index = 0; index < _segments.Count; index++)
        {
            previousCoords[index] = _segments[index].Coord;
        }

        _segments[0].Coord = nextHead;
        _segments[0].EntryDirection = HeadDirection;

        for (var index = 1; index < _segments.Count; index++)
        {
            _segments[index].Coord = previousCoords[index - 1];

            if (grid.TryGetDirection(previousCoords[index], _segments[index].Coord, out var entryDirection))
            {
                _segments[index].EntryDirection = entryDirection;
            }
        }
    }

    public bool HasDuplicateCoords()
    {
        var occupied = new HashSet<HexCoord>();

        foreach (var segment in _segments)
        {
            if (!occupied.Add(segment.Coord))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryAddSegment(HexGrid grid, Func<HexCoord, bool>? isBlocked = null)
    {
        var tailIndex = _segments.Count - 1;
        var tail = _segments[tailIndex];
        var preferredDirection = HexCoord.WrapDirection(tail.EntryDirection + 3);
        isBlocked ??= static _ => false;

        for (var offset = 0; offset < HexCoord.Directions.Length; offset++)
        {
            var direction = HexCoord.WrapDirection(preferredDirection + offset);
            var candidate = tail.Coord.Step(direction);
            if (!grid.Contains(candidate) || Occupies(candidate) || isBlocked(candidate))
            {
                continue;
            }

            var newSegment = new FleetSegmentState(candidate)
            {
                EntryDirection = tail.EntryDirection,
            };
            _segments.Add(newSegment);
            return true;
        }

        return false;
    }
}

public sealed class FleetSegmentState
{
    public FleetSegmentState(HexCoord coord)
    {
        Coord = coord;
        Range = 3;
        Damage = 1;
    }

    public HexCoord Coord { get; set; }

    public int EntryDirection { get; set; }

    public int Range { get; set; }

    public int Damage { get; set; }

    public int ScatterLevel { get; set; }

    public bool HasExplosive { get; set; }

    public FleetSegmentState Clone()
    {
        return new FleetSegmentState(Coord)
        {
            EntryDirection = EntryDirection,
            Range = Range,
            Damage = Damage,
            ScatterLevel = ScatterLevel,
            HasExplosive = HasExplosive,
        };
    }
}