using Microsoft.Xna.Framework;
using Town.Core;
namespace Town.Game;
public sealed partial class TownGame
{
    private bool newIslandOpen;
    private World? pendingIsland;
    private void OpenIslandChooser(){CancelGameplayDrag();settingsOpen=false;pendingIsland=World.Create();newIslandOpen=true;}
    private void DrawIslandChooser(int w,int h)
    {
        int width=Math.Min(600,w-30),x=(w-width)/2,y=Math.Max(15,(h-340)/2);ui.Rect(new(0,0,w,h),new Color(30,50,40,90));ui.Block(new(0,0,w,h));ui.Panel(new(x,y,width,340));ui.Block(new(x,y,width,340));
        ui.Text("新岛屿",new(x+20,y+15),20);ui.Button(new(x+width-48,y+10,34,29),"×",()=>{newIslandOpen=false;pendingIsland=null;});
        DrawIslandMap(simulation.World,new(x+24,y+65,220,200));ui.Text("→",new(x+width/2-14,y+143),25);
        if(pendingIsland is { } preview)DrawIslandMap(preview,new(x+width-244,y+65,220,200));
        ui.Button(new(x+width-220,y+285,78,38),"↻",()=>pendingIsland=World.Create(),false,26);
        ui.Button(new(x+width-112,y+285,78,38),"✓",ApplyNewIsland,false,25);
    }
    private void DrawIslandMap(World world,Rectangle rect)
    {
        ui.Rect(rect,new Color(156,198,192));float unit=Math.Min(rect.Width/(World.Size*.6f),rect.Height/(World.Size*.34f));var center=rect.Center.ToVector2()+new Vector2(0,18);
        for(int z=-World.Half;z<World.Half;z++)for(int x=-World.Half;x<World.Half;x++)
        {
            int height=world.Height(x,z);if(height==0)continue;float sx=center.X+(x-z)*unit*.45f,sy=center.Y+(x+z)*unit*.21f-height*unit*.3f;int size=Math.Max(2,(int)unit);
            var color=world.Surface(x,z) switch{GroundSurface.Sand=>new Color(217,197,151),GroundSurface.Soil=>new Color(169,140,96),GroundSurface.Stone=>new Color(151,161,147),_=>new Color(148,180,109)};
            ui.Rect(new((int)sx,(int)sy,size,Math.Max(2,(int)(size*.7f))),color);
        }
        foreach(var tree in world.LiveTrees){float sx=center.X+(tree.X-tree.Z)*unit*.45f,sy=center.Y+(tree.X+tree.Z)*unit*.21f-world.Height(tree.X,tree.Z)*unit*.3f;ui.Rect(new((int)sx,(int)sy-2,3,4),new Color(74,126,69));}
    }
    private void ApplyNewIsland()
    {
        if(pendingIsland is null)return;
        if(!save.Archive(simulation)){Notice("保存失败");return;}
        if(npcRenderer is not null)SeedResidents(pendingIsland);
        simulation.Load(SaveCodec.Encode(pendingIsland));pendingIsland=null;newIslandOpen=false;ClearWorldSelection();bookView=0;Home();FrameResidentTown();Notice(save.Save(simulation));
    }
}
