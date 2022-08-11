﻿using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;

using Newtonsoft.Json;

namespace SabTool.Serializers.Megapacks
{
    using Data.Packs;
    using Json.Converters;
    using Utils;
    using Utils.Extensions;

    public static class FranceMapSerializer
    {
        public static FranceMap DeserializeRaw(Stream stream, FranceMap franceMap = null)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);

            var isDLC = true;

            if (franceMap == null)
            {
                franceMap = new FranceMap();

                isDLC = false;
            }

            if (!reader.CheckHeaderString("MAP6", reversed: true))
                throw new Exception("Invalid Map header found!");

            var name = reader.ReadStringWithMaxLength(256);

            var paletteCount = 0;

            if (isDLC)
            {
                franceMap.FieldDA88 = reader.ReadInt32();

                paletteCount = reader.ReadInt32();
            }
            else
            {
                paletteCount = franceMap.NumStreamBlocks = reader.ReadInt32();
                franceMap.FieldDA84 = reader.ReadInt32();

                var count = reader.ReadInt32();

                if (count > 0)
                {
                    var unk = reader.ReadInt32();

                    for (var i = 0; i < count; ++i)
                    {
                        var strLen = reader.ReadInt32();
                        var str = reader.ReadStringWithMaxLength(strLen + 1);
                        //Console.WriteLine($"Str: {str} (0x{Hash.FNV32string(str):X8})");

                        var len2 = reader.ReadInt32();
                        var data2 = reader.ReadBytes(len2);

                        var str2 = Encoding.UTF8.GetString(data2, 0, data2.Length - 1);
                        //Console.WriteLine($"Str2: {str2} (0x{Hash.FNV32string(str2):X8})");

                        var data3 = reader.ReadBytes(48);

                        var len4 = reader.ReadInt32();
                        var data4 = reader.ReadBytes(len4);

                        // TODO
                    }
                }

                for (var i = 0; i < 3; ++i)
                {
                    franceMap.Extents[i] = new Vector3[2];
                    franceMap.Extents[i][0].X = reader.ReadSingle();
                    franceMap.Extents[i][0].Y = reader.ReadSingle();
                    franceMap.Extents[i][0].Z = reader.ReadSingle();

                    franceMap.Extents[i][1].X = reader.ReadSingle();
                    franceMap.Extents[i][1].Y = reader.ReadSingle();
                    franceMap.Extents[i][1].Z = reader.ReadSingle();
                }

                for (var i = 0; i < 3; ++i)
                {
                    franceMap.GridCountX[i] = reader.ReadInt16();
                    franceMap.GridCountZ[i] = reader.ReadInt16();

                    franceMap.GridCountX[i] = (int)((franceMap.Extents[i][1].X - franceMap.Extents[i][0].X) / FranceMap.GridLimits[i]);
                    franceMap.GridCountZ[i] = (int)((franceMap.Extents[i][1].Z - franceMap.Extents[i][0].Z) / FranceMap.GridLimits[i]);
                }
            }

            for (var i = 0; i < paletteCount; ++i)
            {
                var paletteBlock = PaletteBlockSerializer.DeserializeRaw(stream);

                franceMap.Palettes[franceMap.CalculateGrid(paletteBlock)] = paletteBlock;
            }

            var interiorCount = reader.ReadInt32();

            if (franceMap.Interiors == null)
                franceMap.Interiors = new(interiorCount);

            for (var i = 0; i < interiorCount; ++i)
                franceMap.Interiors.Add(StreamBlockSerializer.DeserializeFromMapRaw(stream));

            var count3 = reader.ReadInt32();

            if (franceMap.CinematicBlocks == null)
                franceMap.CinematicBlocks = new(count3);

            for (var i = 0; i < count3; ++i)
                franceMap.CinematicBlocks.Add(StreamBlockSerializer.DeserializeFromMapRaw(stream));

            return franceMap;
        }

        public static FranceMap DeserializeJSON(Stream stream)
        {
            return null;
        }

        public static void SerializeJSON(FranceMap franceMap, Stream stream)
        {
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(JsonConvert.SerializeObject(franceMap, Formatting.Indented, new CrcConverter()));
        }
    }
}
