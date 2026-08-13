namespace ScheduleOneBlueprintImporter.Blueprints;

public static class GridRegionLayoutPlanner
{
    public static bool TryCreateCompositeLayout(
        BlueprintFloor floor,
        IReadOnlyList<PlannedPlacement> placements,
        IReadOnlyList<GridShape> grids,
        out IReadOnlyList<GridRegionLayout>? regions)
    {
        regions = null;
        if (grids.Count < 2 || placements.Count < grids.Count)
            return false;

        int innerWidth = floor.Width - 2;
        int innerHeight = floor.Height - 2;
        var candidatesByGrid = new List<List<Candidate>>();
        foreach (GridShape grid in grids)
        {
            var candidates = new List<Candidate>();
            var dimensions = new List<(int Width, int Height)> { (grid.Width, grid.Height) };
            if (grid.Width != grid.Height)
                dimensions.Add((grid.Height, grid.Width));

            foreach (var dimension in dimensions)
            {
                if (dimension.Width > innerWidth || dimension.Height > innerHeight)
                    continue;
                for (int offsetY = 0; offsetY <= innerHeight - dimension.Height; offsetY++)
                {
                    for (int offsetX = 0; offsetX <= innerWidth - dimension.Width; offsetX++)
                    {
                        List<int> contained = Enumerable.Range(0, placements.Count)
                            .Where(index => Contains(
                                placements[index], offsetX, offsetY, dimension.Width, dimension.Height))
                            .ToList();
                        if (contained.Count == 0)
                            continue;

                        int buildableCells = 0;
                        for (int y = offsetY + 1; y < offsetY + dimension.Height + 1; y++)
                        {
                            for (int x = offsetX + 1; x < offsetX + dimension.Width + 1; x++)
                            {
                                if (!string.Equals(floor.Blueprint[y][x], "-1", StringComparison.Ordinal))
                                    buildableCells++;
                            }
                        }
                        candidates.Add(new Candidate(
                            grid.Index, offsetX, offsetY, dimension.Width, dimension.Height,
                            buildableCells, contained));
                    }
                }
            }

            candidatesByGrid.Add(candidates
                .OrderByDescending(candidate => candidate.BuildableCells)
                .ThenBy(candidate => candidate.OffsetY)
                .ThenBy(candidate => candidate.OffsetX)
                .ToList());
        }

        if (candidatesByGrid.Any(candidates => candidates.Count == 0))
            return false;

        var selected = new List<Candidate>();
        var coveredPlacements = new HashSet<int>();
        List<Candidate>? best = null;
        int bestScore = int.MinValue;
        double bestTopologyError = double.MaxValue;

        void Search(int gridPosition, int score)
        {
            if (gridPosition == grids.Count)
            {
                if (coveredPlacements.Count != placements.Count)
                    return;

                double topologyError = TopologyError(selected, grids);
                if (topologyError < bestTopologyError - 0.000001d ||
                    (Math.Abs(topologyError - bestTopologyError) <= 0.000001d && score > bestScore))
                {
                    bestTopologyError = topologyError;
                    bestScore = score;
                    best = selected.ToList();
                }
                return;
            }

            foreach (Candidate candidate in candidatesByGrid[gridPosition])
            {
                if (selected.Any(existing => Overlaps(existing, candidate)) ||
                    candidate.PlacementIndexes.Any(coveredPlacements.Contains))
                {
                    continue;
                }

                selected.Add(candidate);
                foreach (int index in candidate.PlacementIndexes)
                    coveredPlacements.Add(index);
                Search(gridPosition + 1, score + candidate.BuildableCells);
                foreach (int index in candidate.PlacementIndexes)
                    coveredPlacements.Remove(index);
                selected.RemoveAt(selected.Count - 1);
            }
        }

        Search(0, 0);
        if (best == null)
            return false;

        regions = best
            .Select(candidate => new GridRegionLayout(
                candidate.GridIndex,
                candidate.OffsetX,
                candidate.OffsetY,
                candidate.Width,
                candidate.Height,
                candidate.PlacementIndexes
                    .Select(index => placements[index] with
                    {
                        GridX = placements[index].GridX - candidate.OffsetX,
                        GridY = placements[index].GridY - candidate.OffsetY,
                        OriginX = placements[index].OriginX - candidate.OffsetX,
                        OriginY = placements[index].OriginY - candidate.OffsetY,
                    })
                    .ToList()))
            .OrderBy(region => region.OffsetY)
            .ThenBy(region => region.OffsetX)
            .ToList();
        return true;
    }

    private static bool Contains(
        PlannedPlacement placement,
        int offsetX,
        int offsetY,
        int width,
        int height) =>
        placement.GridX >= offsetX && placement.GridY >= offsetY &&
        placement.GridX + placement.Source.Width <= offsetX + width &&
        placement.GridY + placement.Source.Height <= offsetY + height;

    private static bool Overlaps(Candidate left, Candidate right) =>
        left.OffsetX < right.OffsetX + right.Width &&
        right.OffsetX < left.OffsetX + left.Width &&
        left.OffsetY < right.OffsetY + right.Height &&
        right.OffsetY < left.OffsetY + left.Height;

    private static double TopologyError(
        IReadOnlyList<Candidate> candidates,
        IReadOnlyList<GridShape> grids)
    {
        var logicalDistances = new List<double>();
        var nativeDistances = new List<double>();
        for (int left = 0; left < candidates.Count; left++)
        {
            for (int right = left + 1; right < candidates.Count; right++)
            {
                Candidate leftCandidate = candidates[left];
                Candidate rightCandidate = candidates[right];
                GridShape leftGrid = grids.First(grid => grid.Index == leftCandidate.GridIndex);
                GridShape rightGrid = grids.First(grid => grid.Index == rightCandidate.GridIndex);
                double logicalX = leftCandidate.OffsetX + leftCandidate.Width / 2d -
                                  (rightCandidate.OffsetX + rightCandidate.Width / 2d);
                double logicalY = leftCandidate.OffsetY + leftCandidate.Height / 2d -
                                  (rightCandidate.OffsetY + rightCandidate.Height / 2d);
                double nativeX = leftGrid.CenterX - rightGrid.CenterX;
                double nativeY = leftGrid.CenterY - rightGrid.CenterY;
                logicalDistances.Add(Math.Sqrt(logicalX * logicalX + logicalY * logicalY));
                nativeDistances.Add(Math.Sqrt(nativeX * nativeX + nativeY * nativeY));
            }
        }

        double logicalMagnitude = Math.Sqrt(logicalDistances.Sum(distance => distance * distance));
        double nativeMagnitude = Math.Sqrt(nativeDistances.Sum(distance => distance * distance));
        if (logicalMagnitude <= 0.000001d || nativeMagnitude <= 0.000001d)
            return 0d;

        double error = 0d;
        for (int index = 0; index < logicalDistances.Count; index++)
        {
            double delta = logicalDistances[index] / logicalMagnitude -
                           nativeDistances[index] / nativeMagnitude;
            error += delta * delta;
        }
        return error;
    }

    private sealed record Candidate(
        int GridIndex,
        int OffsetX,
        int OffsetY,
        int Width,
        int Height,
        int BuildableCells,
        IReadOnlyList<int> PlacementIndexes);
}

public sealed record GridShape(
    int Index,
    int Width,
    int Height,
    double CenterX = 0d,
    double CenterY = 0d);

public sealed record GridRegionLayout(
    int GridIndex,
    int OffsetX,
    int OffsetY,
    int Width,
    int Height,
    IReadOnlyList<PlannedPlacement> Placements);
