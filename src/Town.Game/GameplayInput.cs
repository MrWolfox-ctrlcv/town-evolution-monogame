using Microsoft.Xna.Framework;
using Town.Core;
using Town.Game.Interaction;
namespace Town.Game;

public sealed partial class TownGame
{
    private string? dragMode,dragKind;
    private int dragQuantity=1,dragStock,dragSource,focusedBuilding;
    private WorldHit? pressedHit;
    private PlacedObject? heldBuilding;
    private Cell heldOffset;
    private GroundPile? heldPile;
    private Vector2 dragPoint,previousBrush;
    private double pressTime;
    private bool brushActed;
    private Vector2? lastBrushSample;
    private int brushPlantQueued;
    private readonly HashSet<string> visitedCells=[];
    private readonly List<PlacedObject> entitySources=[];
    private readonly List<GroundPile> pileSources=[];
    private WorldCommand? proposed;
    private CommandPreview? commandPreview;
    private string previewKey="";
    private static readonly Dictionary<string,string> ToolNames=new(){["sickle"]="镰刀",["saw"]="锯子",["axe"]="斧子",["hammer"]="锤子",["diggingTool"]="挖掘工具",["wateringCan"]="水壶"};
    private void CancelGameplayDrag()
    {
        dragMode=dragKind=null;heldBuilding=null;heldPile=null;proposed=null;commandPreview=null;previewKey="";
        entitySources.Clear();pileSources.Clear();visitedCells.Clear();brushActed=false;lastBrushSample=null;brushPlantQueued=0;press=null;lastPointer=null;models?.SetPreview(null,simulation.World);if(surfaces is not null)RefreshSelection();
    }
    private void GamePointer(Vector2 point,bool down,bool up)
    {
        dragPoint=point;
        if(options.ReviewDirectory is not null&&(down||up)){inputTrace.Enqueue($"{frames} {(down?"down":"up")} {point.X:0},{point.Y:0} {dragMode} {dragKind} {proposed?.Type} {proposed?.X},{proposed?.Z}");while(inputTrace.Count>20)inputTrace.Dequeue();}
        if(down)
        {
            CancelGameplayDrag();press=point;lastPointer=point;previousBrush=point;pressTime=totalTime;dragged=false;
            string? tag=ui.DragTag(point);
            if(tag is not null)
            {
                var split=tag.Split(':');dragMode=split[0];dragKind=split[1];dragStock=simulation.World.Stock(dragKind);dragQuantity=Math.Clamp(quantities.GetValueOrDefault(dragKind,1),1,Math.Max(1,Math.Min(dragStock,World.StackLimit(dragKind))));
                
                if(dragMode=="stock"&&dragStock<=0){Notice("暂无"+World.ItemNames[dragKind]);CancelGameplayDrag();return;}
            }
            else if(IsBookHeader(point)){dragMode="book";}
            else if(!ui.Hit(point,false))pressedHit=WorldPicking.Pick(simulation.World,camera,GraphicsDevice.Viewport,point);
        }
        if(press is Vector2 start&&!up)
        {
            if(Vector2.Distance(start,point)>6)dragged=true;
            if(dragMode=="book")MoveBook(point-(lastPointer??point));
            else if(dragMode is not null)UpdateGameplayDrag(point);
            else if(!ui.Hit(start,false))
            {
                if(!dragged&&totalTime-pressTime>=.4&&pressedHit?.Type is "building" or "farm" or "pile" or "drop")BeginObjectDrag(pressedHit);
                if(dragMode is not null)UpdateGameplayDrag(point);
                else if(dragged&&lastPointer is Vector2 previous)camera.Pan(point-previous,GraphicsDevice.Viewport.Height);
            }
            lastPointer=point;
        }
        if(up&&press is Vector2 downPoint&&Vector2.Distance(downPoint,point)>6)dragged=true;
        if(up&&press is not null)
        {
            if(dragMode is "tool" or "stock"&&!dragged&&ui.Hit(point,false))
            {ui.Hit(point,true);CancelGameplayDrag();}
            else if(dragMode is not null&&dragMode!="book")
            {
                if(dragMode is "pile" or "drop"&&IsTray(point))Commit(new("pickup",0,0,SourceId:dragSource));
                else if(!ui.Hit(point,false)){UpdateGameplayDrag(point);if(proposed is not null&&!brushActed)Commit(proposed);}
                else if(dragMode=="tool"&&!dragged)Notice("将"+ToolNames.GetValueOrDefault(dragKind!,dragKind!)+"拖到目标后松手");
                CancelGameplayDrag();
            }
            else if(dragMode=="book")CancelGameplayDrag();
            else if(!dragged&&!ui.Hit(point,true))
            {
                TapWorld(point);
            }
            press=null;lastPointer=null;reviewCapturePending=options.ReviewDirectory is not null;
        }
        if(press is null&&!ui.Hit(point,false))SetCell(point);
    }
    private void BeginObjectDrag(WorldHit hit)
    {
        dragSource=hit.Id;rotation=0;
        if(hit.Type is "building" or "farm")
        {
            heldBuilding=simulation.World.Objects.FirstOrDefault(b=>b.Id==hit.Id);if(heldBuilding is null)return;
            var picked=camera.Pick(simulation.World,GraphicsDevice.Viewport,dragPoint,false)??new Cell(heldBuilding.X,heldBuilding.Z);heldOffset=new(picked.X-heldBuilding.X,picked.Z-heldBuilding.Z);
            rotation=heldBuilding.Rotation;dragMode="building";dragKind=heldBuilding.Kind;
        }
        else if(hit.Type=="pile"){heldPile=simulation.World.Piles.FirstOrDefault(p=>p.Id==hit.Id);if(heldPile is null)return;dragMode="pile";dragKind=heldPile.Item;dragQuantity=heldPile.Amount;}
        else if(hit.Type=="drop"){var drop=simulation.World.Drops.FirstOrDefault(p=>p.Id==hit.Id);if(drop is null)return;dragMode="drop";dragKind=drop.Item;}
    }
    private void TapWorld(Vector2 point)
    {
        var hit=WorldPicking.Pick(simulation.World,camera,GraphicsDevice.Viewport,point);focusedBuilding=0;
        if(hit is null){selected=null;return;}selected=new(hit.X,hit.Z);RefreshSelection();
        if(hit.Type is "drop" or "pile")Commit(new("pickup",hit.X,hit.Z,SourceId:hit.Id));
        else if(hit.Type is "building" or "farm")focusedBuilding=hit.Id;
        else if(hit.Type=="tree"){hintTool="axe";hintUntil=totalTime+2;}
        else if(hit.Type=="resource"){hintTool="diggingTool";hintUntil=totalTime+2;}
    }
    private void Commit(WorldCommand command){collectionOrigin=dragPoint;simulation.Enqueue(command);noticeUntil=totalTime+3;reviewCapturePending=options.ReviewDirectory is not null;}
    private bool IsTray(Vector2 point){var p=point/ui.Scale;int h=(int)(GraphicsDevice.Viewport.Height/ui.Scale),w=(int)(GraphicsDevice.Viewport.Width/ui.Scale);return(p.X<HudMetrics.TrayWidth||p.X>w-HudMetrics.TrayWidth)&&p.Y>HudMetrics.StartY&&p.Y<HudMetrics.StartY+HudMetrics.Slot(h)*6;}
    private bool ScrollGameplay(int wheel)
    {
        if(wheel==0)return false;
        if(dragMode=="stock"&&dragKind is not null){dragQuantity=Math.Clamp(dragQuantity+Math.Sign(wheel),1,Math.Max(1,Math.Min(dragStock,World.StackLimit(dragKind))));previewKey="";return true;}
        if(bookView!=0&&BookBounds.Contains(MousePoint()/ui.Scale)){TurnBook(Math.Sign(-wheel));return true;}return false;
    }
    private Vector2 MousePoint()=>Microsoft.Xna.Framework.Input.Mouse.GetState().Position.ToVector2();
    private void Brush(Vector2 point)
    {
        bool harvest=dragMode=="tool"&&dragKind=="sickle",plant=dragMode=="stock"&&dragKind is not null&&World.IsCrop(dragKind);
        if(!harvest&&!plant)return;
        int count=Math.Clamp((int)Math.Ceiling(Vector2.Distance(previousBrush,point)/8),1,64);
        for(int i=1;i<=count;i++)
        {
            var p=Vector2.Lerp(previousBrush,point,i/(float)count);if(harvest&&lastBrushSample is Vector2 last&&Vector2.DistanceSquared(last,p)<.00001f)continue;lastBrushSample=p;if(ui.Hit(p,false))continue;
            var hit=WorldPicking.Pick(simulation.World,camera,GraphicsDevice.Viewport,p);if(hit?.Type!="farm")continue;
            if(hit.FarmCell is <0 or >8)continue;
            var farm=simulation.World.Farm(hit.Id)!;var crop=farm.Cells[hit.FarmCell];
            if(harvest&&(crop is null||crop.ReadyTick>simulation.World.Clock))continue;
            if(plant&&(crop is not null||(dragKind=="rice")!=farm.Irrigated||dragStock-brushPlantQueued<=0||simulation.World.Stock(dragKind!)<=0))continue;
            string key=$"{hit.Id}:{hit.FarmCell}";if(!visitedCells.Add(key))continue;
            var c=harvest?new WorldCommand("tool",hit.X,hit.Z,"sickle",ObjectId:hit.Id,FarmCell:hit.FarmCell):new("plant",hit.X,hit.Z,ObjectId:hit.Id,Item:dragKind!,FarmCell:hit.FarmCell);
            Commit(c);brushActed=true;if(plant)brushPlantQueued++;
        }
        previousBrush=point;
    }
    private void UpdateGameplayDrag(Vector2 point)
    {
        if(dragMode is null or "book")return;
        if(dragMode=="stock"&&dragged)quantityItem=null;
        Brush(point);var world=simulation.World;var hit=ui.Hit(point,false)?null:WorldPicking.Pick(world,camera,GraphicsDevice.Viewport,point);
        proposed=null;if(hit is null){commandPreview=null;models.SetPreview(null,world);return;}
        var ground=camera.Pick(world,GraphicsDevice.Viewport,point,false)??new Cell(hit.X,hit.Z);
        selected=ground;
        if(dragMode=="stock")
        {
            if(!brushActed)proposed=new("stockDrop",hit.Type is "building" or "farm" or "pile"?hit.X:ground.X,hit.Type is "building" or "farm" or "pile"?hit.Z:ground.Z,Item:dragKind!,Quantity:dragQuantity,FarmCell:hit.FarmCell,ExpectedStock:dragStock);
        }
        else if(dragMode=="pile")
        {
            proposed=new("movePile",ground.X,ground.Z,ObjectId:hit.Type=="building"?hit.Id:0,SourceId:dragSource,Piles:heldPile is null?[]:[heldPile]);
        }
        else if(dragMode=="drop")proposed=new("moveDrop",ground.X,ground.Z,SourceId:dragSource);
        else if(dragMode=="building"&&heldBuilding is not null)
        {
            if(hit.Type is "building" or "farm"&&hit.Id!=heldBuilding.Id){var other=world.Objects.First(b=>b.Id==hit.Id);proposed=new("merge",ground.X,ground.Z,Entities:[heldBuilding,other]);}
            else proposed=new("move",ground.X-heldOffset.X,ground.Z-heldOffset.Z,Rotation:rotation,ObjectId:heldBuilding.Id,Entities:[heldBuilding]);
        }
        else if(dragMode=="tool")
        {
            if(dragKind=="hammer")
            {
                if(pileSources.Count==0&&hit.Type is "building" or "farm"&&entitySources.Count<2&&!entitySources.Any(b=>b.Id==hit.Id))entitySources.Add(world.Objects.First(b=>b.Id==hit.Id));
                if(entitySources.Count==0&&hit.Type=="pile"&&pileSources.Count<2){var p=world.Piles.First(p=>p.Id==hit.Id);if(p.Item is "wood" or "stone"&&!pileSources.Any(s=>s.Item==p.Item))pileSources.Add(p);}
            }
            if(dragKind=="hammer"&&((entitySources.Count>0&&hit.Type is not ("building" or "farm"))||(pileSources.Count>0&&hit.Type is "building" or "farm" or "tree" or "resource" or "drop")))
            {commandPreview=null;models.SetPreview(null,world);return;}
            if(dragKind=="wateringCan"&&hit.Type=="pile"&&pileSources.Sum(p=>p.Amount)<9){var p=world.Piles.First(p=>p.Id==hit.Id);if(p.Item=="soil"&&!pileSources.Any(s=>s.Id==p.Id))pileSources.Add(p);}
            proposed=new(dragKind=="diggingTool"&&terrainMode!="farm"?(terrainMode=="dig"?"terrainDig":"terrainFill"):"tool",hit.Type!="ground"?hit.X:ground.X,hit.Type!="ground"?hit.Z:ground.Z,dragKind!,rotation,
                ObjectId:hit.Type is "building" or "farm" or "resource"?hit.Id:0,FarmCell:hit.FarmCell,Entities:entitySources.Count>=2?entitySources.ToArray():null,Piles:pileSources.Count>=2?pileSources.ToArray():null);
        }
        if(proposed is null){models.SetPreview(null,world);return;}
        string key=$"{simulation.Revision}:{proposed.Type}:{proposed.Kind}:{proposed.X}:{proposed.Z}:{proposed.Rotation}:{proposed.ObjectId}:{proposed.FarmCell}:{dragQuantity}:{string.Join(',',pileSources.Select(s=>s.Id))}:{string.Join(',',entitySources.Select(s=>s.Id))}";
        if(key==previewKey)return;previewKey=key;commandPreview=world.PreviewPlan(proposed);models.SetPreview(commandPreview.Ghost,world);
        var ghost=commandPreview.Ghost;surfaces.Select(world,ghost is null?ground:new Cell(ghost.X,ghost.Z),ghost?.Kind??"inspect",ghost?.Rotation??0,commandPreview.Result.Ok,pileSources.Select(p=>new Cell(p.X,p.Z)).Concat(entitySources.Select(e=>new Cell(e.X,e.Z))).ToArray(),proposed.Type=="terrainDig"?-1:proposed.Type=="terrainFill"?1:0);
    }
}
