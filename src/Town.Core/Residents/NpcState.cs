namespace Town.Core.Residents;

public enum NpcActivity { Idle, Walking, Working, Blocked, Resting }

public enum NpcTaskKind { None, GoTo, PickupDrop, PickupPile, Store, Chop, Harvest, PickupFarmBox, Eat, Rest, Dig, Process, PickupJob, FetchSupplies, EquipCart, ParkCart }

public sealed record NpcWorkplace(int BuildingId, string Recipe);

public sealed record NpcTask(NpcTaskKind Kind, int TargetX, int TargetZ, string Label,
    int SourceId = 0, int FarmCell = -1, bool Automatic = false, string Item = "", int DestinationId = 0)
{
    public static NpcTask WalkTo(int x, int z, string label) => new(NpcTaskKind.GoTo, x, z, label);
}

/// <summary>居民运行时状态。位置为连续格坐标，所在格由 floor 推算。</summary>
public sealed class NpcState
{
    public int Id;
    public int Home;
    public float X, Z;
    public float Facing;
    /// <summary>渲染用高度，随阶梯平滑过渡。</summary>
    public float Y;
    public int Hunger = NpcConfig.HungerMax;
    public int Stamina = NpcConfig.StaminaMax;
    public double CarriedKg => CargoItems.Sum(p => NpcConfig.WeightOf(p.Key) * p.Value);
    internal Dictionary<string, int> CargoItems { get; } = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> Cargo => CargoItems;
    public bool AutoHaul = true;
    public NpcActivity Activity = NpcActivity.Idle;
    public string Status = "空闲";
    public NpcTask? Task;
    public NpcWorkplace? Workplace;
    public int CartId { get; internal set; }
    public bool HasCart => CartId != 0;
    public double CarryCapacityKg => HasCart ? NpcConfig.CartCapacityKg : NpcConfig.MaxCarryKg;

    /// <summary>当前路径，首个元素为下一个要踏上的格子；不写入存档。</summary>
    public List<Cell> Path = [];
    public int PathIndex;
    public bool PathFailed;
    public Cell? Goal;
    internal long WorkReadyTick;
    internal long NextHaulTick;

    public int CellX => (int)MathF.Floor(X);
    public int CellZ => (int)MathF.Floor(Z);
    public Cell Cell => new(CellX, CellZ);
    public double LoadRatio => Math.Clamp(CarriedKg / CarryCapacityKg, 0, 1);
    public float SpeedCellsPerSecond => NpcConfig.SpeedCellsPerSecond(CarriedKg);
    public bool HasRemainingPath => PathIndex < Path.Count;
    public Cell? NextCell => HasRemainingPath ? Path[PathIndex] : null;

    public void ClearTask()
    {
        Task = null;
        Path.Clear();
        PathIndex = 0;
        PathFailed = false;
        Goal = null;
        WorkReadyTick = 0;
        Activity = NpcActivity.Idle;
        Status = "空闲";
    }
}

/// <summary>存档形态：只保留可重建的部分，路径与临时状态重算。</summary>
public sealed record NpcSave(int Id, int Home, float X, float Z, float Facing, int Hunger, int Stamina, double CarriedKg,
    int TaskKind, int TargetX, int TargetZ, string Label, int SourceId = 0, int FarmCell = -1,
    bool Automatic = false, bool AutoHaul = true, Dictionary<string, int>? Cargo = null, string Item = "", int DestinationId = 0, NpcWorkplace? Workplace = null, int CartId = 0);
