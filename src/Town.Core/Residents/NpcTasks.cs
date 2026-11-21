namespace Town.Core.Residents;

public static class NpcTasks
{
    public static bool CanStaff(string recipe) => recipe is not ("grow-sapling" or "make-fertilizer") && World.Processing.ContainsKey(recipe);

    public static bool SetWorkplace(World world, NpcState npc, int buildingId, string recipe, out string message)
    {
        if (!CanStaff(recipe) || world.Objects.All(b => b.Id != buildingId || b.Kind != World.Processing[recipe].Building))
        {
            message = "这座建筑没有可指派的岗位";
            return false;
        }
        if (world.Npcs.All.Any(other => other != npc && other.Workplace?.BuildingId == buildingId))
        {
            message = "这座工坊已有居民负责";
            return false;
        }
        npc.ClearTask();
        npc.Workplace = new(buildingId, recipe);
        npc.NextHaulTick = world.Clock;
        message = npc.Status = "已分配工坊岗位";
        return true;
    }

    public static bool Assign(World world, NpcState npc, NpcTask task, out string message)
    {
        if (task.Kind == NpcTaskKind.Process && CanStaff(task.Item)
            && !World.HasCargoSupplies(npc, task.Item) && !world.HasAttachedSupplies(task.SourceId, task.Item))
        {
            if (world.CanSupplyResident(npc, task.Item) && TryRoute(world, npc, task, out _, out _))
                foreach (var warehouse in Warehouses(world, npc))
                    if (Assign(world, npc, new(NpcTaskKind.FetchSupplies, warehouse.X, warehouse.Z, "领取原料", warehouse.Id,
                            Automatic: task.Automatic, Item: task.Item, DestinationId: task.SourceId), out message))
                        return true;
            message = world.CanSupplyResident(npc, task.Item) ? "找不到到工坊或仓库的路" : "原料不足或负重已满";
            return false;
        }
        if (task.Kind == NpcTaskKind.ParkCart && (!npc.HasCart || npc.CarriedKg > NpcConfig.MaxCarryKg + 1e-8))
        {
            message = "请先卸货再停放手推车";
            return false;
        }
        if (task.Kind == NpcTaskKind.EquipCart && npc.HasCart)
        {
            message = "居民已经在用手推车";
            return false;
        }
        if (task.Kind == NpcTaskKind.Dig && world.Preview(DigCommand(task)) is { Ok: false } invalid)
        {
            message = invalid.Message;
            return false;
        }
        if (!TryRoute(world, npc, task, out var goal, out var path))
        {
            message = $"无路可走：{task.Label}";
            return false;
        }
        npc.ClearTask();
        npc.Task = task;
        npc.Goal = goal;
        npc.Path = path;
        npc.Activity = NpcActivity.Walking;
        message = npc.Status = $"前往 {task.Label}";
        return true;
    }

