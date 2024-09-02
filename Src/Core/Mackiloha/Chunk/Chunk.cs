namespace Mackiloha.Chunk;

// Successor to Milo container (Used in FME/DCS/RBVR)
public class Chunk
{
    private const uint CHNK_MAGIC = 0x43484E4B; // "CHNK"
    private const uint IS_COMPRESSED = 0x01_00_00_00;

    public Chunk()
    {
        Entries = new List<ChunkEntry>();
    }

    public void WriteToFile(string outPath, bool writeHeader = true)
    {
        using var fs = File.OpenWrite(outPath);
        WriteToStream(fs, writeHeader);
    }

    public void WriteToStream(Stream stream, bool writeHeader = true)
    {
        AwesomeWriter aw = new AwesomeWriter(stream, true);

        if (writeHeader)
        {
            int endianFlag = 0xFF;
            short extraShort = 2;

            if (IsDurango)
            {
                endianFlag = 0x1FF;
                extraShort = 5;
            }

            aw.Write(CHNK_MAGIC);
            aw.Write((int)endianFlag);

            aw.BigEndian = !IsDurango;

            aw.Write(Entries.Count);
            aw.Write(Entries.Max(x => x.Data.Length));
            aw.Write((short)1);
            aw.Write((short)extraShort);

            int currentIdx = 20 + (Entries.Count * 16);

            // Writes block details
            foreach (ChunkEntry entry in Entries)
            {
                aw.Write(entry.Data.Length);
                aw.Write(entry.Data.Length);

                if (IsDurango)
                {
                    aw.Write(currentIdx);
                    aw.Write((int)(entry.Compressed ? IS_COMPRESSED : 0));
                }
                else
                {
                    aw.Write((int)(entry.Compressed ? IS_COMPRESSED : 0));
                    aw.Write(currentIdx);
                }

                currentIdx += entry.Data.Length;
            }
        }

        // Writes blocks
        Entries.ForEach(x => aw.Write(x.Data));
    }

    public static void DecompressChunkFile(string inPath, string outPath, bool writeHeader = true)
    {
        Chunk chunk;
        using (var fs = File.OpenRead(inPath))
        {
            chunk = ReadFromStream(fs);
        }

        chunk.WriteToFile(outPath, writeHeader);
    }

    private static Chunk ReadFromStream(Stream stream)
    {
        Chunk chunk = new Chunk();
        AwesomeReader ar = new AwesomeReader(stream, true);

        if (ar.ReadUInt32() != CHNK_MAGIC) return chunk;

        var flag = ar.ReadUInt32();
        if ((flag & 0x100) != 0)
        {
            chunk.IsDurango = true;
            ar.BigEndian = false;
        }

        int blockCount = ar.ReadInt32();
        ar.BaseStream.Position += 8; // Skips 1, 2/5 (16-bits)

        int[] blockSize = new int[blockCount];
        bool[] compressed = new bool[blockCount]; // Uncompressed by default

        // Reads block details
        for (int i = 0; i < blockCount; i++)
        {
            blockSize[i] = ar.ReadInt32();
            ar.BaseStream.Position += 4; // Decompressed size (Not needed)

            // Fields are swapped depending on platform
            if (chunk.IsDurango)
            {
                ar.BaseStream.Position += 4; // Offset (Not needed)
                compressed[i] = ar.ReadInt32() == IS_COMPRESSED;
            }
            else
            {
                compressed[i] = ar.ReadInt32() == IS_COMPRESSED;
                ar.BaseStream.Position += 4; // Offset (Not needed)
            }
        }

        for (int i = 0; i < blockCount; i++)
        {
            // Reads block bytes
            byte[] block = ar.ReadBytes(blockSize[i]);

            // Decompresses if needed
            if (block.Length > 0 && compressed[i])
            {
                block = Compression.InflateBlock(block, CompressionType.ZLIB);
                blockSize[i] = block.Length;
            }

            chunk.Entries.Add(new ChunkEntry()
            {
                Data = block,
                Compressed = false
            });

            // TODO: Write blocks to stream, and parse internal file system instead
            // ms.Write(block, 0, block.Length);
        }

        return chunk;
    }

    public bool IsDurango { get; set; }
    public List<ChunkEntry> Entries { get; set; }
}

public class ChunkEntry
{
    public byte[] Data;
    public bool Compressed;
}
