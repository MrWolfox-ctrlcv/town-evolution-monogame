using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Town.Core;
using Town.Game.Rendering;

namespace Town.Game;

public sealed class SceneSurfaces(GraphicsDevice device) : IDisposable
{
    private GpuSurface? roads,selection,grid,shore;
    private readonly GpuSurface water=Water(device);
    private static GpuSurface Water(GraphicsDevice device){var m=new SurfaceMesh();m.Quad(new(-600,World.WaterLevel,-600),new(-600,World.WaterLevel,600),new(600,World.WaterLevel,600),new(600,World.WaterLevel,-600),Vector3.Up,new Color(80,147,157));return new(device,m);}

    public void RebuildShore(World world)
    {
        var mesh = new SurfaceMesh();
        for (int z = -World.Half-2; z < World.Half+2; z++)
        for (int x = -World.Half-2; x < World.Half+2; x++)
        {
            if (world.Height(x,z) > 0) continue;
            int distance = 3;
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
                if (Math.Abs(dx)+Math.Abs(dz) < distance && world.Height(x+dx,z+dz) > 0)
                    distance = Math.Abs(dx)+Math.Abs(dz);
            if (distance == 3) continue;
            var color = distance == 1 ? new Color(127,183,162) : new Color(103,166,161);
            float y = World.WaterLevel + .004f;
            mesh.Quad(new(x,y,z),new(x,y,z+1),new(x+1,y,z+1),new(x+1,y,z),Vector3.Up,color);
        }
        shore?.Dispose(); shore = new(device,mesh);
    }
    public void RebuildRoads(World world)
    {
        var m=new SurfaceMesh();
        foreach(var o in world.Objects.Where(o=>o.Kind=="road"))
            m.Box(new(o.X,world.Height(o.X,o.Z)*World.Step+.008f,o.Z),new(1,.02f,1),
                Color.Lerp(new(174,147,105),new(193,169,125),(float)World.Hash(o.X,o.Z)));
        roads?.Dispose();roads=new(device,m);
    }
    public void RebuildGrid(World world)
    {
        var m=new SurfaceMesh();
        for(int z=-World.Half;z<World.Half;z++)for(int x=-World.Half;x<World.Half;x++){int h=world.Height(x,z);if(h==0)continue;float y=h*World.Step+.018f;var c=new Color(122,147,96);m.Quad(new(x,y,z),new(x,y,z+1),new(x+.018f,y,z+1),new(x+.018f,y,z),Vector3.Up,c);m.Quad(new(x,y,z),new(x,y,z+.018f),new(x+1,y,z+.018f),new(x+1,y,z),Vector3.Up,c);}
        grid?.Dispose();grid=new(device,m);
    }
    public void Select(World world,Cell? cell,string tool,int rotation,bool? validOverride=null,Cell[]? sources=null,int layerDelta=0)
    {
        var m=new SurfaceMesh();
        if(cell is Cell p){bool build=World.Catalog.ContainsKey(tool),valid=validOverride??(!build||world.CanPlace(tool,p.X,p.Z,rotation).Ok);var (w,d)=build?World.Footprint(tool,rotation):(1,1);var color=valid?new Color(213,228,147):new Color(218,122,100);
            for(int z=p.Z;z<p.Z+d;z++)for(int x=p.X;x<p.X+w;x++){float sourceY=Math.Max(.25f,world.Height(x,z)*World.Step)+.04f;float y=Math.Max(.25f,Math.Clamp(world.Height(x,z)+layerDelta,0,World.MaxHeight)*World.Step)+.04f;
                if(layerDelta!=0){float low=Math.Min(y,sourceY),high=Math.Max(y,sourceY);foreach(var corner in new[]{new Vector2(.02f,.02f),new(.96f,.02f),new(.02f,.96f),new(.96f,.96f)})m.Box(new(x+corner.X,low,z+corner.Y),new(.025f,Math.Max(.025f,high-low),.025f),color);}
                foreach(var v in new[]{new Vector4(0,0,1,.04f),new(0,.96f,1,.04f),new(0,0,.04f,1),new(.96f,0,.04f,1)})m.Box(new(x+v.X,y,z+v.Y),new(v.Z,.03f,v.W),color);}}
        foreach(var s in sources??[]){float y=world.Height(s.X,s.Z)*World.Step+.07f;var c=new Color(110,200,205);m.Box(new(s.X,y,s.Z),new(1,.03f,.04f),c);m.Box(new(s.X,y,s.Z+.96f),new(1,.03f,.04f),c);m.Box(new(s.X,y,s.Z),new(.04f,.03f,1),c);m.Box(new(s.X+.96f,y,s.Z),new(.04f,.03f,1),c);}
        selection?.Dispose();selection=new(device,m);
    }
    public void Draw(BasicEffect effect,bool showGrid)
    {
        effect.LightingEnabled=false;foreach(var pass in effect.CurrentTechnique.Passes){pass.Apply();water.Draw(device);shore?.Draw(device);}
        effect.LightingEnabled=true;foreach(var pass in effect.CurrentTechnique.Passes){pass.Apply();roads?.Draw(device);if(showGrid)grid?.Draw(device);selection?.Draw(device);}
    }
    public void Dispose(){water.Dispose();roads?.Dispose();grid?.Dispose();selection?.Dispose();shore?.Dispose();}
}
