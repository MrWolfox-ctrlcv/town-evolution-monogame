namespace Town.Core;

public enum Surface { Water, Sand, Grass, Rock }
public readonly record struct Tile(Surface Surface, int Height, int? BuildingId);
public static class WorldQueries
{
    public static Tile TileAt(this World world, int x, int z)
    {
        int h = world.Height(x, z);
        return new(h == 0 ? Surface.Water : h == 1 ? Surface.Sand : h >= World.RockLine ? Surface.Rock : Surface.Grass, h, world.Occupant(x, z)?.Id);
    }
    public const int ChunkSize = 8;
    public static int ChunksPerAxis => World.Size / ChunkSize;
    public static int ChunkCount => ChunksPerAxis * ChunksPerAxis;
    public static Cell ChunkAt(Cell cell) => new((cell.X + World.Half) / ChunkSize, (cell.Z + World.Half) / ChunkSize);
    public static IEnumerable<Cell> AllChunks() { for (int z = 0; z < ChunksPerAxis; z++) for (int x = 0; x < ChunksPerAxis; x++) yield return new(x, z); }
    public static HashSet<Cell> AffectedChunks(IEnumerable<Cell> cells)
    {
        var result = new HashSet<Cell>();
        foreach (var cell in cells)
            foreach (var offset in new[] { new Cell(0, 0), new(-1, 0), new(1, 0), new(0, -1), new(0, 1) })
            {
                var p = new Cell(cell.X + offset.X, cell.Z + offset.Z);
                if (World.Inside(p.X, p.Z)) result.Add(ChunkAt(p));
            }
        return result;
    }
}
