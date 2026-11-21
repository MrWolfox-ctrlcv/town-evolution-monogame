using Town.Core.Residents;

namespace Town.Core;

public sealed partial class World
{
    public (Cell Cell, long Tick)? LastResidentDeposit { get; private set; }
    internal List<Cell> ResidentDigs { get; } = [];

    internal Result DigWithResident(WorldCommand command)
    {
        var result = Execute(command);
        if (result.Ok) ResidentDigs.Add(new(command.X, command.Z));
        return result;
    }

    internal Result EquipResidentCart(NpcState npc, int cartId)
    {
        if (npc.HasCart || !byId.TryGetValue(cartId, out var cart) || cart.Kind != "handcart")
            return new(false, "手推车已被取用");
        if (play.Attachments.Any(a => a.Id == cartId && a.Stock.Values.Any(amount => amount > 0)))
            return new(false, "请先取回手推车上的挂载材料");
        RemoveBuilding(cart);
        npc.CartId = cart.Id;
        return new(true, "已取用手推车");
    }

    internal Result ParkResidentCart(NpcState npc)
    {
        if (!npc.HasCart || npc.CarriedKg > NpcConfig.MaxCarryKg + 1e-8)
            return new(false, "请先卸货再停放手推车");
        if (objects.Count >= 400) return new(false, "物件数量已达上限");
        int rotation = ((int)MathF.Round(npc.Facing / (MathF.PI / 2)) + 4) % 4;
        var neighbors = NpcNavigation.WorkCells(this, npc.Cell, 1, 1);
        foreach (var cell in neighbors)
        {
            if (Npcs.All.Any(other => other.Cell == cell || other.NextCell == cell) ||
                !CanPlace("handcart", cell.X, cell.Z, rotation).Ok) continue;
            AddObject(new(npc.CartId, "handcart", cell.X, cell.Z, rotation));
            npc.CartId = 0;
            return new(true, "已停放手推车");
        }
        return new(false, "旁边没有可停放手推车的空地");
    }

    internal Result LoadResident(NpcState npc, NpcTask task)
    {
        var drop = task.Kind == NpcTaskKind.PickupDrop ? play.Drops.Find(d => d.Id == task.SourceId) : null;
        var pile = task.Kind == NpcTaskKind.PickupPile ? play.Piles.Find(p => p.Id == task.SourceId) : null;
        var item = drop?.Item ?? pile?.Item;
        if (item is null) return new(false, "物品已被收集");
        var amount = Math.Min(drop?.Amount ?? pile!.Amount, ResidentLoadLimit(npc, item, task.Automatic));
        if (amount <= 0) return new(false, task.Automatic && UsedStorage >= Capacity ? "仓库已满" : "负重已满");
        if (drop is not null)
        {
            int index = play.Drops.IndexOf(drop);
            if (drop.Amount == amount) play.Drops.RemoveAt(index);
            else play.Drops[index] = drop with { Amount = drop.Amount - amount };
        }
        else ConsumePile(pile!, amount);

        AddResidentCargo(npc, item, amount);
        return new(true, $"已拾取 {ItemNames[item]} ×{amount}");
    }

    internal Result LoadResidentFromFarm(NpcState npc, int farmId)
    {
        int index = play.Farms.FindIndex(f => f.Id == farmId);
        if (index < 0 || play.Farms[index] is not { Box: true, BoxStock: { Count: > 0 } contents } farm)
            return new(false, "田箱里没有收获");
        var remaining = new Dictionary<string, int>(contents);
        int loaded = 0;
        foreach (var (item, quantity) in contents)
        {
            int accepted = Math.Min(quantity, ResidentLoadLimit(npc, item, true));
            if (accepted <= 0) continue;
            AddResidentCargo(npc, item, accepted);
            if (accepted == quantity) remaining.Remove(item);
            else remaining[item] -= accepted;
            loaded += accepted;
        }
        if (loaded == 0) return new(false, "负重或仓库空间不足");
        play.Farms[index] = farm with { BoxStock = remaining };
        return new(true, $"已取田箱 {loaded} 份");
    }

    internal bool HasAttachedSupplies(int buildingId, string recipe)
    {
        var stock = play.Attachments.Find(a => a.Id == buildingId)?.Stock;
        return stock is not null && Processing[recipe].Inputs.All(input => stock.GetValueOrDefault(input.Item) >= input.Amount);
    }

    internal static bool HasCargoSupplies(NpcState npc, string recipe)
        => Processing[recipe].Inputs.All(input => npc.Cargo.GetValueOrDefault(input.Item) >= input.Amount);

