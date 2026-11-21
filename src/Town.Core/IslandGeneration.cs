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
    public static World Create()=>Create(Random.Shared.Next(1,int.MaxValue));
    public static World Create(int seed)
    {
        var world=new World(new int[Size*Size],seed);
        double R(int n)=>SeedNoise(seed,n*913,n*431,5);
        double radiusX=38+R(1)*2,radiusZ=36.5+R(2)*2.5;
        double phase=R(3)*Math.PI*2;
        // The ridge runs north-west to south-east so both ends meet the coast.
        double axis=.32+(R(4)-.5)*.45;
        double cos=Math.Cos(axis),sin=Math.Sin(axis);
        double ridgeX=-18+R(5)*4,ridgeZ=-19+R(6)*4;
        double ridgeLength=20+R(7)*3,ridgeWidth=7.5+R(8)*2;
        double crest=49+R(9)*7;
        for(int z=-Half;z<Half;z++)for(int x=-Half;x<Half;x++)
        {
            double nx=(x-OriginX)/radiusX,nz=(z-OriginZ)/radiusZ;
            double radial=Math.Sqrt(nx*nx+nz*nz);
            // The coastline bulges towards the south-east so the flat land is part of the island, not added to it.
            double seBulge=Math.Clamp(nx*.7+nz*.7,0,1);
            double angle=Math.Atan2(nz,nx);
            double coast=1+.09*Math.Sin(angle*3+phase)+.045*Math.Cos(angle*7-phase)
                +.08*(SmoothNoise(seed,x*.13,z*.13)-.5)+seBulge*seBulge*.13;
            if(StarterPlain(x,z)){world.heights[Index(x,z)]=2;continue;}
            if(radial>coast){world.heights[Index(x,z)]=0;continue;}

            double dx=x-ridgeX,dz=z-ridgeZ;
            double along=dx*cos+dz*sin,across=-dx*sin+dz*cos;
            // Ridged noise warps the crest line so the mountain reads as a chain, not a cone.
            double crestShift=(SmoothNoise(seed,x*.09+23,z*.09+7)-.5)*9;
            double spine=Math.Abs(across+crestShift);
            double taper=1-Math.Pow(Math.Clamp(Math.Abs(along)/ridgeLength,0,1),1.6);
            double body=Math.Max(0,taper)*Math.Exp(-Math.Pow(spine/(ridgeWidth*(.55+taper*.65)),1.6));
            // Local peaks along the spine give the ridge its summits and saddles.
            double peaks=.82+.18*Math.Sin(along*.30+seed%97);
            double mountain=body*crest*peaks;

            // North-east shoulder: the ridge continues as high ground under the forest.
            double shoulder=Bell(x,z,ridgeX+17,ridgeZ+6,16,11)*crest*.28;
            // South-west hills, terraced down towards the plain.
            double hills=Bell(x,z,-19,-4,15,13)*crest*.26+Bell(x,z,-13,16,12,10)*crest*.12;
            // Rolling ground everywhere else, fading out over the plain.
            double broad=SmoothNoise(seed,x*.09+11,z*.09+5),detail=SmoothNoise(seed,x*.26+3,z*.26+41);
            double ground=2+Math.Pow(broad,1.5)*3.2+detail;
            double rise=Math.Max(mountain,Math.Max(shoulder,hills))+ground;
            double coastSlope=Math.Clamp((coast-radial)*8,0,1);
            rise=2+(rise-2)*Math.Min(coastSlope,StarterBlend(x,z));
            world.heights[Index(x,z)]=Math.Clamp((int)Math.Floor(rise),2,MaxHeight);
        }
        var river=CarveRiver(world,seed);
        for(int z=-Half;z<Half;z++)for(int x=-Half;x<Half;x++)
        {
            int h=world.Height(x,z);
            if(h is <2 or >3||StarterPlain(x,z))continue;
            if(Ocean(x-1,z)||Ocean(x+1,z)||Ocean(x,z-1)||Ocean(x,z+1))world.heights[Index(x,z)]=1;
        }
        bool Ocean(int x,int z)=>world.Height(x,z)==0&&(!Inside(x,z)||!river[Index(x,z)]);
        for(int i=0;i<world.heights.Length;i++)world.surfaces[i]=DefaultSurface(world.heights[i]);
        world.PopulateStarter();
        world.nature.AddRange(PlantForest(world,seed));
        world.InitializeGameplay();
        return world;
    }
    private static double StarterBlend(int x,int z)
    {
        int dx=Math.Max(0,Math.Max(World.TownX-3-x,x-(World.TownX+16)));
        int dz=Math.Max(0,Math.Max(World.TownZ-8-z,z-(World.TownZ+15)));
        double t=Math.Clamp(Math.Sqrt(dx*dx+dz*dz)/9,0,1);
        return t*t*(3-2*t);
    }
    /// <summary>Starter buildings need flat, dry, unobstructed ground in the south-east.</summary>
    private static bool StarterPlain(int x,int z)=>
        x>=World.TownX-3&&x<=World.TownX+16&&z>=World.TownZ-8&&z<=World.TownZ+15;
    /// <summary>Carves a channel from the ridge flank down to the southern sea.</summary>
    private static bool[] CarveRiver(World world,int seed)
    {
        var river=new bool[Size*Size];
        int sourceZ=-12+(int)(SeedNoise(seed,7,11,19)*4),previousX=-8;
        double phase=SeedNoise(seed,3,7,29)*Math.PI*2;
        for(int z=sourceZ;z<Half;z++)
        {
            double length=z-sourceZ;
            int x=-8+(int)Math.Round(Math.Sin(length*.19+phase)*3.7+Math.Sin(length*.38)*1.3-length*.18);
            if(z==sourceZ)previousX=x;
            int start=Math.Min(x,previousX),end=Math.Max(x,previousX)+(z>12?1:0);
            if(z>28&&world.Height(x,z)==0&&world.Height(previousX,z)==0)break;
            for(int bank=start;bank<=end;bank++)
            {
                if(!Inside(bank,z)||StarterPlain(bank,z)||world.Height(bank,z)==0)continue;
                int index=Index(bank,z);
                world.heights[index]=0;river[index]=true;
            }
            previousX=x;
        }
        // Blend across and upstream of the channel so the headwaters sit in a valley.
        const int valleyRadius=10;
        for(int z=-Half;z<Half;z++)for(int x=-Half;x<Half;x++)
        {
            if(!river[Index(x,z)])continue;
            for(int dz=-valleyRadius;dz<=valleyRadius;dz++)for(int dx=-valleyRadius;dx<=valleyRadius;dx++)
            {
                int a=x+dx,b=z+dz,squared=dx*dx+dz*dz;
                if(squared==0||squared>valleyRadius*valleyRadius||!Inside(a,b)||StarterPlain(a,b))continue;
                int index=Index(a,b);
                int bankHeight=(int)Math.Round(2+Math.Sqrt(squared)*.8+squared*.29);
                world.heights[index]=Math.Min(world.heights[index],bankHeight);
            }
        }
        return river;
    }
    private static IEnumerable<NaturalObject> PlantForest(World world,int seed)
    {
        var trees=new List<NaturalObject>();
        for(int z=-Half+1;z<Half-1;z++)for(int x=-Half+1;x<Half-1;x++)
        {
            if(world.Occupant(x,z) is not null || StarterPlain(x,z) &&
                (x<=World.TownX+14&&z<=World.TownZ+11 || SeedNoise(seed,x,z,34)>.26))continue;
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
