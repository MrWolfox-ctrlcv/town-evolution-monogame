using System.Text.Json;

namespace Town.Core;

public sealed record SaveData(int Schema, int[] Heights, PlacedObject[] Objects, int NextId,PlaySave? Gameplay=null,int Seed=0,int Generator=0,int[]? Surfaces=null);
public static class SaveCodec
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static string Encode(World world) => JsonSerializer.Serialize(new SaveData(World.Schema, world.Heights.ToArray(), world.Objects.ToArray(), world.NextId,world.SnapshotGameplay(),world.Seed,world.Generator,world.Surfaces.Select(v=>(int)v).ToArray()), Options);
    public static World Decode(string text)
    {
        if (text.Length > 600_000) throw new FormatException("存档过大");
        SaveData data;
        try { data = JsonSerializer.Deserialize<SaveData>(text, Options) ?? throw new FormatException("空存档"); }
        catch (JsonException error) { throw new FormatException("存档结构不合法", error); }
        if (data.Schema is not (1 or 2 or World.Schema) || (data.Schema>=2&&data.Gameplay is null) || data.Heights is null || data.Heights.Length != World.Size * World.Size || data.Heights.Any(h => h is < 0 or > 9) || data.Objects is null || data.Objects.Length > 400 || data.NextId is < 1 or > 1_000_000_000 || data.Generator is <0 or >1 || data.Schema==3&&(data.Surfaces is null||data.Surfaces.Length!=World.Size*World.Size||data.Surfaces.Any(v=>v is <0 or >3))) throw new FormatException("存档版本或数值不合法");
        return World.Restore(data);
    }
}
