using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using System.Text.Json;
using Town.Core;
using Town.Game.Interaction;
using Town.Game.Rendering;
namespace Town.Game;

public sealed record GameOptions(string SaveDirectory,string? ReviewDirectory=null,bool AutoExit=false);
public sealed partial class TownGame : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly Simulation simulation=new();
    private readonly MapCamera camera=new();
    private readonly LocalSave save;
    private readonly GameOptions options;
    private readonly GamePreferences preferences;
    private TerrainRenderer terrain=null!;
    private InstancedModels models=null!;
    private SceneSurfaces surfaces=null!;
    private Overlay ui=null!;
    private BasicEffect matte=null!;
    private SpriteBatch presenter=null!;
    private RenderTarget2D? target,sceneTarget;
    private MouseState lastMouse;
    private int? activeTouch;
    private bool multiTouchGesture;
    private readonly Queue<string> inputTrace=[];
    private KeyboardState lastKeys;
    private Vector2? press,lastPointer;
    private bool dragged,showGrid,reviewed,settingsOpen,reviewCapturePending;
    private Cell? selected;
    private string message="拖入工具采集，叠放材料，再用锤子合成。";
    private int rotation,frames,idleUploads;
    private double noticeUntil,totalTime;
    private readonly System.Diagnostics.Stopwatch renderClock=System.Diagnostics.Stopwatch.StartNew();
    private double nextFrameMs;
    public TownGame(GameOptions options)
    {
        this.options=options;save=new(options.SaveDirectory);preferences=GamePreferences.Load(options.SaveDirectory);
        graphics=new(this){PreferredBackBufferWidth=1280,PreferredBackBufferHeight=800,GraphicsProfile=GraphicsProfile.HiDef,SynchronizeWithVerticalRetrace=true};
        Content.RootDirectory="Content";IsMouseVisible=true;IsFixedTimeStep=false;InactiveSleepTime=TimeSpan.FromMilliseconds(100);
#if DESKTOP
        Window.AllowUserResizing=true;Window.Title="Town Evolution | MonoGame 3.8.6 | .NET 10";
        var display=GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        graphics.PreferredBackBufferWidth=Math.Min(1920,(int)(display.Width*.7f));
        graphics.PreferredBackBufferHeight=Math.Min(1200,(int)(display.Height*.7f));
#endif
    }
    protected override void LoadContent()
    {
        matte=new(GraphicsDevice){VertexColorEnabled=true,LightingEnabled=true,AmbientLightColor=new(.66f,.69f,.62f),DiffuseColor=Vector3.One,SpecularColor=Vector3.Zero};
        matte.DirectionalLight0.Enabled=true;matte.DirectionalLight0.Direction=Vector3.Normalize(new(-.45f,-.82f,.35f));matte.DirectionalLight0.DiffuseColor=new(.38f,.35f,.29f);
        terrain=new(GraphicsDevice);models=new(GraphicsDevice,Content);surfaces=new(GraphicsDevice);ui=new(GraphicsDevice,Content);presenter=new(GraphicsDevice);
        var restored=save.Load(simulation);if(restored is not null)message=restored;
        thumbnails=new(GraphicsDevice,Content);TrackCollections(true);Rebuild();Home();camera.Update(GraphicsDevice.Viewport);lastMouse=Mouse.GetState();
    }
    private void Rebuild(){terrain.Rebuild(simulation.World);models.Sync(simulation.World);surfaces.RebuildRoads(simulation.World);surfaces.RebuildGrid(simulation.World);RefreshSelection();}
    private void RefreshSelection()=>surfaces.Select(simulation.World,selected,"inspect",rotation);
    private void Rotate(){rotation=(rotation+1)%4;RefreshSelection();}
    private void Home(){camera.Target=new(0,.8f,0);camera.Yaw=MathHelper.Pi/6;float fit=World.Half*1.05f;camera.Zoom=GraphicsDevice.Viewport.AspectRatio<1?fit/GraphicsDevice.Viewport.AspectRatio:fit;}
    private void Notice(string text){message=text;noticeUntil=totalTime+3;}
    protected override void Update(GameTime time)
    {
        totalTime=time.TotalGameTime.TotalSeconds;if(!IsActive){CancelGameplayDrag();base.Update(time);return;}
        var keys=Keyboard.GetState();var mouse=Mouse.GetState();bool Key(Keys key)=>keys.IsKeyDown(key)&&lastKeys.IsKeyUp(key);
        if(Key(Keys.Escape)){CancelGameplayDrag();bookView=0;focusedBuilding=0;}
        if(Key(Keys.Q))camera.Yaw-=MathHelper.PiOver4;if(Key(Keys.E))camera.Yaw+=MathHelper.PiOver4;
        if(Key(Keys.R))Rotate();if(Key(Keys.G))showGrid=!showGrid;
        if(Key(Keys.F12)&&options.ReviewDirectory is not null)Capture();
        if(Key(Keys.Z)&&(keys.IsKeyDown(Keys.LeftControl)||keys.IsKeyDown(Keys.RightControl)))Undo();
        if(Key(Keys.S)&&(keys.IsKeyDown(Keys.LeftControl)||keys.IsKeyDown(Keys.RightControl)))Notice(save.Save(simulation));
        if(Key(Keys.Tab))bookView=bookView==0?1:0;
        var touches=TouchPanel.GetState();var contacts=touches.Where(t=>t.State!=TouchLocationState.Released).ToArray();
        if(contacts.Length>=2){CancelGameplayDrag();multiTouchGesture=true;activeTouch=null;var a=contacts[0];var b=contacts[1];if(a.TryGetPreviousLocation(out var pa)&&b.TryGetPreviousLocation(out var pb)){float old=Vector2.Distance(pa.Position,pb.Position),now=Vector2.Distance(a.Position,b.Position);if(now>10)camera.Zoom=MathHelper.Clamp(camera.Zoom*old/now,7,52);}}
        else if(multiTouchGesture){if(contacts.Length==0)multiTouchGesture=false;}
        else if(contacts.Length==1){var t=contacts[0];bool down=t.State==TouchLocationState.Pressed||activeTouch!=t.Id;activeTouch=t.Id;Pointer(t.Position,down,false);}
        else if(activeTouch is int touchId){var released=touches.Where(t=>t.Id==touchId&&t.State==TouchLocationState.Released).ToArray();activeTouch=null;if(released.Length>0)Pointer(released[0].Position,false,true);else CancelGameplayDrag();}
        else if(!OperatingSystem.IsAndroid()){var p=mouse.Position.ToVector2();Pointer(p,mouse.LeftButton==ButtonState.Pressed&&lastMouse.LeftButton==ButtonState.Released,mouse.LeftButton==ButtonState.Released&&lastMouse.LeftButton==ButtonState.Pressed);int wheel=mouse.ScrollWheelValue-lastMouse.ScrollWheelValue;if(wheel!=0&&!ScrollGameplay(wheel))camera.Zoom=MathHelper.Clamp(camera.Zoom*MathF.Exp(-wheel*.001f),7,52);}
        camera.Update(GraphicsDevice.Viewport);simulation.Advance(time.ElapsedGameTime.TotalSeconds);
        while(simulation.TryChange(out var change)){if(change.Type=="reload"){Rebuild();TrackCollections(true);}else{if(change.Type is "raise" or "lower" or "terrainDig" or "terrainFill"){terrain.Rebuild(simulation.World,WorldQueries.AffectedChunks(change.Cells));surfaces.RebuildGrid(simulation.World);}models.Sync(simulation.World);if(change.Type is "place" or "remove" or "move")surfaces.RebuildRoads(simulation.World);RefreshSelection();}}
        TrackCollections();lastKeys=keys;lastMouse=mouse;base.Update(time);
    }
    private void Pointer(Vector2 point,bool down,bool up)=>GamePointer(point,down,up);
    private void SetCell(Vector2 point){var hit=WorldPicking.Pick(simulation.World,camera,GraphicsDevice.Viewport,point);Cell? next=hit is null?null:new Cell(hit.X,hit.Z);if(next==selected)return;selected=next;RefreshSelection();}
    private void Undo(){if(!simulation.Undo())Notice("还没有可撤销的操作");else Notice("已撤销上一步");}
    protected override void Draw(GameTime time)
    {
        if(!IsActive&&(options.ReviewDirectory is null||reviewed)){Thread.Sleep(80);return;}
        double wait=nextFrameMs-renderClock.Elapsed.TotalMilliseconds;if(wait>1)Thread.Sleep((int)wait);
        nextFrameMs=renderClock.Elapsed.TotalMilliseconds+1000.0/30;
        int w=GraphicsDevice.PresentationParameters.BackBufferWidth,h=GraphicsDevice.PresentationParameters.BackBufferHeight;
        if(target is null||target.Width!=w||target.Height!=h){target?.Dispose();target=new(GraphicsDevice,w,h,false,SurfaceFormat.Color,DepthFormat.None);}
        float renderScale=preferences.RenderScale;float limit=Math.Min(1,Math.Min(4096f/(w*renderScale),4096f/(h*renderScale)));int sw=Math.Max(1,(int)(w*renderScale*limit)),sh=Math.Max(1,(int)(h*renderScale*limit));
        if(sceneTarget is null||sceneTarget.Width!=sw||sceneTarget.Height!=sh||sceneTarget.MultiSampleCount!=preferences.Samples){sceneTarget?.Dispose();sceneTarget=new(GraphicsDevice,sw,sh,false,SurfaceFormat.Color,DepthFormat.Depth24,preferences.Samples,RenderTargetUsage.DiscardContents);}
        camera.Update(new Viewport(0,0,w,h));matte.View=camera.View;matte.Projection=camera.Projection;matte.World=Matrix.Identity;
        GraphicsDevice.SetRenderTarget(sceneTarget);GraphicsDevice.Clear(new Color(176,204,193));GraphicsDevice.BlendState=BlendState.Opaque;GraphicsDevice.DepthStencilState=DepthStencilState.Default;GraphicsDevice.RasterizerState=RasterizerState.CullNone;
        terrain.Draw(matte);surfaces.Draw(matte,showGrid);models.Draw(camera.View*camera.Projection);
        GraphicsDevice.SetRenderTarget(target);GraphicsDevice.SetVertexBuffers(Array.Empty<VertexBufferBinding>());GraphicsDevice.Indices=null;GraphicsDevice.Textures[0]=null;
        presenter.Begin(SpriteSortMode.Deferred,BlendState.Opaque,SamplerState.LinearClamp,DepthStencilState.None,RasterizerState.CullNone);presenter.Draw(sceneTarget,new Rectangle(0,0,w,h),Color.White);presenter.End();
        float uiScale=HudMetrics.Scale(h);DrawUi((int)(w/uiScale),(int)(h/uiScale),uiScale);frames++;
        GraphicsDevice.SetRenderTarget(null);GraphicsDevice.SetVertexBuffers(Array.Empty<VertexBufferBinding>());GraphicsDevice.Textures[0]=null;
        presenter.Begin(SpriteSortMode.Deferred,BlendState.Opaque,SamplerState.LinearClamp,DepthStencilState.None,RasterizerState.CullNone);presenter.Draw(target,new Rectangle(0,0,w,h),Color.White);presenter.End();
        if(frames==5)idleUploads=models.InstanceUploads;
        if(options.ReviewDirectory is not null&&frames>=35&&(!reviewed||reviewCapturePending&&simulation.PendingCount==0))Capture(!reviewed);base.Draw(time);
    }
    private void DrawUi(int w,int h,float scale)=>DrawFunctionalUi(w,h,scale);
    private void Capture(bool saveImage=true)
    {
        var capturedTarget=target??throw new InvalidOperationException("No rendered frame is available.");
        reviewed=true;reviewCapturePending=false;string dir=options.ReviewDirectory!;Directory.CreateDirectory(dir);
        if(saveImage){using var image=File.Create(Path.Combine(dir,"render.png"));capturedTarget.SaveAsPng(image,capturedTarget.Width,capturedTarget.Height);}
        var world=simulation.World;
        var tiles=Enumerable.Range(-World.Half,World.Size).SelectMany(z=>Enumerable.Range(-World.Half,World.Size).Select(x=>new Cell(x,z))).Select(c=>{var p=camera.Project(GraphicsDevice.Viewport,new(c.X+.5f,Math.Max(World.WaterLevel,world.Height(c.X,c.Z)*World.Step),c.Z+.5f));return new{x=c.X,z=c.Z,screenX=p.X,screenY=p.Y,height=world.Height(c.X,c.Z),empty=world.Occupant(c.X,c.Z) is null&&!world.HasNature(c.X,c.Z),canPlaceOven=world.CanPlace("oven",c.X,c.Z,rotation).Ok};});
        var entities=new List<object>();
        void Point(string type,int id,float x,float z,float above,int cell=-1){var p=camera.Project(GraphicsDevice.Viewport,new(x,world.Height((int)MathF.Floor(x),(int)MathF.Floor(z))*World.Step+above,z));p=new(MathF.Round(p.X),MathF.Round(p.Y));var hit=WorldPicking.Pick(world,camera,GraphicsDevice.Viewport,new(p.X,p.Y));entities.Add(new{type,id,cell,screenX=p.X,screenY=p.Y,hitType=hit?.Type,hitId=hit?.Id,hitCell=hit?.FarmCell});}
        foreach(var b in world.Objects.Where(b=>b.Kind!="road")){var(w,d)=World.Footprint(b.Kind,b.Rotation);Point("building",b.Id,b.X+w*.5f,b.Z+d*.5f,b.Kind=="farm"?.05f:1);if(b.Kind=="farm")for(int i=0;i<9;i++){var p=WorldPicking.FarmCenter(b,i);Point("farm",b.Id,p.X,p.Z,world.Farm(b.Id)?.Cells[i]is null?.04f:.6f,i);}}
        foreach(var t in world.LiveTrees)Point("tree",World.Index(t.X,t.Z),t.X+.5f,t.Z+.5f,1.3f);
        foreach(var p in world.Piles)Point("pile",p.Id,p.X+.5f,p.Z+.5f,.25f);
        foreach(var p in world.Drops)Point("drop",p.Id,p.X+p.OffsetX,p.Z+p.OffsetZ,.15f);
        var data=new{inputTrace=inputTrace.ToArray(),entities,platform=OperatingSystem.IsAndroid()?"Android":"Windows",seed=world.Seed,framework="MonoGame 3.8.6",graphicsProfile=GraphicsDevice.GraphicsProfile.ToString(),adapter=GraphicsDevice.Adapter.Description,frames,simulationTicks=simulation.Ticks,revision=simulation.Revision,pendingCommands=simulation.PendingCount,objects=world.Objects.Count,terrainChunks=terrain.Count,terrainTriangles=terrain.Triangles,terrain.Rebuilds,models=models.Counts,models.GeometryLoads,models.InstanceUploads,idleUploads,models.InstanceCount,instancedDrawCalls=models.DrawCalls,gesture=new{mode=dragMode,kind=dragKind,quantity=dragQuantity,previewOk=commandPreview?.Result.Ok,previewReason=commandPreview?.Result.Message},bookView,bookPage,focusedBuilding,rotation,selected,result=simulation.LastResult.Message,ok=simulation.LastResult.Ok,notice=message,saveLocked=save.Locked,width=capturedTarget.Width,height=capturedTarget.Height,quality=GamePreferences.QualityNames[preferences.Quality],sceneWidth=sceneTarget?.Width,sceneHeight=sceneTarget?.Height,antialiasSamples=sceneTarget?.MultiSampleCount,uiScale=HudMetrics.Scale(capturedTarget.Height),cameraYaw=camera.Yaw,ui=ui.Targets,tiles};
        File.WriteAllText(Path.Combine(dir,"world.json"),SaveCodec.Encode(world));
        File.WriteAllText(Path.Combine(dir,"render.json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));if(options.AutoExit)Exit();
    }
    protected override void UnloadContent(){target?.Dispose();sceneTarget?.Dispose();terrain?.Dispose();models?.Dispose();surfaces?.Dispose();matte?.Dispose();ui?.Dispose();thumbnails?.Dispose();presenter?.Dispose();base.UnloadContent();}
}