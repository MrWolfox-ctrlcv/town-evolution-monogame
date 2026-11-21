namespace Town.Core;

public sealed partial class World
{
    // Schema 4: a 96x96 island laid out north-up.
    //   north-west  : the main ridge, the only source of stone
    //   north-east  : forest on a slope that rises out of the ridge
    //   south-west  : a spur of the same ridge, terraced down into hills
    //   south-east  : the coastal plain the town is built on
    // The seed rebuilds the same coast, ridges and woodland, so an unedited island stays reproducible.
    private const double OriginX = 5, OriginZ = 2;
    // The settlement sits in the south-east quadrant.
    private const double PlainX = 17, PlainZ = 17;
    public static World Create()=>Create(Random.Shared.Next(1,int.MaxValue));
    public static World Create(int seed)
    {
        var world=new World(new int[Size*Size],seed);
        double R(int n)=>SeedNoise(seed,n*913,n*431,5);
        double radiusX=41+R(1)*2.5,radiusZ=38.5+R(2)*3;
        double phase=R(3)*Math.PI*2;
        // The main ridge runs north-west to south-east so both ends meet the coast.
        double axis=(R(4)-.5)*.6;
        double cos=Math.Cos(axis),sin=Math.Sin(axis);
        double ridgeX=-19+R(5)*4,ridgeZ=-20+R(6)*4;
        double ridgeLength=16+R(7)*3,ridgeWidth=7.5+R(8)*2;
        double crest=62+R(9)*7;
        // A lower spur leaves the north-west ridge and runs south-west; the hills grow out of it.
        double spurAngle=axis-1.15;
        double spurX=Math.Cos(spurAngle),spurZ=Math.Sin(spurAngle);
        for(int z=-Half;z<Half;z++)for(int x=-Half;x<Half;x++)
        {
            double nx=(x-OriginX)/radiusX,nz=(z-OriginZ)/radiusZ;
            double radial=Math.Sqrt(nx*nx+nz*nz);
            // The coastline bulges towards the south-east so the flat land is part of the island, not added to it.
            double seBulge=Math.Clamp(nx*.7+nz*.7,0,1);
            double coast=1+.07*Math.Sin(Math.Atan2(nz,nx)*5+phase)+.06*(SmoothNoise(seed,x*.17,z*.17)-.5)+seBulge*seBulge*.22;
            if(radial>coast){world.heights[Index(x,z)]=0;continue;}
            bool plain=PlainZone(x,z);

            // --- main ridge ---
            double dx=x-ridgeX,dz=z-ridgeZ;
            double along=dx*cos+dz*sin,across=-dx*sin+dz*cos;
            double crestShift=(SmoothNoise(seed,x*.11+23,z*.11+7)-.5)*16;
            double spine=Math.Abs(across+crestShift);
            double taper=1-Math.Pow(Math.Clamp(Math.Abs(along)/ridgeLength,0,1),1.6);
            double body=Math.Max(0,taper)*Math.Exp(-Math.Pow(spine/(ridgeWidth*(.55+taper*.65)),1.6));
            double peaks=.72+.28*Math.Sin(along*.34+seed%97);
            double mountain=body*crest*peaks;

            // --- south-west spur, firmly attached to the ridge ---
            double sx=x-(ridgeX+cos*2),sz=z-(ridgeZ+sin*2);
            double sAlong=(sx*spurX+sz*spurZ)/30,sAcross=Math.Abs(-sx*spurZ+sz*spurX)/11;
            double sTaper=1-Math.Pow(Math.Clamp(sAlong,0,1),1.35);
            double spurHeight=sAlong>0?sTaper*sTaper*Math.Exp(-sAcross*sAcross*1.5)*crest*.34:0;

            // --- north-east shoulder: high ground under the forest, rising from the plain ---
            double neBand=Math.Clamp((22-z)/26.0,0,1)*Math.Clamp((x+6)/24.0,0,1);
            double shoulder=neBand*neBand*crest*.20*Bell(x,z,ridgeX+16,ridgeZ+10,20,17);

            // --- rolling ground everywhere else, fading out over the plain ---
            double broad=SmoothNoise(seed,x*.09+11,z*.09+5),detail=SmoothNoise(seed,x*.26+3,z*.26+41);
            double ground=2+Math.Pow(broad,1.5)*3.2+detail*1.1;
            double rise=Math.Max(mountain,spurHeight)+shoulder+ground*PlainBlend(x,z);
            int height=(int)Math.Floor(rise);
            if(plain)height=2;
            // Land sits at two layers or higher; layer one is reserved for the beach ring.
            world.heights[Index(x,z)]=Math.Clamp(height,2,MaxHeight);
        }
        CarveRiver(world,seed);
        // One beach ring: the lowest land cells that touch the sea drop to a single sandy layer.
        for(int z=-Half;z<Half;z++)for(int x=-Half;x<Half;x++)
        {
            int h=world.Height(x,z),index=Index(x,z);
            if(h is <2 or >4||world.river[index])continue;
            if(!Seaside(world,world.river,x,z))continue;
            world.heights[index]=1;
        }
        for(int i=0;i<world.heights.Length;i++)
            world.nature.Count.ToString();
        for(int i=0;i<world.heights.Length;i++)
            world.surfaces[i]=DefaultSurface(world.heights[i]);
        world.PopulateStarter();
        world.nature.AddRange(PlantForest(world,seed));
        world.InitializeGameplay();
        return world;
    }
    /// <summary>True when a cell touches the ocean (not the river) and is low enough to be a beach.</summary>
    private static bool Seaside(World world,bool[] river,int x,int z)
    {
        foreach(var (dx,dz) in new[]{(-1,0),(1,0),(0,-1),(0,1)})
        {
            int a=x+dx,b=z+dz;
            if(!Inside(a,b))continue;
            int index=Index(a,b);
            if(world.heights[index]==0&&!river[index])return true;
        }
        return false;
    }
    /// <summary>Southern coastal plain: east of the middle, south of the middle.</summary>
    private static bool PlainZone(int x,int z)=>x>=4&&z>=2;
    /// <summary>Zero on the plain, one everywhere else, with a soft boundary.</summary>
    private static double PlainBlend(int x,int z)
    {
        double ex=Math.Clamp((20-x)/12.0,0,1),ez=Math.Clamp((18-z)/12.0,0,1);
        double plain=Math.Min(ex,ez);
        return 1-plain*plain;
    }
    /// <summary>Starter buildings need flat, dry, unobstructed ground in the south-east.</summary>
    private static bool StarterPlain(int x,int z)=>
        x>=World.TownX-3&&x<=World.TownX+16&&z>=World.TownZ-4&&z<=World.TownZ+15;
    /// <summary>Carves a meandering channel from the ridge foot to the southern sea; banks stay grass.</summary>
    private static void CarveRiver(World world,int seed)
    {
        double startX=-4+SeedNoise(seed,5,3,17)*5,startZ=-8+SeedNoise(seed,7,11,19)*5;
        double direction=Math.PI*.5+(SeedNoise(seed,3,7,29)-.5)*.7;
        double drift=0;
        for(double t=0;t<76;t+=.5)
        {
            drift+=(SeedNoise(seed,(int)(t*4),11,31)-.5)*.22;
            double angle=direction+drift;
            int cx=(int)Math.Floor(startX+Math.Cos(angle)*t),cz=(int)Math.Floor(startZ+Math.Sin(angle)*t);
            if(!Inside(cx,cz))break;
            int h=world.Height(cx,cz);
            if(h==0&&t>2)break;
            // The channel never cuts the settlement or the shoreline terrace.
            if(StarterPlain(cx,cz))continue;
            world.heights[Index(cx,cz)]=0;world.river[Index(cx,cz)]=true;
        }
    }
    private static IEnumerable<NaturalObject> PlantForest(World world,int seed)
    {
        var trees=new List<NaturalObject>();
        for(int z=-Half+1;z<Half-1;z++)for(int x=-Half+1;x<Half-1;x++)
        {
            if(StarterPlain(x,z))continue;
            int h=world.Height(x,z);
            if(h is <2 || h>=World.RockLine)continue;
            if(world.river[Index(x,z)])continue;
            // Dense woodland in the north-east, thinner groves elsewhere.
            double east=Math.Clamp((x+10)/26.0,0,1),north=Math.Clamp((6-z)/28.0,0,1);
            double bias=east*north*.55;
            double density=SmoothNoise(seed,x*.12+31,z*.12+17)+bias;
            double relief=Math.Min(1,(h-1)/9.0);
            if(density<.58-relief*.1)continue;
            if(SeedNoise(seed,x,z,21)>.66)continue;
            double variant=SeedNoise(seed,x,z,22)*.49+(SeedNoise(seed,x,z,23)>.82?.51:0);
            trees.Add(new(x,z,variant));
        }
        return trees.OrderBy(t=>SeedNoise(seed,t.X,t.Z,24)).Take(620).OrderBy(t=>t.Z).ThenBy(t=>t.X);
    }
    private static double Bell(double x,double z,double cx,double cz,double rx,double rz)=>Math.Exp(-Math.Pow((x-cx)/rx,2)-Math.Pow((z-cz)/rz,2));
    private static double SmoothNoise(int seed,double x,double z)
    {
        int ix=(int)Math.Floor(x),iz=(int)Math.Floor(z);double fx=x-ix,fz=z-iz;fx=fx*fx*(3-2*fx);fz=fz*fz*(3-2*fz);
        double a=SeedNoise(seed,ix,iz,8)*(1-fx)+SeedNoise(seed,ix+1,iz,8)*fx,b=SeedNoise(seed,ix,iz+1,8)*(1-fx)+SeedNoise(seed,ix+1,iz+1,8)*fx;return a*(1-fz)+b*fz;
    }
    private static double SeedNoise(int seed,int x,int z,int salt)
    {
        unchecked{uint n=(uint)seed*747796405u^(uint)x*374761393u^(uint)z*668265263u^(uint)salt*2246822519u;n=(n^(n>>16))*2246822519u;n=(n^(n>>13))*3266489917u;return(n^(n>>16))/4294967296.0;}
    }
}