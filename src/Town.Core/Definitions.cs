namespace Town.Core;

public enum Surface { Water, Sand, Grass, Rock }
public readonly record struct Tile(Surface Surface, int Height, int? BuildingId);
public static class WorldQueries
{
    public static Tile TileAt(this World world, int x, int z)
    {
        int h = world.Height(x, z);
        return new(h == 0 ? Surface.Water : h == 1 ? Surface.Sand : h >= 6 ? Surface.Rock : Surface.Grass, h, world.Occupant(x, z)?.Id);
    }
    public const int ChunkSize = 8;
    public static Cell ChunkAt(Cell cell) => new((cell.X + World.Half) / ChunkSize, (cell.Z + World.Half) / ChunkSize);
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