    internal static bool Tick(World world, NpcState npc)
    {
        UpdateNeeds(world, npc);
        if (npc.Task is not { } task)
        {
            if (world.Clock < npc.NextHaulTick ||
                world.Clock % NpcConfig.TicksPerSecond != npc.Id % NpcConfig.TicksPerSecond) return false;
            npc.NextHaulTick = world.Clock + NpcConfig.TicksPerSecond;
            if (!AssignNeeds(world, npc) && !AssignWorkplace(world, npc) && npc.AutoHaul) AssignHauling(world, npc);
            return false;
        }

        if (npc.PathFailed || npc.Goal is null || !npc.HasRemainingPath && npc.Cell != npc.Goal.Value)
        {
            if (!Assign(world, npc, task, out var reason)) Finish(npc, reason);
            return false;
        }
        if (npc.HasRemainingPath) return false;
        if (task.Kind == NpcTaskKind.GoTo)
        {
            Finish(npc, $"已到达 {task.Label}");
            return false;
        }
        if (!Goals(world, task).Contains(npc.Cell))
        {
            Finish(npc, "工作目标已变化");
            return false;
        }

        if (task.Kind == NpcTaskKind.Rest)
        {
            npc.Activity = NpcActivity.Resting;
            npc.Status = "休息中";
            if (npc.Stamina >= NpcConfig.StaminaRestTarget) Finish(npc, "休息好了");
            return false;
        }

        if (task.Kind == NpcTaskKind.FetchSupplies) return FetchSupplies(world, npc, task);

        FaceTarget(npc, task);
        bool doesWork = task.Kind is NpcTaskKind.Chop or NpcTaskKind.Harvest or NpcTaskKind.Dig or NpcTaskKind.Process;
        if (doesWork)
        {
            if (npc.WorkReadyTick == 0)
            {
                npc.WorkReadyTick = world.Clock + (long)(NpcConfig.TicksPerSecond / NpcConfig.WorkSpeed);
                npc.Activity = NpcActivity.Working;
                npc.Status = task.Label;
            }
            if (world.Clock < npc.WorkReadyTick) return false;
        }
        var result = task.Kind switch
        {
            NpcTaskKind.PickupDrop or NpcTaskKind.PickupPile => world.LoadResident(npc, task),
            NpcTaskKind.PickupFarmBox => world.LoadResidentFromFarm(npc, task.SourceId),
            NpcTaskKind.Store => world.StoreResident(npc),
            NpcTaskKind.Chop => world.Execute(new("tool", task.TargetX, task.TargetZ, "axe")),
            NpcTaskKind.Harvest => world.Execute(new("tool", task.TargetX, task.TargetZ, "sickle",
                ObjectId: task.SourceId, FarmCell: task.FarmCell)),
            NpcTaskKind.Eat => world.FeedResident(npc),
            NpcTaskKind.Dig => world.DigWithResident(DigCommand(task)),
            NpcTaskKind.Process => world.StartResidentProcessing(npc, task),
            NpcTaskKind.PickupJob => world.LoadResidentFromJob(npc, task.SourceId),
            NpcTaskKind.EquipCart => world.EquipResidentCart(npc, task.SourceId),
            NpcTaskKind.ParkCart => world.ParkResidentCart(npc),
            _ => new Result(false, "工作目标已变化")
        };
        Finish(npc, result.Ok && doesWork && task.Kind != NpcTaskKind.Process ? "已完成工作" : result.Message);
        return result.Ok;
    }

    private static WorldCommand DigCommand(NpcTask task) => new("terrainDig", task.TargetX, task.TargetZ, "diggingTool");

    private static NpcTask ProcessAt(PlacedObject building, string recipe, bool automatic)
        => new(NpcTaskKind.Process, building.X, building.Z, "加工 " + World.ItemNames[World.Processing[recipe].Outputs[0].Item],
            building.Id, Automatic: automatic, Item: recipe);

    private static bool FetchSupplies(World world, NpcState npc, NpcTask task)
    {
        var destination = world.Objects.FirstOrDefault(o => o.Id == task.DestinationId);
        if (destination is null || !CanStaff(task.Item) || destination.Kind != World.Processing[task.Item].Building
            || world.Jobs.Any(j => j.Id == destination.Id))
        {
            Finish(npc, "工坊工作已变化");
            return false;
        }
        var work = ProcessAt(destination, task.Item, task.Automatic);
        if (world.HasAttachedSupplies(destination.Id, task.Item))
        {
            if (!Assign(world, npc, work, out var reason)) Finish(npc, reason);
            return false;
        }
        if (!TryRoute(world, npc, work, out _, out _))
        {
            Finish(npc, "找不到到工坊的路");
            return false;
        }
        var result = world.LoadResidentSupplies(npc, task.Item);
        if (!result.Ok) Finish(npc, result.Message);
        else if (!Assign(world, npc, work, out var reason)) Finish(npc, reason);
        return result.Ok;
    }

