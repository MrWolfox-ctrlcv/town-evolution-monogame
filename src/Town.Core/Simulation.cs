namespace Town.Core;

public readonly record struct WorldChange(int Revision, string Type, IReadOnlyList<Cell> Cells, int? ObjectId);

/// <summary>Fixed 20 Hz simulation, command queue and bounded undo. Rendering consumes changes only.</summary>
public sealed class Simulation
{
    public const double StepSeconds = 1.0 / 20;
    public World World { get; private set; } = World.Create();
    private readonly Queue<WorldCommand> pending = new();
    private readonly Queue<WorldChange> changes = new();
    private readonly List<string> undo = [];
    private double accumulated;
    public long Ticks { get; private set; }
    public int Revision { get; private set; }
    public Result LastResult { get; private set; } = new(true, "欢迎来到春溪岛");
    public int PendingCount => pending.Count;
    public void Enqueue(WorldCommand command)
    {
        if (pending.Count < 32) pending.Enqueue(command);
    }
    public void Advance(double realSeconds)
    {
        if (!double.IsFinite(realSeconds) || realSeconds < 0) return;
        accumulated += Math.Min(realSeconds, .25);
        while (accumulated + 1e-10 >= StepSeconds)
        {
            accumulated -= StepSeconds; Ticks++;
            if(World.AdvancePlay())changes.Enqueue(new(++Revision,"growth",[],null));
            while (pending.TryDequeue(out var command)) Apply(command);
        }
    }
    private void Apply(WorldCommand command)
    {
        var old = command.Type=="move" ? World.Objects.FirstOrDefault(o=>o.Id==command.ObjectId) : World.Occupant(command.X, command.Z);
        var before = SaveCodec.Encode(World);
        LastResult = World.Execute(command);
        if (!LastResult.Ok) return;
        undo.Add(before); if (undo.Count > 30) undo.RemoveAt(0);
        var item = command.Type == "place" ? World.Objects[^1] : old;
        Cell[] cells = item is not null ? World.Cells(item).ToArray() : [new(command.X, command.Z)];
        if(command.Type=="move")cells=cells.Concat(World.Cells(World.Objects.First(o=>o.Id==command.ObjectId))).Distinct().ToArray();
        changes.Enqueue(new(++Revision, command.Type, cells, item?.Id));
    }
    public bool TryChange(out WorldChange change) => changes.TryDequeue(out change);
    public bool Undo()
    {
        if (undo.Count == 0) return false;
        var restored = SaveCodec.Decode(undo[^1]); undo.RemoveAt(undo.Count - 1);
        Restore(restored); LastResult = new(true, "已撤销上一步"); return true;
    }
    public void Load(string json)
    {
        var restored = SaveCodec.Decode(json); // Validate completely before changing live world.
        undo.Clear(); Restore(restored);
    }
    private void Restore(World world)
    {
        World = world; pending.Clear(); changes.Clear();
        changes.Enqueue(new(++Revision, "reload", [], null));
    }
}