    public bool CanSupplyResident(NpcState npc, string recipe)
    {
        if (!Processing.TryGetValue(recipe, out var rule)) return false;
        var missing = rule.Inputs.Select(input => (input.Item, Amount: Math.Max(0, input.Amount - npc.Cargo.GetValueOrDefault(input.Item)))).ToArray();
        return missing.All(input => Stock(input.Item) >= input.Amount)
            && missing.Sum(input => input.Amount * NpcConfig.WeightOf(input.Item)) + npc.CarriedKg <= npc.CarryCapacityKg + 1e-8;
    }

    internal Result LoadResidentSupplies(NpcState npc, string recipe)
    {
        if (!CanSupplyResident(npc, recipe)) return new(false, "原料不足或负重已满");
        foreach (var (item, amount) in Processing[recipe].Inputs)
        {
            int missing = amount - npc.Cargo.GetValueOrDefault(item);
            if (missing <= 0) continue;
            Take(item, missing);
            AddResidentCargo(npc, item, missing);
        }
        return new(true, "已领取原料");
    }

    internal Result StartResidentProcessing(NpcState npc, NpcTask task)
    {
        bool attached = HasAttachedSupplies(task.SourceId, task.Item);
        if (!attached && !HasCargoSupplies(npc, task.Item)) return new(false, "工坊缺少原料");
        var candidate = Fork();
        try
        {
            candidate.StartProcessing(candidate.Building(task.SourceId), task.Item, attached, !attached);
        }
        catch (RuleFailure error)
        {
            return new(false, error.Message);
        }
        Adopt(candidate);
        if (!attached)
            foreach (var (item, amount) in Processing[task.Item].Inputs)
            {
                if (npc.CargoItems[item] == amount) npc.CargoItems.Remove(item);
                else npc.CargoItems[item] -= amount;
            }
        return new(true, "工坊开始加工");
    }

    internal bool HasResidentFood => NpcConfig.Food.Any(food => Stock(food.Item) > 0);

    internal Result FeedResident(NpcState npc)
    {
        foreach (var (item, filling) in NpcConfig.Food)
        {
            if (Stock(item) == 0) continue;
            Take(item, 1);
            npc.Hunger = Math.Min(NpcConfig.HungerMax, npc.Hunger + filling);
            return new(true, $"已吃 {ItemNames[item]}");
        }
        return new(false, "仓库没有食物");
    }

    internal bool HasReadyResidentJob(NpcState npc, int buildingId)
        => play.Jobs.Find(job => job.Id == buildingId && job.ReadyTick <= Clock) is { } job && CanLoadJob(npc, job);

    private bool CanLoadJob(NpcState npc, WorkJob job)
    {
        var outputs = Processing[job.Recipe].Outputs;
        return outputs.Sum(p => p.Amount * NpcConfig.WeightOf(p.Item)) <= npc.CarryCapacityKg - npc.CarriedKg + 1e-8
            && outputs.Sum(p => p.Amount) <= Capacity - UsedStorage - npc.Cargo.Values.Sum();
    }

    internal Result LoadResidentFromJob(NpcState npc, int buildingId)
    {
        var job = play.Jobs.Find(j => j.Id == buildingId && j.ReadyTick <= Clock);
        if (job is null) return new(false, "没有可领取的成品");
        if (!CanLoadJob(npc, job)) return new(false, "负重或仓库空间不足，成品留在工坊");

        foreach (var (item, amount) in Processing[job.Recipe].Outputs) AddResidentCargo(npc, item, amount);
        play.Jobs.Remove(job);
        Discover(job.Recipe);
        return new(true, "已领取成品");
    }

    private int ResidentLoadLimit(NpcState npc, string item, bool automatic)
    {
        int allowed = (int)Math.Floor((npc.CarryCapacityKg - npc.CarriedKg + 1e-8) / NpcConfig.WeightOf(item));
        return automatic ? Math.Min(allowed, Capacity - UsedStorage - npc.Cargo.Values.Sum()) : allowed;
    }

    private static void AddResidentCargo(NpcState npc, string item, int amount)
    {
        npc.CargoItems[item] = npc.CargoItems.GetValueOrDefault(item) + amount;
    }

    internal Result StoreResident(NpcState npc)
    {
        int free = Capacity - UsedStorage;
        if (free <= 0) return new(false, "仓库已满，物品随身保留");
        int deposited = 0;
        foreach (var (item, amount) in npc.CargoItems.ToArray())
        {
            int accepted = Math.Min(amount, free);
            Gain(item, accepted);
            if (accepted == amount) npc.CargoItems.Remove(item);
            else npc.CargoItems[item] -= accepted;
            deposited += accepted;
            free -= accepted;
            if (free == 0) break;
        }
        if (deposited == 0) return new(false, "身上没有物品");
        LastResidentDeposit = (npc.Cell, Clock);
        return new(true, $"已存放 {deposited} 份");
    }
}
