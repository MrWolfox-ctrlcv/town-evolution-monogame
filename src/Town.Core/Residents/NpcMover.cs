namespace Town.Core.Residents;

/// <summary>单个居民每 tick 的位置推进。不改世界，只改自身状态。</summary>
public static class NpcMover
{
    /// <summary>推进一个模拟 tick，返回是否移动过。</summary>
    public static bool Tick(World world, NpcState npc)
    {
        if (!npc.HasRemainingPath)
        {
            SettleHeight(world, npc);
            return false;
        }

        var next = npc.Path[npc.PathIndex];
        if (NpcNavigation.StepCost(world, npc.CellX, npc.CellZ, next.X, next.Z) is null)
        {
            // 地形或建筑改变导致路径失效：原地清空，由上层重新寻路。
            npc.Path.Clear();
            npc.PathIndex = 0;
            npc.PathFailed = true;
            npc.Activity = NpcActivity.Blocked;
            npc.Status = "前路被挡";
            SettleHeight(world, npc);
            return false;
        }

        float targetX = next.X + .5f, targetZ = next.Z + .5f;
        float dx = targetX - npc.X, dz = targetZ - npc.Z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        float speed = npc.SpeedCellsPerSecond;
        if (npc.HasCart)
        {
            bool road = world.Occupant(npc.CellX, npc.CellZ)?.Kind == "road" && world.Occupant(next.X, next.Z)?.Kind == "road";
            speed = (road ? NpcConfig.CartRoadSpeed : NpcConfig.CartGroundSpeed) * (1 - NpcConfig.LoadSlowdown * (float)npc.LoadRatio);
        }
        float budget = speed / NpcConfig.TicksPerSecond;

        if (distance <= budget || distance < 1e-4f)
        {
            npc.X = targetX; npc.Z = targetZ;
            npc.PathIndex++;
        }
        else
        {
            npc.X += dx / distance * budget;
            npc.Z += dz / distance * budget;
            if (distance > 1e-4f) npc.Facing = MathF.Atan2(dx, dz);
        }

        SettleHeight(world, npc);
        if (!npc.HasRemainingPath)
        {
            npc.Path.Clear();
            npc.PathIndex = 0;
        }
        return true;
    }

    /// <summary>渲染高度平滑跟随所站地面，台阶不会瞬移。</summary>
    private static void SettleHeight(World world, NpcState npc)
    {
        float ground = world.Height(npc.CellX, npc.CellZ) * World.Step;
        npc.Y += (ground - npc.Y) * NpcConfig.HeightLerp;
        if (MathF.Abs(ground - npc.Y) < .01f) npc.Y = ground;
    }
}
