using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Town.Game;

public sealed class Overlay : IDisposable
{
    private readonly SpriteBatch batch;
    private readonly Texture2D pixel,atlas,disc;
    private readonly Dictionary<char,int> glyphs;
    private readonly List<(Rectangle Rect,Action Action,string Label,Func<Vector2,bool>? Shape,string? Control)> buttons=[];
    public string? ControlAt(Vector2 point)
    {
        var target=TargetAt(point);
        return target.Control??(target.Label==""?$"button:{target.Rect.X}:{target.Rect.Y}":target.Label);
    }
    public bool Occupies(Rectangle rect) => buttons.Any(b=>b.Rect.Intersects(rect));
    private (Rectangle Rect,Action Action,string Label,Func<Vector2,bool>? Shape,string? Control) TargetAt(Vector2 point)
    {
        point/=scale;
        return buttons.LastOrDefault(b=>b.Rect.Contains(point)&&(b.Shape?.Invoke(point)??true));
    }
    private readonly Dictionary<string,Texture2D> icons=[];
    private readonly List<(Rectangle Rect,string Tag)> drags=[];
    private readonly ContentManager content;
    private float scale=1;
    public readonly Color Ink=new(53,73,61),Muted=new(125,143,117),Paper=new(246,246,235),Green=new(81,109,74);
    public Overlay(GraphicsDevice device,ContentManager content)
    {
        this.content=content;batch=new(device);pixel=new(device,1,1);pixel.SetData(new[]{Color.White});atlas=content.Load<Texture2D>("ui-font");
        const int diameter=64;
        disc=new(device,diameter,diameter);
        var pixels=new Color[diameter*diameter];
        for(int y=0;y<diameter;y++)for(int x=0;x<diameter;x++)
        {
            var offset=new Vector2(x+.5f-diameter/2f,y+.5f-diameter/2f);
            pixels[y*diameter+x]=Color.White*Math.Clamp(diameter/2f-offset.Length(),0,1);
        }
        disc.SetData(pixels);
        glyphs=FontCharacters.Characters.Select((c,i)=>(c,i)).ToDictionary(p=>p.c,p=>p.i);
    }
    public IReadOnlyList<object> Targets=>buttons.Select(b=>(object)new{label=b.Label,x=(int)(b.Rect.Center.X*scale),y=(int)(b.Rect.Center.Y*scale),width=(int)(b.Rect.Width*scale),height=(int)(b.Rect.Height*scale),drag=drags.LastOrDefault(d=>d.Rect==b.Rect).Tag}).ToArray();
    public Vector2? ButtonCenter(string label)
    {
        var target=buttons.FirstOrDefault(b=>b.Label==label);
        return target.Action is null?null:target.Rect.Center.ToVector2()*scale;
    }
    public float Scale=>scale;
    public string? DragTag(Vector2 point){var top=TargetAt(point);return drags.LastOrDefault(d=>d.Rect==top.Rect).Tag;}
    public void DragButton(Rectangle rect,string icon,string label,string tag,Action tapped,bool selected=false){IconButton(rect,icon,label,tapped,selected);drags.Add((rect,tag));}
    public void ModelIcon(Texture2D texture,Rectangle rect)=>batch.Draw(texture,rect,Color.White);
    public bool Hit(Vector2 point,bool execute)
    {
        var target=TargetAt(point);if(target.Action is null)return false;
        if(execute)target.Action();return true;
    }
    public void Begin(float uiScale){scale=uiScale;buttons.Clear();drags.Clear();batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.LinearClamp,DepthStencilState.None,RasterizerState.CullNone,transformMatrix:Matrix.CreateScale(scale));}
    public void Rect(Rectangle rect,Color color)=>batch.Draw(pixel,rect,color);
    public void Panel(Rectangle rect){Rect(new(rect.X,rect.Y+4,rect.Width,rect.Height),new Color(50,70,55,18));Rect(rect,Paper);Rect(new(rect.X,rect.Y,rect.Width,1),Color.White);}
    public void Text(string text,Vector2 origin,int size=16,Color? color=null)
    {
        float glyphScale=size/44f,x=origin.X,y=origin.Y;
        foreach(char c in text)
        {
            if(c=='\n'){x=origin.X;y+=size*1.55f;continue;}
            if(!glyphs.TryGetValue(c,out int i))i=glyphs['?'];
            batch.Draw(atlas,new Vector2(x,y),new Rectangle(i%32*64,i/32*64,64,64),color??Ink,0,Vector2.Zero,glyphScale,SpriteEffects.None,0);
            x+=(c>127?23:c==' '?8:12)*size/22f;
        }
    }
    public void Button(Rectangle rect,string text,Action action,bool selected=false,int size=15)
    {
        Rect(rect,selected?Green:new Color(229,233,213));Text(text,new(rect.X+9,rect.Y+(rect.Height-size)/2-3),size,selected?Paper:Ink);
        buttons.Add((rect,action,text,null,null));
    }
    public void End()=>batch.End();
    public void Outline(string text,Vector2 position,int size=14)
    {
        foreach(var offset in new[]{new Vector2(-1,0),new(1,0),new(0,-1),new(0,1)})Text(text,position+offset,size,new Color(47,72,55));
        Text(text,position,size,new Color(255,247,220));
    }
    public void Block(Rectangle rect)=>buttons.Add((rect,()=>{},"UI区域",null,null));
    public void Icon(string name,Rectangle rect,bool flip=false)
    {
        if(!icons.TryGetValue(name,out var texture))icons[name]=texture=content.Load<Texture2D>("Ui/"+name);
        batch.Draw(texture,rect,null,Color.White,0,Vector2.Zero,flip?SpriteEffects.FlipHorizontally:SpriteEffects.None,0);
    }
    public void IconButton(Rectangle rect,string icon,string label,Action action,bool selected=false,bool flip=false)
    {
        if(selected){var c=new Color(255,225,132);Rect(new(rect.X+4,rect.Y+3,rect.Width-8,2),c);Rect(new(rect.X+4,rect.Y+3,2,13),c);Rect(new(rect.Right-6,rect.Y+3,2,13),c);}
        int size=Math.Min(rect.Width-6,rect.Height-6);Icon(icon,new(rect.Center.X-size/2,rect.Center.Y-size/2,size,size),flip);
        buttons.Add((rect,action,label,null,null));
    }
    public void ImageButton(Texture2D texture,Rectangle rect,Action action,string label,bool selected=false){if(selected)Rect(rect,Green);ModelIcon(texture,rect);buttons.Add((rect,action,label,null,null));}
    public void Region(Rectangle rect,Action action,string label)=>buttons.Add((rect,action,label,null,null));
    public void Badge(string text,Vector2 position,Color? color=null){Rect(new((int)position.X-3,(int)position.Y,Math.Max(23,text.Length*10+4),21),color??Paper);Text(text,position,14);}
    public void Progress(Rectangle rect,float progress,Color color){Rect(rect,new Color(212,219,198));Rect(new(rect.X,rect.Y,(int)(rect.Width*Math.Clamp(progress,0,1)),rect.Height),color);}
    public void ActionDisc(Rectangle rect,string? icon,string label,string control,Action action,string face="")
    {
        bool Inside(Vector2 p)=>Vector2.DistanceSquared(p,rect.Center.ToVector2())<rect.Width*rect.Width/4f;
        batch.Draw(disc,rect,new Color(60,76,54));
        batch.Draw(disc,Inset(rect,2),Paper);
        if(icon is not null)Icon(icon,Inset(rect,7));
        else Text(face,new(rect.Center.X-8,rect.Center.Y-15),23);
        buttons.Add((rect,action,label,Inside,control));
    }
    private static Rectangle Inset(Rectangle rect,int size)=>new(rect.X+size,rect.Y+size,rect.Width-size*2,rect.Height-size*2);
    public void Dispose(){disc.Dispose();pixel.Dispose();batch.Dispose();}
}
