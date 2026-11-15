namespace Town.Core;

public sealed partial class World
{
    // Schema 4: a 96x96 island laid out north-up.
    //   north-west  : the main ridge, the only source of stone
    //   north-east  : forest on the ridge shoulder
    //   south-west  : hills and high slopes
    //   south-east  : the coastal plain the town is built on
    // The seed rebuilds the same coast, ridges and woodland, so an unedited island stays reproducible.
    private const double OriginX = 5, OriginZ = 2;
    // Screen north is -Z; the settlement sits in the south-east quadrant.
    private const double PlainX = 17, PlainZ = 17;
    public static World Create()=>Create(Random.Shared.Next(1,int.MaxValue));
    public static World Create(int seed)
    {
        var world=new World(new int[Size*Size],seed);
        double R(int n)=>SeedNoise(seed,n*913,n*431,5);
        double radiusX=41+R(1)*2.5,radiusZ=38.5+R(2)*3;
        double phase=R(3)*Math.PI*2;
        // The ridge runs north-west to south-east so both ends meet the coast.
        double axis=(R(4)-.5)*.6;
        double cos=Math.Cos(axis),sin=Math.Sin(axis);
        double ridgeX=-18+R(5)*4,ridgeZ=-19+R(6)*4;
        double ridgeLength=15+R(7)*3.5,ridgeWidth=7.5+R(8)*2;
        double crest=62+R(9)*7;
        for(int z=-Half;z<Half;z++)for(int x=-Half;x<Half;x++)
        {
            double nx=(x-OriginX)/radiusX,nz=(z-OriginZ)/radiusZ;
            double radial=Math.Sqrt(nx*nx+nz*nz);
            // The coastline bulges towards the south-east so the flat land is part of the island, not added to it.
            double seBulge=Math.Clamp(nx*.7+nz*.7,0,1);
            double coast=1+.07*Math.Sin(Math.Atan2(nz,nx)*5+phase)+.06*(SmoothNoise(seed,x*.17,z*.17)-.5)+seBulge*seBulge*.22;
            if(radial>coast){world.heights[Index(x,z)]=0;continue;}
            bool plain=PlainZone(x,z);

            double dx=x-ridgeX,dz=z-ridgeZ;
            double along=dx*cos+dz*sin,across=-dx*sin+dz*cos;
            // Ridged noise warps the crest line so the mountain reads as a chain, not a cone.
            double crestShift=(SmoothNoise(seed,x*.11+23,z*.11+7)-.5)*16;
            double spine=Math.Abs(across+crestShift);
            double taper=1-Math.Pow(Math.Clamp(Math.Abs(along)/ridgeLength,0,1),1.6);
            double body=Math.Max(0,taper)*Math.Exp(-Math.Pow(spine/(ridgeWidth*(.55+taper*.65)),1.6));
            // Local peaks along the spine give the ridge its summits and saddles.
            double peaks=.72+.28*Math.Sin(along*.34+seed%97);
            double mountain=body*crest*peaks;

            // North-east shoulder: the ridge continues as high ground under the forest.
            double shoulder=PlainBlend(x,z)*Bell(x,z,ridgeX+ridgeLength*.55+7,ridgeZ-ridgeLength*.35-4,11,10)*crest*.22;
            // South-west hills, terraced down towards the plain.
            double hills=Bell(x,z,-19,-2,15,13)*crest*.20+Bell(x,z,-8,15,11,9)*crest*.12;
            // Rolling ground everywhere else, fading out over the plain.
            double broad=SmoothNoise(seed,x*.09+11,z*.09+5),detail=SmoothNoise(seed,x*.26+3,z*.26+41);
            double ground=2+Math.Pow(broad,1.5)*3.2+detail*1.1;
            double rise=Math.Max(mountain,0)+shoulder+hills+ground*PlainBlend(x,z);
            int height=(int)Math.Floor(rise);
            if(plain)height=Math.Min(height,2);
            if(StarterPlain(x,z))height=2;
            world.heights[Index(x,z)]=Math.Clamp(height,1,MaxHeight);
        }
        CarveRiver(world,seed);
        for(int z=-Half;z<Half;z++)for(int x=-Half;x<Half;x++)
        {
            int h=world.Height(x,z);
            if(h is <2 or >3)continue;
            if(new[]{new Cell(x-1,z),new Cell(x+1,z),new Cell(x,z-1),new Cell(x,z+1)}.Any(c=>world.Height(c.X,c.Z)==0))world.heights[Index(x,z)]=1;
        }
        for(int i=0;i<world.heights.Length;i++)world.surfaces[i]=DefaultSurface(world.heights[i]);
        world.PopulateStarter();
        world.nature.AddRange(PlantForest(world,seed));
        world.InitializeGameplay();
        return world;
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
        x>=World.TownX-3&&x<=World.TownX+16&&z>=World.TownZ-8&&z<=World.TownZ+15;
    /// <summary>Carves a channel from the ridge flank down to the southern sea.</summary>
    private static void CarveRiver(World world,int seed)
    {
        double startX=-6+SeedNoise(seed,5,3,17)*6,startZ=-4+SeedNoise(seed,7,11,19)*6;
        double direction=Math.PI*.5+(SeedNoise(seed,3,7,29)-.5)*.9;
        double drift=0;
        for(double t=0;t<70;t+=.5)
        {
            drift+=(SeedNoise(seed,(int)(t*4),11,31)-.5)*.16;
            double angle=direction+drift;
            int cx=(int)Math.Floor(startX+Math.Cos(angle)*t),cz=(int)Math.Floor(startZ+Math.Sin(angle)*t);
            if(!Inside(cx,cz))break;
            int h=world.Height(cx,cz);
            if(h==0&&t>2)break;
            for(int dx=0;dx<2;dx++)
            {
                int px=cx+dx;
                if(!Inside(px,cz)||StarterPlain(px,cz))continue;
                world.heights[Index(px,cz)]=0;
            }
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