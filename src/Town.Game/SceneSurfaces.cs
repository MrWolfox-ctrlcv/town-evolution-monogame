using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Game.Rendering;

namespace Town.Game;

public sealed class SceneSurfaces(GraphicsDevice device) : IDisposable
{
    private GpuSurface? roads,selection,grid;
    private readonly GpuSurface water=Water(device);
    private static GpuSurface Water(GraphicsDevice device){var m=new SurfaceMesh();m.Quad(new(-100,.22f,-100),new(-100,.22f,100),new(100,.22f,100),new(100,.22f,-100),Vector3.Up,new Color(159,196,189));return new(device,m);}
    public void RebuildRoads(World world)
    {
        var m=new SurfaceMesh();
        foreach(var o in world.Objects.Where(o=>o.Kind=="road"))m.Box(new(o.X,world.Height(o.X,o.Z)*World.Step+.008f,o.Z),new(1,.035f,1),new Color(212,190,149));
        for(int i=0;i<24;i++)m.Box(new(-7.5f+i*.25f,1.01f,3.85f),new(.23f,.13f,1.3f),new Color(186,148,104));
        foreach(float z in new[]{3.85f,5.15f}){m.Box(new(-7.5f,1.67f,z),new(6,.09f,.09f),new Color(148,113,76));for(int x=-7;x<=-2;x++)m.Box(new(x,.22f,z),new(.12f,1.65f,.12f),new Color(152,121,79));}
        roads?.Dispose();roads=new(device,m);
    }
    public void RebuildGrid(World world)
    {
        var m=new SurfaceMesh();
        for(int z=-20;z<20;z++)for(int x=-20;x<20;x++){int h=world.Height(x,z);if(h==0)continue;float y=h*World.Step+.018f;var c=new Color(122,147,96);m.Quad(new(x,y,z),new(x,y,z+1),new(x+.018f,y,z+1),new(x+.018f,y,z),Vector3.Up,c);m.Quad(new(x,y,z),new(x,y,z+.018f),new(x+1,y,z+.018f),new(x+1,y,z),Vector3.Up,c);}
        grid?.Dispose();grid=new(device,m);
    }
    public void Select(World world,Cell? cell,string tool,int rotation,bool? validOverride=null,Cell[]? sources=null)
    {
        var m=new SurfaceMesh();
        if(cell is Cell p){bool build=World.Catalog.ContainsKey(tool),valid=validOverride??(!build||world.CanPlace(tool,p.X,p.Z,rotation).Ok);var (w,d)=build?World.Footprint(tool,rotation):(1,1);var color=valid?new Color(213,228,147):new Color(218,122,100);
            for(int z=p.Z;z<p.Z+d;z++)for(int x=p.X;x<p.X+w;x++){float y=Math.Max(.25f,world.Height(x,z)*World.Step)+.04f;foreach(var v in new[]{new Vector4(0,0,1,.04f),new(0,.96f,1,.04f),new(0,0,.04f,1),new(.96f,0,.04f,1)})m.Box(new(x+v.X,y,z+v.Y),new(v.Z,.03f,v.W),color);}}
        foreach(var s in sources??[]){float y=world.Height(s.X,s.Z)*World.Step+.07f;var c=new Color(110,200,205);m.Box(new(s.X,y,s.Z),new(1,.03f,.04f),c);m.Box(new(s.X,y,s.Z+.96f),new(1,.03f,.04f),c);m.Box(new(s.X,y,s.Z),new(.04f,.03f,1),c);m.Box(new(s.X+.96f,y,s.Z),new(.04f,.03f,1),c);}
        selection?.Dispose();selection=new(device,m);
    }
    public void Draw(BasicEffect effect,bool showGrid)
    {
        effect.LightingEnabled=false;foreach(var pass in effect.CurrentTechnique.Passes){pass.Apply();water.Draw(device);}
        effect.LightingEnabled=true;foreach(var pass in effect.CurrentTechnique.Passes){pass.Apply();roads?.Draw(device);if(showGrid)grid?.Draw(device);selection?.Draw(device);}
    }
    public void Dispose(){water.Dispose();roads?.Dispose();grid?.Dispose();selection?.Dispose();}
}
