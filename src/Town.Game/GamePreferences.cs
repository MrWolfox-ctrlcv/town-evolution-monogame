using System.Text.Json;
namespace Town.Game;
public sealed class GamePreferences
{
    public int Quality {get;set;}=2;
    public static readonly string[] QualityNames=["流畅","标准","高清","超清"];
    [System.Text.Json.Serialization.JsonIgnore] public float RenderScale=>Quality switch{0=>.7f,1=>.85f,3=>1.5f,_=>1};
    [System.Text.Json.Serialization.JsonIgnore] public int Samples=>OperatingSystem.IsAndroid()?0:Quality switch{0=>0,1=>0,2=>4,_=>4};
    public static GamePreferences Load(string directory)
    {
        try{var value=JsonSerializer.Deserialize<GamePreferences>(File.ReadAllText(Path.Combine(directory,"preferences.json")));return value is not null&&value.Quality is >=0 and <=3?value:new();}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public bool Save(string directory){try{Directory.CreateDirectory(directory);var path=Path.Combine(directory,"preferences.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));File.Move(path+".tmp",path,true);return true;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){return false;}}
}
