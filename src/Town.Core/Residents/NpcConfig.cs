namespace Town.Core.Residents;

/// <summary>第一阶段居民数值。全部集中在此，便于后续调整；不含渲染与存储引用。</summary>
public static class NpcConfig
{
    /// <summary>固定 20 Hz 模拟步，与 Simulation.StepSeconds 一致。</summary>
    public const int TicksPerSecond = 20;

    // —— 移动 ——
    /// <summary>空载平地行走速度（格/秒）。</summary>
    public const float WalkCellsPerSecond = 2f;
    /// <summary>满载时的速度衰减比例：实地速度 = 空载 × (1 − 0.3 × 负重/最大负重)。</summary>
    public const float LoadSlowdown = .3f;
    /// <summary>相邻格高度差超过此值时不可步行，必须走台阶级差。</summary>
    public const int MaxStepUp = 1;
    /// <summary>渲染高度跟随地形的平滑系数（每 tick）。</summary>
    public const float HeightLerp = .35f;
    /// <summary>转向角速度（弧度/tick）。</summary>
    public const float TurnRadiansPerTick = .35f;

    // —— 负重 ——
    public const double BodyWeightKg = 60;
    public const double MaxCarryKg = 12;
    public const double CartCapacityKg = 32;
    public const float CartRoadSpeed = 1.8f;
    public const float CartGroundSpeed = 1.4f;

    // —— 工作 ——
    public const float WorkSpeed = 1f;
    public const int StaminaThresholdLow = 30;

    // —— 需求 ——
    public const int HungerMax = 100;
    public const int StaminaMax = 100;
    public const int HungerDrainPerMinute = 20;
    public const int HungerSeekThreshold = 40;
    public const int StaminaRestTarget = 85;
    public const int StaminaRestPerSecond = 6;
    public const int WalkDrainInterval = TicksPerSecond * 4;
    public const int WorkDrainPerSecond = 2;
    internal static readonly (string Item, int Filling)[] Food =
        [("bread", 45), ("roastedCarrot", 35), ("vegetable", 25), ("lettuce", 20)];

    // —— 寻路代价（A*，四方向） ——
    public const float CostFlat = 10;
    public const float CostStepUp = 15;
    public const float CostStepDown = 12;
    /// <summary>单次寻路最多展开的节点数，防止无路时卡住一帧。</summary>
    public const int MaxPathNodes = 8192;

    private static readonly Dictionary<string,double> weights = new(StringComparer.Ordinal)
    {
        ["vegetable"] = .5, ["wheat"] = .5, ["lettuce"] = .5, ["rice"] = .5,
        ["bread"] = .5, ["roastedCarrot"] = .5, ["flour"] = .5, ["milledRice"] = .5,
        ["wood"] = 3, ["soil"] = 4, ["stone"] = 5, ["mud"] = 4,
        ["treeSeed"] = .1, ["sapling"] = .8, ["fertilizer"] = 2,
    };

    /// <summary>单份物品重量（kg）。未登记的物品按 1kg 计，避免除零或静默掉重。</summary>
    public static double WeightOf(string item) => weights.TryGetValue(item, out var w) ? w : 1;

    /// <summary>实际行走速度（格/秒）：空载速度和负重衰减，下限保留 30%。</summary>
    public static float SpeedCellsPerSecond(double carriedKg)
    {
        double ratio = Math.Clamp(carriedKg / MaxCarryKg, 0, 1);
        return (float)(WalkCellsPerSecond * (1 - LoadSlowdown * ratio));
    }
}
