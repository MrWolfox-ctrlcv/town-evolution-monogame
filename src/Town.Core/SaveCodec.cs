using System.Text.Json;
using Town.Core.Residents;

namespace Town.Core;

public sealed record SaveData(int Schema, int[] Heights, PlacedObject[] Objects, int NextId,PlaySave? Gameplay=null,int Seed=0,int[]? Surfaces=null,NaturalObject[]? Nature=null,long Grown=0,
    Residents.NpcSave[]? Residents=null,string? Island=null);
public static class SaveCodec
{
    /// <summary>Well above the ~90 KB a full 96x96 island produces, low enough to reject absurd input.</summary>
    public const int MaxCharacters = 8_000_000;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static string Encode(World world) => JsonSerializer.Serialize(new SaveData(World.Schema, world.Heights.ToArray(), world.Objects.ToArray(), world.NextId,world.SnapshotGameplay(),world.Seed,world.Surfaces.Select(v=>(int)v).ToArray(),world.Nature.ToArray(),world.Grown,
        world.Npcs.Snapshot(),world.IslandTag), Options);
    public static World Decode(string text)
    {
        if (text.Length > MaxCharacters) throw new FormatException("存档过大");
        SaveData data;
        try { data = JsonSerializer.Deserialize<SaveData>(text, Options) ?? throw new FormatException("空存档"); }
        catch (JsonException error) { throw new FormatException("存档结构不合法", error); }
        if (data.Residents?.Length > NpcDemoIsland.MaxResidents) throw new FormatException("居民数量超出上限");
        if (data.Schema != World.Schema || data.Gameplay is null || data.Heights is null || data.Heights.Length != World.Size * World.Size || data.Heights.Any(h => h is < 0 or > World.MaxHeight) || data.Objects is null || data.Objects.Length > 400 || data.NextId is < 1 or > 1_000_000_000 || data.Nature is null || data.Nature.Length > World.Size * World.Size / 3 || data.Grown < 0 || (data.Surfaces is null || data.Surfaces.Length != World.Size * World.Size || data.Surfaces.Any(v => v is < 0 or > 3))) throw new FormatException("存档版本或数值不合法");
        var seen=new HashSet<int>();
        if (data.Residents is not null)
        {
            var npcIds=new HashSet<int>();
            var workplaces=new HashSet<int>();
            foreach (var npc in data.Residents)
                if (npc is null || npc.Id is < 1 or int.MaxValue || !npcIds.Add(npc.Id) || !double.IsFinite(npc.X) || !double.IsFinite(npc.Z)
                    || !double.IsFinite(npc.Facing) || !double.IsFinite(npc.CarriedKg) || npc.CarriedKg < 0 || npc.CarriedKg > (npc.CartId == 0 ? NpcConfig.MaxCarryKg : NpcConfig.CartCapacityKg) + 1e-6
                    || npc.Hunger is < 0 or > Residents.NpcConfig.HungerMax || npc.Stamina is < 0 or > Residents.NpcConfig.StaminaMax
                    || !World.Inside((int)MathF.Floor((float)npc.X), (int)MathF.Floor((float)npc.Z))
                    || data.Heights[World.Index((int)MathF.Floor((float)npc.X), (int)MathF.Floor((float)npc.Z))] == 0
                    || !Enum.IsDefined((NpcTaskKind)npc.TaskKind) || npc.Label is null || npc.Label.Length > 100
                    || npc.CartId < 0 || npc.CartId >= data.NextId
                    || npc.Item is null || npc.Item.Length > 64
                    || (NpcTaskKind)npc.TaskKind is NpcTaskKind.Process or NpcTaskKind.FetchSupplies && !NpcTasks.CanStaff(npc.Item)
                    || npc.TaskKind == (int)NpcTaskKind.FetchSupplies && npc.DestinationId <= 0
                    || npc.Workplace is { } work && (work.BuildingId < 1 || work.Recipe is null || !NpcTasks.CanStaff(work.Recipe) || !workplaces.Add(work.BuildingId))
                    || npc.TaskKind != 0 && !World.Inside(npc.TargetX, npc.TargetZ)
                    || npc.Cargo is { } cargo && (cargo.Any(p => !World.ItemNames.ContainsKey(p.Key) || p.Value is < 1 or > 120)
                        || cargo.Sum(p => NpcConfig.WeightOf(p.Key) * p.Value) > (npc.CartId == 0 ? NpcConfig.MaxCarryKg : NpcConfig.CartCapacityKg) + 1e-6))
                    throw new FormatException("居民记录不合法");
        }
        if(data.Nature.Any(n=>n is null||!World.Inside(n.X,n.Z)||!double.IsFinite(n.Variant)||n.Variant is <0 or >1||!seen.Add(World.Index(n.X,n.Z))||data.Heights[World.Index(n.X,n.Z)]==0))throw new FormatException("天然植被记录不合法");
        return World.Restore(data);
    }
}
