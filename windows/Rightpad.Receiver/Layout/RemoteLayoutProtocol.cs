using System.Buffers.Binary;
using System.Text;

namespace Rightpad.Receiver;

internal sealed record LayoutRect(int X, int Y, int Width, int Height)
{
    public bool Valid(int width, int height) => X >= 0 && Y >= 0 && Width >= 20 && Height >= 20 &&
        (long)X + Width <= width && (long)Y + Height <= height;
}
internal sealed record LayoutItem(string Id, string Label, ushort Flags, LayoutRect Rect);
internal sealed record LayoutMessage(byte Kind, byte State, byte Error, ulong Run, ulong Epoch, ulong Revision,
    ulong Geometry, ulong Transaction, ulong Request, ulong Next, ulong Serial,
    int Remaining, int Width, int Height, LayoutItem[] Items);

internal static class RemoteLayoutProtocol
{
    public const byte Query = 1, Try = 2, Keep = 3, Revert = 4, Snapshot = 128;
    public const int Header = 88, Max = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static bool Matches(ReadOnlySpan<byte> b) => b.Length >= 4 && b[..4].SequenceEqual("RPLY"u8);
    public static LayoutMessage? Decode(byte[] bytes)
    {
        if (bytes.Length < Header || bytes.Length > Max || !Matches(bytes)) return null;
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes), Utf8);
            r.ReadUInt32();
            if (r.ReadByte() != 1) return null;
            byte kind = r.ReadByte(), state = r.ReadByte(), error = r.ReadByte();
            if ((kind < 1 || kind > 4) && kind != Snapshot || state > 5 || error > 6) return null;
            ulong run = r.ReadUInt64(), epoch = r.ReadUInt64(), revision = r.ReadUInt64(), geometry = r.ReadUInt64();
            ulong txn = r.ReadUInt64(), request = r.ReadUInt64(), next = r.ReadUInt64(), serial = r.ReadUInt64();
            int remaining = r.ReadInt32(), width = r.ReadInt32(), height = r.ReadInt32();
            int count = r.ReadUInt16();
            if (r.ReadUInt16() != 0 || count > 16 || remaining < 0 || remaining > 60000) return null;
            var items = new List<LayoutItem>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                int idLength = r.ReadByte(), labelLength = r.ReadByte(); ushort flags = r.ReadUInt16();
                var rect = new LayoutRect(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                if (idLength < 1 || idLength > 64 || labelLength > 64 || flags > 1) return null;
                string id = Utf8.GetString(r.ReadBytes(idLength)), label = Utf8.GetString(r.ReadBytes(labelLength));
                if (Utf8.GetByteCount(id) != idLength || Utf8.GetByteCount(label) != labelLength || !ids.Add(id)) return null;
                items.Add(new(id, label, flags, rect));
            }
            if (r.BaseStream.Position != bytes.Length || (kind is Query or Keep or Revert && count != 0)) return null;
            if (kind == Snapshot && (epoch == 0 || revision == 0 || geometry == 0 || serial == 0 ||
                width < 20 || height < 20 || items.Count == 0 || !items.Any(i => i.Flags == 0) ||
                items.Any(i => !i.Rect.Valid(width, height)))) return null;
            return new(kind, state, error, run, epoch, revision, geometry, txn, request, next, serial,
                remaining, width, height, items.ToArray());
        }
        catch (Exception e) when (e is IOException or ArgumentException) { return null; }
    }
    public static byte[] Encode(LayoutMessage m)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Utf8);
        w.Write("RPLY"u8); w.Write((byte)1); w.Write(m.Kind); w.Write(m.State); w.Write(m.Error);
        w.Write(m.Run); w.Write(m.Epoch); w.Write(m.Revision); w.Write(m.Geometry); w.Write(m.Transaction);
        w.Write(m.Request); w.Write(m.Next); w.Write(m.Serial); w.Write(m.Remaining); w.Write(m.Width); w.Write(m.Height);
        w.Write((ushort)m.Items.Length); w.Write((ushort)0);
        foreach (var i in m.Items)
        {
            byte[] id = Utf8.GetBytes(i.Id), label = Utf8.GetBytes(i.Label);
            if (id.Length is < 1 or > 64 || label.Length > 64) throw new ArgumentException("Layout strings too long");
            w.Write((byte)id.Length); w.Write((byte)label.Length); w.Write(i.Flags);
            w.Write(i.Rect.X); w.Write(i.Rect.Y); w.Write(i.Rect.Width); w.Write(i.Rect.Height); w.Write(id); w.Write(label);
        }
        if (stream.Length > Max) throw new ArgumentException("Layout datagram too large");
        return stream.ToArray();
    }
}
