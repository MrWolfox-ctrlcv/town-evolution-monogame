namespace Town.Core.Residents;

/// <summary>地块通行判定与 A* 寻路。只读 World，不改变任何状态。</summary>
public static class NpcNavigation
{
    /// <summary>该格能否站人：陆地、无建筑占位（土路可以走）、无天然植被、无已种树。</summary>
    public static bool Passable(World world, int x, int z)
    {
        if (!World.Inside(x, z)) return false;
        if (world.Height(x, z) <= 0) return false;
        if (world.Occupant(x, z) is { } occupant && occupant.Kind != "road") return false;
        if (world.HasNature(x, z)) return false;
        return true;
    }

    /// <summary>四方向步进的代价；不可达返回 null。相邻高度差最多一级。</summary>
    public static float? StepCost(World world, int fromX, int fromZ, int toX, int toZ)
    {
        if (!Passable(world, toX, toZ)) return null;
        int delta = world.Height(toX, toZ) - world.Height(fromX, fromZ);
        if (delta > NpcConfig.MaxStepUp || delta < -NpcConfig.MaxStepUp) return null;
        return delta > 0 ? NpcConfig.CostStepUp : delta < 0 ? NpcConfig.CostStepDown : NpcConfig.CostFlat;
    }

    private static readonly (int Dx, int Dz)[] Neighbours = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    public sealed record PathResult(List<Cell> Cells, bool Found, bool TooFar, int Expanded);

    /// <summary>A*，四方向，曼哈顿启发式（乘以最小步进代价，保持可采纳）。</summary>
    public static PathResult FindPath(World world, int startX, int startZ, int goalX, int goalZ, bool preferRoads = false)
    {
        var empty = new List<Cell>();
        if (startX == goalX && startZ == goalZ) return new(empty, true, false, 0);
        if (!World.Inside(startX, startZ) || !World.Inside(goalX, goalZ)) return new(empty, false, false, 0);

        int size = World.Size;
        var open = new PriorityQueue<int, float>();
        var gScore = new Dictionary<int, float>(1024);
        var cameFrom = new Dictionary<int, int>(1024);
        var closed = new HashSet<int>();

        int start = World.Index(startX, startZ), goal = World.Index(goalX, goalZ);
        gScore[start] = 0;
        open.Enqueue(start, Heuristic(startX, startZ, goalX, goalZ));
        int expanded = 0;

        while (open.Count > 0)
        {
            if (expanded++ > NpcConfig.MaxPathNodes) return new(empty, false, true, expanded);
            int current = open.Dequeue();
            if (!closed.Add(current)) continue;
            if (current == goal) return new(Rebuild(cameFrom, current), true, false, expanded);

            int cx = current % size - World.Half, cz = current / size - World.Half;
            float cg = gScore[current];
            foreach (var (dx, dz) in Neighbours)
            {
                int nx = cx + dx, nz = cz + dz;
                if (StepCost(world, cx, cz, nx, nz) is not float step) continue;
                if (preferRoads && world.Occupant(nx, nz)?.Kind != "road") step *= 1.25f;
                int next = World.Index(nx, nz);
                if (closed.Contains(next)) continue;
                float tentative = cg + step;
                if (gScore.TryGetValue(next, out var known) && known <= tentative) continue;
                gScore[next] = tentative;
                cameFrom[next] = current;
                open.Enqueue(next, tentative + Heuristic(nx, nz, goalX, goalZ));
            }
        }
        return new(empty, false, false, expanded);
    }

    private static float Heuristic(int x, int z, int gx, int gz) =>
        (Math.Abs(gx - x) + Math.Abs(gz - z)) * NpcConfig.CostFlat;

    private static List<Cell> Rebuild(Dictionary<int, int> cameFrom, int current)
    {
        var path = new List<Cell>();
        while (cameFrom.TryGetValue(current, out int previous))
        {
            path.Add(new Cell(current % World.Size - World.Half, current / World.Size - World.Half));
            current = previous;
        }
        path.Reverse();
        return path;
    }

    /// <summary>目标点周围可站立、可交互的格子；按距离由近到远返回。</summary>
    public static IEnumerable<Cell> ApproachCells(World world, int originX, int originZ, int radius = 2)
    {
        var cells = new List<(Cell Cell, int Distance)>();
        for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int x = originX + dx, z = originZ + dz;
                if (!Passable(world, x, z)) continue;
                cells.Add((new Cell(x, z), Math.Abs(dx) + Math.Abs(dz)));
            }
        return cells.OrderBy(c => c.Distance).Select(c => c.Cell);
    }

    internal static IEnumerable<Cell> WorkCells(World world, Cell anchor, int width, int depth)
    {
        int east = anchor.X + width - 1, south = anchor.Z + depth - 1;
        for (int x = anchor.X; x <= east; x++)
        {
            if (StepCost(world, x, anchor.Z, x, anchor.Z - 1) is not null) yield return new(x, anchor.Z - 1);
            if (StepCost(world, x, south, x, south + 1) is not null) yield return new(x, south + 1);
        }
        for (int z = anchor.Z; z <= south; z++)
        {
            if (StepCost(world, anchor.X, z, anchor.X - 1, z) is not null) yield return new(anchor.X - 1, z);
            if (StepCost(world, east, z, east + 1, z) is not null) yield return new(east + 1, z);
        }
    }
}
