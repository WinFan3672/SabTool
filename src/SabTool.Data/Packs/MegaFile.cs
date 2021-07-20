﻿using System;
using System.Collections.Generic;
using System.IO;

namespace SabTool.Data.Packs
{
    using Utils.Extensions;

    public class MegaFile
    {
        public Dictionary<int, FileEntry> FileEntries { get; set; } = new Dictionary<int, FileEntry>();
        public uint FileCount { get; set; }
        public int[] Array3D8 { get; set; }

        public bool ReadHeader(BinaryReader br)
        {
            if (!br.CheckHeaderString("MP00", reversed: true))
                return false;

            FileCount = br.ReadUInt32();

            for (var i = 0; i < FileCount; ++i)
            {
                var entry = new FileEntry(br);

                FileEntries.Add(entry.Crc, entry);

                var crc = entry.Crc;
                var crc2 = entry.Crc2;

                /*var block = StreamingManager.GetStreamBlockByCRC(crc, out string source);
                if (block != null)
                    Console.WriteLine($"Adding entry: 0x{entry.Crc:X8} => {block.FileName,-45} through {source}");

                block = StreamingManager.GetStreamBlockByCRC(crc2, out string source2);
                if (block != null)
                    Console.WriteLine($"Adding entry: 0x{crc:X8} => 0x{crc2:X8} => {block.FileName,-45} through {source2}");
                else
                    Console.WriteLine($"Adding unknown entry: 0x{crc:X8} => 0x{crc2:X8}");*/
            }

            Array3D8 = new int[2 * FileCount];

            for (var i = 0; i < FileCount; ++i)
            {
                Array3D8[2 * i] = br.ReadInt32();
                Array3D8[2 * i + 1] = br.ReadInt32();

                var entryCrc = Array3D8[2 * i];
                var crc2 = Array3D8[2 * i + 1];

                if (!FileEntries.ContainsKey(entryCrc))
                {
                    Console.WriteLine($"ERROR: 0x{entryCrc:X8} => 0x{crc2:X8} is not a valid fileentry!");
                    continue;
                }

                var entry = FileEntries[entryCrc];
                if (entry.Crc == entryCrc && entry.Crc2 == crc2)
                    continue;

                /*string source;
                var block = StreamingManager.GetStreamBlockByCRC(entryCrc, out source);
                if (block != null)
                    Console.WriteLine($"Map entry: 0x{entryCrc:X8} => {block.FileName,-45} through {source}");

                block = StreamingManager.GetStreamBlockByCRC(crc2, out source);
                if (block != null)
                    Console.WriteLine($"Map entry: 0x{entryCrc:X8} 0x{crc2:X8} => {block.FileName,-45} through {source}");
                else
                    Console.WriteLine($"Map unknown entry: 0x{entryCrc:X8} => 0x{crc2:X8}");*/
            }

            return true;
        }

        public void ReadStreamBlocks()
        {

        }

        public void Export()
        {

        }
    }

    public enum FileEntryType
    {
        Mesh = 0,
        Texture = 1,
        Physics = 2,
        PathGraph = 3,
        AIFence = 4,
        Unk5 = 5,
        SoundBank = 6,
        FlashMovie = 7,
        WSD = 8
    }

    public enum FileReadMethod : uint
    {
        Mesh = 0xFE5E3A56u,
        Texture = 0xA40D777Du,
        Physics = 0x4445EA18u,
        PathGraph = 0xE1087B27u,
        AIFence = 0xD3098461u,
        SoundBank = 0xDD62BA1Au,
        FlashMovie = 0xB5D2FE96u,
        WSD = 0x9AB5A351u,
        Unk5 = 0x00000001u,
        Typ2 = 0x00000002u, // SBLA (not yet finished)
        Typ3 = 0x00000003u  // SBLA (finished)
    }

    public class FileEntry
    {
        public int Crc { get; set; }
        public int Crc2 { get; set; }
        public uint Size { get; set; }
        public long Offset { get; set; }

        public FileEntry(BinaryReader br)
        {
            Crc = br.ReadInt32();
            Crc2 = br.ReadInt32();
            Size = br.ReadUInt32();
            Offset = br.ReadInt64();
        }
    }
}