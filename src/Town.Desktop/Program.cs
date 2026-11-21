using Town.Game;
using Town.Core;
// Keep window coordinates in physical pixels and prevent Windows bitmap enlargement.
Environment.SetEnvironmentVariable("SDL_WINDOWS_DPI_AWARENESS","permonitorv2");
Environment.SetEnvironmentVariable("SDL_WINDOWS_DPI_SCALING","0");
var reviewIndex=Array.IndexOf(args,"--review");
string? review=reviewIndex>=0&&reviewIndex+1<args.Length?Path.GetFullPath(args[reviewIndex+1]):null;
bool npcDemo=args.Contains("--npc-demo");
bool npcTown=args.Contains("--npc-town");
bool autoExit=args.Contains("--exit-after-capture");
int islandIndex=Array.IndexOf(args,"--island-snapshot");
string? island=npcTown&&islandIndex>=0&&islandIndex+1<args.Length?Path.GetFullPath(args[islandIndex+1]):null;
int walkIndex=Array.IndexOf(args,"--npc-walk-check");
Cell? walk=autoExit&&review is not null&&walkIndex>=0&&walkIndex+2<args.Length
    &&int.TryParse(args[walkIndex+1],out int x)&&int.TryParse(args[walkIndex+2],out int z)?new(x,z):null;
bool haulCheck=npcTown&&autoExit&&review is not null&&args.Contains("--npc-haul-check");
bool cameraCheck=autoExit&&review is not null&&args.Contains("--camera-check");
bool? canopyCheck=autoExit&&review is not null&&args.Contains("--canopy-check")?args.Contains("--canopy-motion"):null;
int saveIndex=Array.IndexOf(args,"--save-dir");
var save=saveIndex>=0&&saveIndex+1<args.Length?Path.GetFullPath(args[saveIndex+1])
    :review is not null?Path.Combine(review,"isolated-save")
    :npcDemo||npcTown?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TownEvolution.MonoGame.NpcDemo")
    :Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TownEvolution.MonoGame");
bool controlsCheck=npcTown&&autoExit&&review is not null&&args.Contains("--controls-check");
using var game=new TownGame(new GameOptions(save,review,autoExit,npcDemo,npcTown,island,walk,haulCheck,cameraCheck,canopyCheck,controlsCheck));
game.Run();