    private static bool AssignWorkplace(World world, NpcState npc)
    {
        if (npc.Workplace is not { } work) return false;
        var building = world.Objects.FirstOrDefault(o => o.Id == work.BuildingId);
        if (building is null || building.Kind != World.Processing[work.Recipe].Building)
        {
            npc.Workplace = null;
            npc.Status = "工坊已变化";
            return false;
        }
        var job = world.Jobs.FirstOrDefault(j => j.Id == building.Id);
        bool needsCargo = job is null && World.HasCargoSupplies(npc, work.Recipe);
        if (npc.Cargo.Count > 0 && !needsCargo)
        {
            if (world.Capacity <= world.UsedStorage) return false;
            return AssignStorage(world, npc);
        }
        if (job is not null)
        {
            if (job.ReadyTick > world.Clock) return false;
            if (world.HasReadyResidentJob(npc, building.Id))
                return Assign(world, npc, new(NpcTaskKind.PickupJob, building.X, building.Z, "领取成品", building.Id, Automatic: true), out _);
            npc.Status = "仓库空间不足，成品留在工坊";
            return false;
        }
        if (Assign(world, npc, ProcessAt(building, work.Recipe, true), out var reason)) return true;
        npc.Status = reason;
        return false;
    }

    private static void UpdateNeeds(World world, NpcState npc)
    {
        long elapsed = world.Clock - npc.Id;
        if (elapsed <= 0) return;
        if (elapsed % (NpcConfig.TicksPerSecond * 60 / NpcConfig.HungerDrainPerMinute) == 0)
            npc.Hunger = Math.Max(0, npc.Hunger - 1);
        if (elapsed % NpcConfig.TicksPerSecond != 0) return;
        int change = npc.Activity switch
        {
            NpcActivity.Walking => elapsed % NpcConfig.WalkDrainInterval == 0 ? -1 : 0,
            NpcActivity.Working => -NpcConfig.WorkDrainPerSecond,
            NpcActivity.Resting => NpcConfig.StaminaRestPerSecond,
            _ => 1
        };
        npc.Stamina = Math.Clamp(npc.Stamina + change, 0, NpcConfig.StaminaMax);
    }

    private static bool AssignNeeds(World world, NpcState npc)
    {
        if (npc.Hunger < NpcConfig.HungerSeekThreshold && world.HasResidentFood)
            foreach (var warehouse in Warehouses(world, npc))
                if (Assign(world, npc, new(NpcTaskKind.Eat, warehouse.X, warehouse.Z, "找食物", warehouse.Id, Automatic: true), out _))
                    return true;

        if (npc.Stamina >= NpcConfig.StaminaThresholdLow) return false;
        var home = world.Objects.FirstOrDefault(o => o.Id == npc.Home && o.Kind is "house" or "residence");
        if (home is not null && Assign(world, npc, new(NpcTaskKind.Rest, home.X, home.Z, "回家休息", home.Id, Automatic: true), out _))
            return true;
        if (!NpcNavigation.Passable(world, npc.CellX, npc.CellZ)) return false;
        return Assign(world, npc, new(NpcTaskKind.Rest, npc.CellX, npc.CellZ, "原地休息", Automatic: true), out _);
    }

    private static IEnumerable<PlacedObject> Warehouses(World world, NpcState npc)
        => world.Objects.Where(o => World.StorageCapacity(o.Kind) > 0)
            .OrderBy(o => Distance(npc, o.X, o.Z)).ThenBy(o => o.Id).Take(6);

