using System;
using System.IO;
using System.Text;

namespace CursedAppleMapConvert
{
    /// <summary>Deadlock's baked lightmaps: their compressed blocks lifted out of a .vtex_c, untouched.</summary>
    internal static class Lightmaps
    {
        private const ushort FormatBc6h = 19;

        /// <summary>
        /// Copies a square, single-mip, uncompressed BC6H texture's blocks from a Source 2 .vtex_c to <paramref name="dst"/>
        /// (rows top to bottom, as Direct3D lays them out). Returns its size, or 0 if it isn't one.
        /// </summary>
        public static int ExtractBc6h(string vtex, string dst)
        {
            using var fs = File.OpenRead(vtex);
            var br = new BinaryReader(fs);
            // Resource header: file size, header version, type version, then the block table (offsets relative to themselves).
            br.ReadUInt32(); br.ReadUInt16(); br.ReadUInt16();
            long tablePos = fs.Position;
            uint tableOffset = br.ReadUInt32();
            uint blockCount = br.ReadUInt32();
            long dataStart = -1, dataSize = 0;
            fs.Position = tablePos + tableOffset;
            for (int i = 0; i < blockCount; i++)
            {
                string type = Encoding.ASCII.GetString(br.ReadBytes(4));
                long here = fs.Position;
                uint offset = br.ReadUInt32();
                uint size = br.ReadUInt32();
                if (type == "DATA") { dataStart = here + offset; dataSize = size; }
            }
            if (dataStart < 0) return 0;

            // Texture header (DATA): version, flags, reflectivity, width, height, depth, format, mips.
            fs.Position = dataStart;
            br.ReadUInt16(); br.ReadUInt16();
            for (int i = 0; i < 4; i++) br.ReadSingle();
            int width = br.ReadUInt16(), height = br.ReadUInt16(), depth = br.ReadUInt16();
            byte format = br.ReadByte();
            byte mips = br.ReadByte();
            if (format != FormatBc6h || width != height || mips != 1 || depth > 1 || width % 4 != 0) return 0;

            // The pixel data follows the resource's DATA block: one mip of 4x4 blocks, 16 bytes each.
            long pixels = dataStart + dataSize;
            long expected = (long)(width / 4) * (height / 4) * 16;
            if (fs.Length - pixels != expected) return 0; // compressed mips or extra slices: not a plain copy
            fs.Position = pixels;
            using (var o = File.Create(dst))
            {
                var buf = new byte[1 << 20];
                long left = expected;
                while (left > 0)
                {
                    int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
                    if (n <= 0) throw new EndOfStreamException("lightmap cut short");
                    o.Write(buf, 0, n);
                    left -= n;
                }
            }
            return width;
        }
    }
}
