using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SCSKiller.Core.Warming;

public sealed record Ps4ShaderEntry(string Archive, string Name, string Stage, string? Hash, int Bytes, int? CodeOffset);

/// <summary>Reads shader assets from zlib PSARC archives without extracting or changing game files.</summary>
public static class ShadPs4ShaderIndex
{
    const int MaxEntry = 64 << 20;

    public static IReadOnlyList<Ps4ShaderEntry> Read(string gameDirectory, Action<Ps4ShaderEntry, byte[]>? visitor = null)
    {
        var result = new List<Ps4ShaderEntry>();
        foreach (var path in Directory.EnumerateFiles(gameDirectory, "*.psarc", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(path);
            var header = new byte[32];
            stream.ReadExactly(header);
            if (!header.AsSpan(0, 4).SequenceEqual("PSAR"u8) || !header.AsSpan(8, 4).SequenceEqual("zlib"u8))
                throw new InvalidDataException($"Unsupported PSARC compression: {path}");
            var tocSize = checked((int)U32(header, 12));
            var entrySize = checked((int)U32(header, 16));
            var count = checked((int)U32(header, 20));
            var blockSize = checked((int)U32(header, 24));
            if (entrySize != 30 || count < 1 || blockSize != 65536 || tocSize < 32 || tocSize > MaxEntry
                || (long)entrySize * count > tocSize - 32)
                throw new InvalidDataException($"Unsupported PSARC table: {path}");
            var table = new byte[tocSize - 32];
            stream.ReadExactly(table);
            byte[] Entry(int index)
            {
                var offset = checked(index * entrySize);
                var block = U32(table, offset + 16);
                var size = U40(table, offset + 20);
                var position = U40(table, offset + 25);
                if (size > MaxEntry || position > stream.Length) throw new InvalidDataException("PSARC entry exceeds bounds.");
                var data = new byte[(int)size];
                stream.Position = position;
                var written = 0;
                while (written < data.Length)
                {
                    var blockOffset = checked(entrySize * count + (int)block++ * 2);
                    if (blockOffset < 0 || blockOffset > table.Length - 2) throw new InvalidDataException("PSARC block table exceeds bounds.");
                    var stored = (int)BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(blockOffset, 2));
                    if (stored == 0) stored = blockSize;
                    var expected = Math.Min(blockSize, data.Length - written);
                    var bytes = new byte[stored];
                    stream.ReadExactly(bytes);
                    if (stored == expected) bytes.CopyTo(data, written);
                    else
                    {
                        using var compressed = new MemoryStream(bytes, false);
                        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                        zlib.ReadExactly(data.AsSpan(written, expected));
                        if (zlib.ReadByte() != -1) throw new InvalidDataException("PSARC block has excess decoded data.");
                    }
                    written += expected;
                }
                return data;
            }
            var names = Encoding.UTF8.GetString(Entry(0)).TrimEnd('\0', '\r', '\n').Split('\n');
            if (names.Length != count - 1) throw new InvalidDataException($"PSARC name count mismatch: {path}");
            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i].TrimEnd('\r');
                var extension = Path.GetExtension(name).ToLowerInvariant();
                if (extension is not (".vxo" or ".pxo" or ".cxo" or ".gxo" or ".hxo" or ".dxo")) continue;
                var bytes = Entry(i + 1);
                var info = bytes.AsSpan().IndexOf("OrbShdr"u8);
                string? hash = null;
                int? codeOffset = null;
                if (info >= 0 && info <= bytes.Length - 28)
                {
                    var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(info + 8, 4)) >> 8;
                    if (length > 0 && length % 4 == 0 && length <= info)
                        for (var at = Math.Max(0, info - 65536); at <= info - 8; at += 4)
                        {
                            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)) != 0xBEEB03FF) continue;
                            var displacement = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4, 4));
                            if ((long)at + ((long)displacement + 1) * 8 != info || at + length > bytes.Length) continue;
                            codeOffset = at;
                            hash = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(info + 16, 8)).ToString("x16");
                            break;
                        }
                }
                var entry = new Ps4ShaderEntry(Path.GetRelativePath(gameDirectory, path), name, extension[1..], hash, bytes.Length, codeOffset);
                result.Add(entry);
                visitor?.Invoke(entry, bytes);
            }
        }
        return result;
    }

    static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
    static long U40(byte[] bytes, int offset) => ((long)bytes[offset] << 32) | U32(bytes, offset + 1);
}
