using Microsoft.Xna.Framework;
using Town.Core;
using Town.Game.Interaction;
using Town.Game.Rendering;
namespace Town.Game;

public sealed partial class TownGame
{
    private ThumbnailCache thumbnails=null!;
    private int productPage,bookView,bookPage,bookRecipe;
    private bool contextOpen=true,terrainMenu;
    private string terrainMode="farm";
    private string? quantityItem;
    private readonly Dictionary<string,int> quantities=[];
    private Vector2? bookPosition;
    private Rectangle BookBounds;
    private readonly Dictionary<string,Vector2> productCenters=[];
    private Dictionary<string,int> lastStock=[];
    private Vector2 collectionOrigin;
    private sealed record CollectionFlight(string Item,int Amount,Vector2 From,double Start);
    private readonly List<CollectionFlight> flights=[];
    private static string IconName(string kind)=>kind switch{"vegetable"=>"carrot","wateringCan"=>"watering-can","diggingTool"=>"digging-tools","treeSeed"=>"tree-seed","milledRice"=>"milled-rice","roastedCarrot"=>"roasted-carrot",_=>kind};
    private string? hintTool;
    private double hintUntil;
    private string TerrainToolIcon=>terrainMode switch{"dig"=>"digging-pickaxe","fill"=>"soil",_=>"digging-tool"};
    private void DrawFunctionalUi(int w,int h,float scale)
    {
        var world=simulation.World;ui.Begin(scale);productCenters.Clear();
        ui.Outline($"{world.UsedStorage} / {world.Capacity}",new(w/2-25,19),15);
        ui.IconButton(new(w-95,4,84,70),"tools","设置",()=>settingsOpen=!settingsOpen);
        int slot=HudMetrics.Slot(h),i=0;
        foreach(var(kind,name)in ToolNames)
        {
            string held=kind;var rect=new Rectangle(10,HudMetrics.StartY+i++*slot,84,slot-3);
            ui.DragButton(rect,kind=="diggingTool"?TerrainToolIcon:IconName(kind),name,"tool:"+kind,()=>{hintTool=held;hintUntil=totalTime+2;if(held=="diggingTool")terrainMenu=!terrainMenu;},dragMode=="tool"&&dragKind==kind||hintTool==kind&&totalTime<hintUntil&&Math.Sin(totalTime*8)>0);
            if(kind=="diggingTool")ui.Badge(terrainMode=="dig"?"↓":terrainMode=="fill"?"↑":"+",new(rect.Right-22,rect.Bottom-20));
        }
        var items=World.ItemNames.Keys.ToArray();int pages=(int)Math.Ceiling(items.Length/6f);productPage=Math.Clamp(productPage,0,pages-1);
        for(i=0;i<6&&productPage*6+i<items.Length;i++)
        {
            string item=items[productPage*6+i];var rect=new Rectangle(w-94,HudMetrics.StartY+i*slot,84,slot-3);productCenters[item]=rect.Center.ToVector2()*scale;
            ui.DragButton(rect,IconName(item),World.ItemNames[item],"stock:"+item,()=>quantityItem=quantityItem==item?null:item);
            ui.Badge(world.Stock(item).ToString(),new(rect.Right-29,rect.Bottom-22));
        }
        ui.Button(new(w-90,HudMetrics.StartY+slot*6+4,76,30),$"› {productPage+1}/{pages}",()=>productPage=(productPage+1)%pages,false,14);
        ui.IconButton(new(w/2-137,h-88,78,76),"camera-rotate","左转",()=>camera.Yaw-=MathHelper.PiOver4,false,true);
        ui.IconButton(new(w/2-42,h-94,84,84),"manage","管理",()=>contextOpen=!contextOpen,contextOpen);
        ui.IconButton(new(w/2+59,h-88,78,76),"camera-rotate","右转",()=>camera.Yaw+=MathHelper.PiOver4);
        ui.IconButton(new(w-98,h-98,86,86),"handbook","图鉴",()=>bookView=bookView==0?1:0);
        DrawWorldCaptions(scale);
        DrawResidentPanel(w,h);
        if(terrainMenu)
        {
            int x=108,y=90;ui.Panel(new(x,y,306,136));ui.Block(new(x,y,306,136));
            string[] modes=["farm","dig","fill"],icons=["digging-tool","digging-pickaxe","soil"],labels=["田地","挖掘","填土"];
            for(i=0;i<3;i++){string mode=modes[i];int px=x+8+i*99;ui.IconButton(new(px,y+8,90,88),icons[i],labels[i],()=>{terrainMode=mode;terrainMenu=false;},terrainMode==mode);ui.Text(labels[i],new(px+27,y+105),14);}
        }
        if(quantityItem is { } qi)
        {
            int x=w-326,y=100;ui.Panel(new(x,y,216,126));ui.Block(new(x,y,216,126));ui.Icon(IconName(qi),new(x+80,y+6,56,56));
            int limit=Math.Max(1,Math.Min(world.Stock(qi),World.StackLimit(qi))),amount=Math.Clamp(quantities.GetValueOrDefault(qi,1),1,limit);quantities[qi]=amount;
            ui.Button(new(x+12,y+76,51,38),"−",()=>quantities[qi]=Math.Max(1,amount-1),false,22);ui.Text(amount.ToString(),new(x+102,y+83),20);ui.Button(new(x+153,y+76,51,38),"+",()=>quantities[qi]=Math.Min(limit,amount+1),false,22);
        }
        if(settingsOpen)DrawSettings(w);
        if(contextOpen&&world.Objects.FirstOrDefault(b=>b.Id==focusedBuilding)is { } building)DrawBuildingContext(w,h,building);
        if(bookView!=0)DrawBook(w,h);
        if(newIslandOpen)DrawIslandChooser(w,h);
        if(dragMode is not null&&dragMode!="book")DrawHeldTool(scale);
        if(totalTime<noticeUntil&&(!simulation.LastResult.Ok||save.Locked))ui.Outline("×",dragPoint/scale+new Vector2(25,15),27);
        if(totalTime<noticeUntil&&message.Contains("保存")){ui.Icon("save",new(w/2-35,58,30,30));ui.Outline(message.Contains("已保存")?"✓":"×",new(w/2+3,62),22);}
        foreach(var f in flights)
        {
            float t=(float)((totalTime-f.Start)/.65);if(t is <0 or >1)continue;
            var to=productCenters.GetValueOrDefault(f.Item,new Vector2((w-45)*scale,h*.5f*scale));var p=Vector2.Lerp(f.From,to,t)/scale-new Vector2(0,MathF.Sin(t*MathHelper.Pi)*48);
            ui.Icon(IconName(f.Item),new((int)p.X-20,(int)p.Y-20,40,40));ui.Outline("+"+f.Amount,p+new Vector2(17,-15),15);
        }
        flights.RemoveAll(f=>totalTime-f.Start>.7);ui.End();
    }
    private void DrawSettings(int w)
    {
        int x=Math.Max(106,w-454),y=78;ui.Panel(new(x,y,344,288));ui.Block(new(x,y,344,288));ui.Text("设置",new(x+16,y+12),20);ui.Button(new(x+295,y+8,34,29),"×",()=>settingsOpen=false);
        ui.IconButton(new(x+16,y+49,66,62),"save","保存",()=>Notice(save.Save(simulation)));
        ui.IconButton(new(x+97,y+49,66,62),"return","撤销",Undo);
        ui.IconButton(new(x+178,y+49,66,62),"town","全景",Home);
        ui.Button(new(x+259,y+64,64,36),"网格",()=>showGrid=!showGrid,showGrid,14);
        ui.Text("清晰度",new(x+17,y+133),16);
        for(int i=0;i<4;i++){int choice=i;ui.Button(new(x+16+i*79,y+165,74,38),GamePreferences.QualityNames[i],()=>SetQuality(choice),preferences.Quality==i,15);}
        ui.Button(new(x+16,y+225,66,36),"+",()=>camera.Zoom=Math.Max(7,camera.Zoom*.85f),false,23);ui.Button(new(x+95,y+225,66,36),"−",()=>camera.Zoom=Math.Min(52,camera.Zoom*1.18f),false,23);
        ui.IconButton(new(x+185,y+216,55,54),"town","新岛屿",OpenIslandChooser);ui.Button(new(x+242,y+228,86,36),"新岛屿",OpenIslandChooser,false,14);

    }
    private void SetQuality(int choice){preferences.Quality=choice;if(!preferences.Save(options.SaveDirectory))Notice("保存失败");reviewCapturePending=options.ReviewDirectory is not null;}
    private void DrawBuildingContext(int w,int h,PlacedObject b)
    {
        int x=Math.Max(108,w/2-185),y=h-367;var world=simulation.World;ui.Panel(new(x,y,370,260));ui.Block(new(x,y,370,260));ui.Text(World.Catalog[b.Kind].Name,new(x+14,y+12),18);
        ui.Button(new(x+322,y+8,34,29),"×",()=>focusedBuilding=0);
        ui.IconButton(new(x+13,y+45,52,50),"rotate","旋转",()=>Commit(new("move",b.X,b.Z,Rotation:(b.Rotation+1)%4,ObjectId:b.Id,Entities:[b])));
        ui.Button(new(x+77,y+55,52,36),"×",()=>Commit(new("remove",b.X,b.Z)),false,24);
        var job=world.Jobs.FirstOrDefault(j=>j.Id==b.Id);
        if(job is not null)
        {
            var rule=World.Processing[job.Recipe];bool ready=job.ReadyTick<=world.Clock;DrawPart(new(rule.Outputs[0].Item,"",rule.Outputs[0].Amount),new(x+157,y+48),52);
            ui.Progress(new(x+142,y+113,178,6),1-(job.ReadyTick-world.Clock)/(float)rule.Ticks,ui.Green);
            if(ready)ui.IconButton(new(x+260,y+47,65,62),"manage","领取",()=>Commit(new("collectJob",b.X,b.Z,ObjectId:b.Id)));
            else ui.Text("◷ "+Math.Max(0,(job.ReadyTick-world.Clock+19)/20),new(x+258,y+69),18);
        }
        else
        {
            var rules=World.Processing.Where(p=>p.Value.Building==b.Kind&&p.Key is not ("grow-sapling" or "make-fertilizer")).ToArray();
            for(int i=0;i<rules.Length;i++)
            {
                var rule=rules[i];int ry=y+108+i*46;bool attached=rule.Value.Inputs.All(p=>(world.Attachments.FirstOrDefault(a=>a.Id==b.Id)?.Stock.GetValueOrDefault(p.Item)??0)>=p.Amount);int n=0;
                foreach(var input in rule.Value.Inputs){DrawPart(new(input.Item,"",input.Amount),new(x+20+n++*63,ry),42);}
                ui.Text("→",new(x+165,ry+8),23);DrawPart(new(rule.Value.Outputs[0].Item,"",rule.Value.Outputs[0].Amount),new(x+211,ry),42);
                ui.IconButton(new(x+283,ry-1,48,45),"tools","加工:"+rule.Key,()=>Commit(new("process",b.X,b.Z,Kind:attached?"attached":"",ObjectId:b.Id,Item:rule.Key)));
            }
            if(b.Kind is "nursery" or "compostBarrel")
            {
                DrawPart(new("treeSeed","",b.Kind=="nursery"?1:2),new(x+160,y+55),44);ui.Text("+",new(x+213,y+66),20);ui.Icon(b.Kind=="nursery"?"watering-can":"carrot",new(x+245,y+53,46,46));
            }
            if(world.Farm(b.Id)is { } farm)
            {
                for(int i=0;i<9;i++)ui.Rect(new(x+158+i%3*15,y+51+i/3*15,12,12),farm.Cells[i]is null?new Color(174,145,102):farm.Cells[i]!.ReadyTick<=world.Clock?new Color(131,174,88):new Color(230,196,108));
                ui.Icon("sickle",new(x+230,y+53,44,44));if(farm.Box){ui.IconButton(new(x+292,y+48,55,55),"manage","领取田箱",()=>Commit(new("collectBox",b.X,b.Z,ObjectId:b.Id)));ui.Badge((farm.BoxStock?.Values.Sum()??0)+"/16",new(x+287,y+106));}
            }
        }
        var attachment=world.Attachments.FirstOrDefault(a=>a.Id==b.Id);int count=0;
        if(attachment is not null)foreach(var(item,amount)in attachment.Stock.Where(p=>p.Value>0).Take(5))
        {string key=item;var r=new Rectangle(x+15+count++*65,y+210,50,46);ui.IconButton(r,IconName(item),"取回:"+item,()=>Commit(new("detach",b.X,b.Z,ObjectId:b.Id,Item:key)));ui.Badge(amount.ToString(),new(r.Right-12,r.Bottom-15));}
    }
    private void DrawWorldCaptions(float scale)
    {
        var world=simulation.World;
        Vector2? At(Vector3 position){var p=camera.Project(GraphicsDevice.Viewport,position)/scale;return p.X<100||p.X>GraphicsDevice.Viewport.Width/scale-100||p.Y<70||p.Y>GraphicsDevice.Viewport.Height/scale-100?null:p;}
        foreach(var pile in world.Piles)if(At(new(pile.X+.5f,world.Height(pile.X,pile.Z)*World.Step+.65f,pile.Z+.5f))is Vector2 p)ui.Badge(pile.Amount.ToString(),p-new Vector2(7,10));
        foreach(var b in world.Objects.Where(b=>b.Kind=="farm")){var f=world.Farm(b.Id);if(f is null)continue;int ready=f.Cells.Count(c=>c is not null&&c.ReadyTick<=world.Clock);if(ready>0&&At(new(b.X+1,world.Height(b.X,b.Z)*World.Step+1.1f,b.Z+1))is Vector2 p){ui.Icon("manage",new((int)p.X-14,(int)p.Y-17,28,28));ui.Badge(ready.ToString(),p+new Vector2(12,0));}}
        foreach(var job in world.Jobs.Where(j=>j.ReadyTick<=world.Clock)){var b=world.Objects.First(o=>o.Id==job.Id);if(At(new(b.X+.5f,world.Height(b.X,b.Z)*World.Step+2,b.Z+.5f))is Vector2 p)ui.Icon("manage",new((int)p.X-17,(int)p.Y-17,34,34));}
    }
    private void DrawHeldTool(float scale)
    {
        var p=dragPoint/scale;string icon=dragKind=="diggingTool"?TerrainToolIcon:IconName(dragKind??"hammer");
        if(dragMode=="building"&&thumbnails.Get(dragKind!)is { } model)ui.ModelIcon(model,new((int)p.X-37,(int)p.Y-66,74,74));else ui.Icon(icon,new((int)p.X-36,(int)p.Y-65,72,72));
        if(dragMode=="stock"||dragMode=="pile")ui.Badge(dragQuantity.ToString(),p+new Vector2(29,-16));
        if(commandPreview is { } preview)ui.Outline(preview.Result.Ok?"✓":"×",p+new Vector2(35,16),27);
    }
    private void TrackCollections(bool reload=false)
    {
        var world=simulation.World;
        if(!reload)foreach(var(item,amount)in world.Inventory){int gain=amount-lastStock.GetValueOrDefault(item);if(gain>0)flights.Add(new(item,gain,collectionOrigin,totalTime));}
        lastStock=world.Inventory.ToDictionary(p=>p.Key,p=>p.Value);
    }
    private bool IsBookHeader(Vector2 point){var p=point/ui.Scale;return bookView!=0&&new Rectangle(BookBounds.X+46,BookBounds.Y,BookBounds.Width-94,37).Contains(p);}
    private void MoveBook(Vector2 delta){bookPosition=(bookPosition??BookBounds.Location.ToVector2())+delta/ui.Scale;}
    private void TurnBook(int direction){if(bookView==1)bookPage=(bookPage+direction+(RecipeBook.All.Count+3)/4)%((RecipeBook.All.Count+3)/4);else bookRecipe=(bookRecipe+direction+RecipeBook.All.Count)%RecipeBook.All.Count;}
    private void DrawBook(int w,int h)
    {
        var world=simulation.World;const int width=310,height=405;var p=bookPosition??new Vector2(w-410,h-height-115);p.X=Math.Clamp(p.X,0,Math.Max(0,w-width));p.Y=Math.Clamp(p.Y,0,Math.Max(0,h-height));bookPosition=p;
        BookBounds=new((int)p.X,(int)p.Y,width,height);int x=BookBounds.X,y=BookBounds.Y;
        ui.Rect(new(x-5,y-5,width+10,height+10),new Color(37,92,84));ui.Panel(BookBounds);ui.Block(BookBounds);
        ui.Text("✓ "+world.Discoveries.Count+" / "+RecipeBook.All.Count,new(x+95,y+12),16);ui.Button(new(x+264,y+7,36,29),"×",()=>bookView=0,false,15);
        if(bookView==1)
        {
            for(int i=0;i<4&&bookPage*4+i<RecipeBook.All.Count;i++)
            {
                int index=bookPage*4+i,rowY=y+53+i*74;var recipe=RecipeBook.All[index];bool known=world.Discoveries.ContainsKey(recipe.Id);
                ui.Button(new(x+12,rowY,286,65),"",()=>{bookRecipe=index;bookView=2;});
                if(known)DrawEquation(recipe,new(x+20,rowY+2),266,45);else ui.Text("?    +    ?    =    ?",new(x+48,rowY+18),22);
            }
            ui.Text($"{bookPage+1} / {(RecipeBook.All.Count+3)/4}",new(x+130,y+370),14);
        }
        else
        {
            var recipe=RecipeBook.All[bookRecipe];bool known=world.Discoveries.ContainsKey(recipe.Id);
            ui.Button(new(x+9,y+7,39,29),"↶",()=>bookView=1,false,16);
            if(!known){ui.Text("?",new(x+129,y+108),52);ui.Text("?  +  ?  =  ?",new(x+65,y+230),25);}
            else
            {
                ui.Text(recipe.Title,new(x+22,y+55),19);
                int n=recipe.Ingredients.Length,step=110;for(int i=0;i<n;i++){int px=x+width/2+(i*2-n+1)*step/2;DrawPart(recipe.Ingredients[i],new(px-28,y+98),56);if(i<n-1)ui.Text("+",new(px+47,y+115),22);}
                ui.Text("↓",new(x+138,y+172),30);if(recipe.Tool is not null){ui.Icon(IconName(recipe.Tool),new(x+192,y+163,48,48));}
                DrawPart(recipe.Result,new(x+113,y+230),82);if(recipe.Byproducts is {Length:>0}){ui.Text("+",new(x+202,y+253),20);DrawPart(recipe.Byproducts[0],new(x+234,y+239),56);}
                ui.Text("✓ ×"+world.Discoveries[recipe.Id],new(x+119,y+336),15);ui.Text($"{bookRecipe+1} / {RecipeBook.All.Count}",new(x+123,y+373),13);
            }
        }
        ui.Button(new(x+16,y+366,46,29),"‹",()=>TurnBook(-1),false,20);ui.Button(new(x+247,y+366,46,29),"›",()=>TurnBook(1),false,20);
    }
    private void DrawEquation(RecipeDefinition recipe,Vector2 at,int width,int size)
    {
        var parts=recipe.Ingredients.Concat(recipe.Tool is null?[]:[new RecipeIngredient(recipe.Tool,"",1)]).Append(recipe.Result).ToArray();float step=width/(float)parts.Length;
        for(int i=0;i<parts.Length;i++){DrawPart(parts[i],at+new Vector2(i*step,0),size);if(i<parts.Length-1)ui.Text(i==parts.Length-2?"=":"+",at+new Vector2((i+1)*step-13,12),18);}
    }
    private void DrawPart(RecipeIngredient part,Vector2 at,int size)
    {
        if(thumbnails.Get(part.Kind)is { } image)ui.ModelIcon(image,new((int)at.X,(int)at.Y,size,size));else ui.Icon(IconName(part.Kind),new((int)at.X,(int)at.Y,size,size));
        if(part.Amount>1){ui.Rect(new((int)at.X+size-22,(int)at.Y+size-13,30,18),ui.Paper);ui.Text("×"+part.Amount,at+new Vector2(size-22,size-15),12);}
    }
}
