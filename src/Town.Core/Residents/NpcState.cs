namespace Town.Core.Residents;

public enum NpcActivity { Idle, Walking, Working, Blocked }

public enum NpcTaskKind { None, GoTo }

/// <summary>玩家直接派的单步任务。第一阶段只有"走到某格"一种。</summary>
public sealed class NpcTask
{
    public NpcTaskKind Kind = NpcTaskKind.None;
    public int TargetX, TargetZ;
    public string Label = "";
    public static NpcTask WalkTo(int x, int z, string label) => new() { Kind = NpcTaskKind.GoTo, TargetX = x, TargetZ = z, Label = label };
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
    public double CarriedKg;
    public NpcActivity Activity = NpcActivity.Idle;
    public string Status = "空闲";
    public NpcTask? Task;

    /// <summary>当前路径，首个元素为下一个要踏上的格子；不写入存档。</summary>
    public List<Cell> Path = [];
    public int PathIndex;
    public bool PathFailed;

    public int CellX => (int)MathF.Floor(X);
    public int CellZ => (int)MathF.Floor(Z);
    public Cell Cell => new(CellX, CellZ);
    public double LoadRatio => Math.Clamp(CarriedKg / NpcConfig.MaxCarryKg, 0, 1);
    public float SpeedCellsPerSecond => NpcConfig.SpeedCellsPerSecond(CarriedKg);
    public bool HasRemainingPath => PathIndex < Path.Count;
    public Cell? NextCell => HasRemainingPath ? Path[PathIndex] : null;

    public void ClearTask()
    {
        Task = null;
        Path.Clear();
        PathIndex = 0;
        PathFailed = false;
        Activity = NpcActivity.Idle;
        Status = "空闲";
    }
}

/// <summary>存档形态：只保留可重建的部分，路径与临时状态重算。</summary>
public sealed record NpcSave(int Id, int Home, float X, float Z, float Facing, int Hunger, int Stamina, double CarriedKg,
    int TaskKind, int TargetX, int TargetZ, string Label);
