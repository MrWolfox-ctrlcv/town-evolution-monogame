namespace Town.Core.Residents;

/// <summary>第一阶段直接指派：只做"走到目标旁"，劳动在后续步骤接入。</summary>
public static class NpcTasks
{
    public static bool Assign(World world, NpcState npc, NpcTask task, out string message)
    {
        npc.Task = task;
        npc.PathFailed = false;
        if (TryPath(world, npc, task, out message)) return true;
        npc.Status = message;
        npc.Task = null;
        return false;
    }

    /// <summary>每 tick 维护任务：到达、重算路径或报告无路。</summary>
    public static void Tick(World world, NpcState npc)
    {
        var task = npc.Task;
        if (task is null || task.Kind == NpcTaskKind.None) return;

        if (npc.CellX == task.TargetX && npc.CellZ == task.TargetZ && !npc.HasRemainingPath)
        {
            npc.Activity = NpcActivity.Idle;
            npc.Status = "已到达 " + task.Label;
            npc.Task = null;
            return;
        }

        if (!npc.HasRemainingPath)
        {
            if (!TryPath(world, npc, task, out string reason))
            {
                npc.Status = reason;
                npc.Task = null;
            }
        }
        else if (npc.Activity == NpcActivity.Blocked)
        {
            npc.Activity = NpcActivity.Walking;
            TryPath(world, npc, task, out _);
        }
    }

    private static bool TryPath(World world, NpcState npc, NpcTask task, out string message)
    {
        foreach (var candidate in Goals(world, task))
        {
            var result = NpcNavigation.FindPath(world, npc.CellX, npc.CellZ, candidate.X, candidate.Z);
            if (result.Found)
            {
                npc.Path = result.Cells;
                npc.PathIndex = 0;
                npc.PathFailed = false;
                npc.Activity = NpcActivity.Walking;
                npc.Status = "前往 " + task.Label;
                message = npc.Status;
                return true;
            }
            if (result.TooFar) break;
        }
        message = "无路可走：到不了 " + task.Label;
        return false;
    }

    /// <summary>目标本身与它四周的可站立格，按直线距离排序。</summary>
    private static IEnumerable<Cell> Goals(World world, NpcTask task)
    {
        if (NpcNavigation.Passable(world, task.TargetX, task.TargetZ)) yield return new Cell(task.TargetX, task.TargetZ);
        foreach (var cell in NpcNavigation.ApproachCells(world, task.TargetX, task.TargetZ)) yield return cell;
    }
}
