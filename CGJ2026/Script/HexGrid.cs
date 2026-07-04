using System;
using System.Collections.Generic;

using Godot;

public sealed class HexGrid
{
    public const int DefaultRadius = 12;

    private readonly HashSet<HexCoord> _legalCells;

    public HexGrid(int radius)
    {
        Radius = radius;
        _legalCells = BuildLegalCells(radius);
    }

    public int Radius { get; }

    public IReadOnlyCollection<HexCoord> LegalCells => _legalCells;

    public bool Contains(HexCoord coord)
    {
        return _legalCells.Contains(coord);
    }

    public bool IsEdge(HexCoord coord)
    {
        return Math.Max(Math.Abs(coord.Q), Math.Max(Math.Abs(coord.R), Math.Abs(coord.S))) == Radius;
    }

    public IEnumerable<HexCoord> GetNeighbors(HexCoord coord)
    {
        for (var direction = 0; direction < HexCoord.Directions.Length; direction++)
        {
            yield return coord.Step(direction);
        }
    }

    public Vector2 ToWorld(HexCoord coord, float hexSize)
    {
        var x = hexSize * Mathf.Sqrt(3.0f) * (coord.Q + (coord.R * 0.5f));
        var y = hexSize * 1.5f * coord.R;
        return new Vector2(x, y);
    }

    public int RotateDirection(int direction, int delta)
    {
        return HexCoord.WrapDirection(direction + delta);
    }

    public bool TryGetDirection(HexCoord from, HexCoord to, out int direction)
    {
        var delta = to - from;

        for (var index = 0; index < HexCoord.Directions.Length; index++)
        {
            if (HexCoord.Directions[index].Equals(delta))
            {
                direction = index;
                return true;
            }
        }

        direction = 0;
        return false;
    }

    public HashSet<HexCoord> CreateConnectedReefs(RandomNumberGenerator random, IReadOnlyCollection<HexCoord> reservedCells, int clusterCount, int clusterMinSize, int clusterMaxSize)
    {
        var reefs = new HashSet<HexCoord>();
        var reserve = new HashSet<HexCoord>(reservedCells);

        for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            var targetSize = random.RandiRange(clusterMinSize, clusterMaxSize);
            var cluster = CreateSingleReefCluster(random, reserve, reefs, targetSize);

            foreach (var cell in cluster)
            {
                reefs.Add(cell);
            }
        }

        return reefs;
    }

    private HashSet<HexCoord> CreateSingleReefCluster(RandomNumberGenerator random, HashSet<HexCoord> reserve, HashSet<HexCoord> reefs, int targetSize)
    {
        var startCandidates = new List<HexCoord>();
        foreach (var cell in _legalCells)
        {
            if (CanPlaceReefCell(cell, reserve, reefs, allowTouchingCluster: null))
            {
                startCandidates.Add(cell);
            }
        }

        if (startCandidates.Count == 0)
        {
            throw new InvalidOperationException("Unable to place an isolated reef cluster.");
        }

        var cluster = new HashSet<HexCoord>();
        var start = startCandidates[random.RandiRange(0, startCandidates.Count - 1)];
        cluster.Add(start);

        while (cluster.Count < targetSize)
        {
            var expansionCandidates = new List<HexCoord>();
            foreach (var source in cluster)
            {
                foreach (var neighbor in GetNeighbors(source))
                {
                    if (CanPlaceReefCell(neighbor, reserve, reefs, cluster))
                    {
                        expansionCandidates.Add(neighbor);
                    }
                }
            }

            if (expansionCandidates.Count == 0)
            {
                throw new InvalidOperationException("Unable to grow a reef cluster to the requested size.");
            }

            var next = expansionCandidates[random.RandiRange(0, expansionCandidates.Count - 1)];
            cluster.Add(next);
        }

        return cluster;
    }

    private bool CanPlaceReefCell(HexCoord cell, HashSet<HexCoord> reserve, HashSet<HexCoord> reefs, HashSet<HexCoord>? allowTouchingCluster)
    {
        if (!Contains(cell) || reserve.Contains(cell) || reefs.Contains(cell))
        {
            return false;
        }

        foreach (var neighbor in GetNeighbors(cell))
        {
            if (reefs.Contains(neighbor))
            {
                return false;
            }

            if (allowTouchingCluster == null)
            {
                continue;
            }

            if (!allowTouchingCluster.Contains(neighbor))
            {
                continue;
            }
        }

        return true;
    }

    private static HashSet<HexCoord> BuildLegalCells(int radius)
    {
        var cells = new HashSet<HexCoord>();

        for (var q = -radius; q <= radius; q++)
        {
            for (var r = -radius; r <= radius; r++)
            {
                var s = -q - r;
                if (Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(s))) <= radius)
                {
                    cells.Add(new HexCoord(q, r, s));
                }
            }
        }

        return cells;
    }

    private static void Shuffle<T>(IList<T> values, RandomNumberGenerator random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var swapIndex = random.RandiRange(0, index);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}