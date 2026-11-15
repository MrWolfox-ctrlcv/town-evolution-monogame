using Town.Game;
// Keep window coordinates in physical pixels and prevent Windows bitmap enlargement.
Environment.SetEnvironmentVariable("SDL_WINDOWS_DPI_AWARENESS","permonitorv2");
Environment.SetEnvironmentVariable("SDL_WINDOWS_DPI_SCALING","0");
var reviewIndex=Array.IndexOf(args,"--review");
string? review=reviewIndex>=0&&reviewIndex+1<args.Length?Path.GetFullPath(args[reviewIndex+1]):null;
var save=review is not null?Path.Combine(review,"isolated-save"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TownEvolution.MonoGame");
using var game=new TownGame(new GameOptions(save,review,Array.IndexOf(args,"--exit-after-capture")>=0));
game.Run();