    private static void AssignHauling(World world, NpcState npc)
    {
        if (world.UsedStorage >= world.Capacity)
        {
            if (npc.Cargo.Count != 0) npc.Status = "仓库已满，物品随身保留";
            return;
        }
        if (npc.Cargo.Count != 0)
        {
            if (world.Capacity - world.UsedStorage > npc.Cargo.Values.Sum() && TryPickNearby(world, npc, 3)) return;
            AssignStorage(world, npc);
            return;
        }
        if (TryPickNearby(world, npc, 24)) return;
        foreach (var job in world.Objects.Where(o => Distance(npc, o.X, o.Z) <= 24
                     && world.HasReadyResidentJob(npc, o.Id) && !AlreadyClaimed(world, NpcTaskKind.PickupJob, o.Id)
                     && !world.Npcs.All.Any(other => other.Workplace?.BuildingId == o.Id))
                 .OrderBy(o => Distance(npc, o.X, o.Z)).ThenBy(o => o.Id).Take(6))
            if (Assign(world, npc, new(NpcTaskKind.PickupJob, job.X, job.Z, "领取成品", job.Id, Automatic: true), out _))
                return;
        foreach (var farm in world.Objects.Where(o => o.Kind == "farm" && Distance(npc, o.X, o.Z) <= 24
                     && world.Farm(o.Id)?.BoxStock?.Values.Any(amount => amount > 0) == true
                     && !AlreadyClaimed(world, NpcTaskKind.PickupFarmBox, o.Id))
                 .OrderBy(o => Distance(npc, o.X, o.Z)).ThenBy(o => o.Id).Take(6))
            if (Assign(world, npc, new(NpcTaskKind.PickupFarmBox, farm.X, farm.Z, "领取田箱", farm.Id, Automatic: true), out _))
                return;
    }

    private static bool AssignStorage(World world, NpcState npc)
    {
        foreach (var storage in Warehouses(world, npc))
            if (Assign(world, npc, new(NpcTaskKind.Store, storage.X, storage.Z, "存放物品", storage.Id, Automatic: true), out _))
                return true;
        npc.Status = "找不到可到达的仓库";
        return false;
    }

    private static bool TryPickNearby(World world, NpcState npc, int radius)
    {
        var candidates = world.Drops.Where(d => Distance(npc, d.X, d.Z) <= radius
            && NpcConfig.WeightOf(d.Item) <= npc.CarryCapacityKg - npc.CarriedKg
            && !AlreadyClaimed(world, NpcTaskKind.PickupDrop, d.Id));
        foreach (var drop in candidates.OrderBy(d => Distance(npc, d.X, d.Z)).ThenBy(d => d.Id).Take(6))
            if (Assign(world, npc, new(NpcTaskKind.PickupDrop, drop.X, drop.Z, "拾取 " + World.ItemNames[drop.Item],
                    drop.Id, Automatic: true), out _))
                return true;
        return false;
    }

    private static int Distance(NpcState npc, int x, int z) => Math.Abs(npc.CellX - x) + Math.Abs(npc.CellZ - z);
    private static bool AlreadyClaimed(World world, NpcTaskKind kind, int sourceId)
        => world.Npcs.All.Any(other => other.Task is { } pending && pending.Kind == kind && pending.SourceId == sourceId);

    private static bool TryRoute(World world, NpcState npc, NpcTask task, out Cell goal, out List<Cell> path)
    {
        foreach (var candidate in Goals(world, task).OrderBy(cell => Distance(npc, cell.X, cell.Z)))
        {
            if (task.Kind is not (NpcTaskKind.GoTo or NpcTaskKind.PickupDrop or NpcTaskKind.PickupPile or NpcTaskKind.ParkCart)
                && !(task.Kind == NpcTaskKind.Rest && task.SourceId == 0) && world.Npcs.All.Any(other => other != npc &&
                    (other.Goal == candidate || !other.HasRemainingPath && other.Cell == candidate)))
                continue;
            var result = NpcNavigation.FindPath(world, npc.CellX, npc.CellZ, candidate.X, candidate.Z, npc.HasCart);
            if (result.Found)
            {
                goal = candidate;
                path = result.Cells;
                return true;
            }
            if (result.TooFar) break;
        }
        goal = default;
        path = [];
        return false;
    }

