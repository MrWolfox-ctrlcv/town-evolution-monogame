using Town.Core;

namespace Town.Game;

public sealed class LocalSave(string directory)
{
    private readonly string path=Path.Combine(directory,"foundation-v1.json");
    public bool Locked {get;private set;}
    public string? Load(Simulation simulation)
    {
        if(!File.Exists(path))return null;
        try{simulation.Load(File.ReadAllText(path));return "已恢复上次保存的小岛";}
        catch(Exception error)when(error is FormatException or IOException or UnauthorizedAccessException){Locked=true;return "原存档无法读取，已保留，保存已锁定";}
    }
    public bool Archive(Simulation simulation)
    {
        if(Locked)return false;
        try{var folder=Path.Combine(directory,"islands");Directory.CreateDirectory(folder);string json=SaveCodec.Encode(simulation.World);SaveCodec.Decode(json);File.WriteAllText(Path.Combine(folder,$"island-{simulation.World.Seed}-{DateTime.UtcNow:yyyyMMdd-HHmmss-ffff}.json"),json);return true;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or FormatException){return false;}
    }
    public string Save(Simulation simulation)
    {
        if(Locked)return "原存档已保留，请先备份处理再保存";
        try{Directory.CreateDirectory(directory);string json=SaveCodec.Encode(simulation.World);SaveCodec.Decode(json);var temporary=path+".tmp";File.WriteAllText(temporary,json);if(File.Exists(path))File.Copy(path,path+".bak",true);File.Move(temporary,path,true);return "小岛已保存到本机";}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){return "保存失败，请检查磁盘空间和目录权限";}
    }
}
