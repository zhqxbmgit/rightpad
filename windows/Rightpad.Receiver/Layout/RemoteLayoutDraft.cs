namespace Rightpad.Receiver;

/** No transport or persistence dependency: every edit is only a Windows draft. */
internal sealed class RemoteLayoutDraft
{
    public LayoutMessage? Basis { get; private set; }
    public LayoutItem[] Items { get; private set; } = [];
    public bool Dirty { get; private set; }
    public bool Conflict { get; private set; }
    public string? Selected { get; set; }
    public LayoutItem? Selection => Items.FirstOrDefault(i => i.Id == Selected);
    public void Load(LayoutMessage m)
    {
        Basis = m; Items = m.Items.Where(i => i.Flags == 0).ToArray(); Dirty = Conflict = false;
        if (!Items.Any(i => i.Id == Selected)) Selected = Items.FirstOrDefault()?.Id;
    }
    public void Observe(LayoutMessage m)
    {
        if (Basis is null || m.State > 0 && (!Dirty || m.Transaction != Basis.Transaction || m.State != Basis.State))
        { Load(m); return; }
        if (m.Epoch != Basis.Epoch || m.Revision != Basis.Revision || m.Geometry != Basis.Geometry)
        {
            if (Dirty) Conflict = true;
            else Load(m);
        }
    }
    public bool Adjust(int field, int delta)
    {
        if (Basis is null || Selection is not { } selected || Conflict) return false;
        var r = selected.Rect;
        long[] values = [r.X, r.Y, r.Width, r.Height]; values[field] += delta;
        if (values.Any(v => v < 0 || v > int.MaxValue)) return false;
        var next = new LayoutRect((int)values[0], (int)values[1], (int)values[2], (int)values[3]);
        if (!next.Valid(Basis.Width, Basis.Height)) return false;
        Items = Items.Select(i => i.Id == Selected ? i with { Rect = next } : i).ToArray(); Dirty = true;
        return true;
    }
}