    private static IEnumerable<Cell> Goals(World world, NpcTask task)
    {
        var cell = new Cell(task.TargetX, task.TargetZ);
        if (!World.Inside(cell.X, cell.Z)) return [];
        switch (task.Kind)
        {
            case NpcTaskKind.GoTo:
                return NpcNavigation.Passable(world, cell.X, cell.Z) ? [cell]
                    : NpcNavigation.ApproachCells(world, cell.X, cell.Z);
            case NpcTaskKind.PickupDrop:
                return NpcNavigation.Passable(world, cell.X, cell.Z)
                    && world.Drops.Any(d => d.Id == task.SourceId && d.X == cell.X && d.Z == cell.Z) ? [cell] : [];
            case NpcTaskKind.PickupPile:
                return NpcNavigation.Passable(world, cell.X, cell.Z)
                    && world.Piles.Any(p => p.Id == task.SourceId && p.X == cell.X && p.Z == cell.Z) ? [cell] : [];
            case NpcTaskKind.Chop:
                return world.TreeMature(cell.X, cell.Z) && world.LiveTrees.Any(t => t.X == cell.X && t.Z == cell.Z)
                    ? NpcNavigation.WorkCells(world, cell, 1, 1) : [];
            case NpcTaskKind.Dig:
                return world.Height(cell.X, cell.Z) > 0 ? NpcNavigation.WorkCells(world, cell, 1, 1) : [];
            case NpcTaskKind.ParkCart:
            case NpcTaskKind.Rest when task.SourceId == 0:
                return NpcNavigation.Passable(world, cell.X, cell.Z) ? [cell] : [];
            case NpcTaskKind.Rest:
            case NpcTaskKind.EquipCart:
            case NpcTaskKind.Eat:
            case NpcTaskKind.FetchSupplies:
            case NpcTaskKind.Process:
            case NpcTaskKind.PickupJob:
            case NpcTaskKind.Store:
            case NpcTaskKind.Harvest:
            case NpcTaskKind.PickupFarmBox:
                var building = world.Occupant(cell.X, cell.Z);
                if (building?.Id != task.SourceId) return [];
                if (task.Kind is NpcTaskKind.Store or NpcTaskKind.Eat or NpcTaskKind.FetchSupplies && World.StorageCapacity(building.Kind) == 0) return [];
                if (task.Kind == NpcTaskKind.Rest && building.Kind is not ("residence" or "house")) return [];
                if (task.Kind == NpcTaskKind.EquipCart && building.Kind != "handcart") return [];
                if (task.Kind == NpcTaskKind.Process && (!CanStaff(task.Item) || World.Processing[task.Item].Building != building.Kind
                    || world.Jobs.Any(job => job.Id == building.Id))) return [];
                if (task.Kind == NpcTaskKind.PickupJob && !world.Jobs.Any(job => job.Id == building.Id && job.ReadyTick <= world.Clock)) return [];
                if (task.Kind == NpcTaskKind.Harvest && (task.FarmCell is < 0 or >= 9
                    || world.Farm(building.Id)?.Cells[task.FarmCell] is not { } crop || crop.ReadyTick > world.Clock)) return [];
                if (task.Kind == NpcTaskKind.PickupFarmBox && world.Farm(building.Id)?.BoxStock?.Values.Any(amount => amount > 0) != true) return [];
                var (width, depth) = World.Footprint(building.Kind, building.Rotation);
                return NpcNavigation.WorkCells(world, new(building.X, building.Z), width, depth);
            default:
                return [];
        }
    }

    private static void FaceTarget(NpcState npc, NpcTask task)
    {
        float x = task.TargetX + .5f - npc.X, z = task.TargetZ + .5f - npc.Z;
        if (x != 0 || z != 0) npc.Facing = MathF.Atan2(x, z);
    }

    private static void Finish(NpcState npc, string status)
    {
        npc.ClearTask();
        npc.Status = status;
    }
}
