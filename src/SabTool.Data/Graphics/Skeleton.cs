﻿using System;
using System.IO;
using System.Numerics;

using SharpGLTF.Transforms;

namespace SabTool.Data.Graphics
{
    using Utils.Extensions;

    public class Skeleton
    {
        #region Static
        public static Skeleton SingleBoneInstance { get; }

        static Skeleton()
        {
            SingleBoneInstance = new Skeleton
            {
                NumBones = 1
            };
        }
        #endregion

        public int NumBones { get; set; }
        public byte[] UnkData { get; set; }
        public Matrix4x4[] BasePoses { get; set; }
        public AffineTransform[] UnkBasePoses { get; set; }
        public short[] Indices { get; set; }
        public Bone[] Bones { get; set; }

        public bool Read(BinaryReader reader)
        {
            var someSkip = reader.ReadInt32();
            var int_4 = reader.ReadInt32();
            var int_8 = reader.ReadInt32();
            var int_C = reader.ReadInt32();
            var someSkip2 = reader.ReadInt32();
            var int_14 = reader.ReadInt32();
            var int_18 = reader.ReadInt32();
            var int_1C = reader.ReadInt32();
            var int_20 = reader.ReadInt32();
            var int_24 = reader.ReadInt32();

            reader.BaseStream.Position += 0x4;

            // Should this be above or under the unk bytes?
            reader.BaseStream.Position += someSkip;

            UnkData = reader.ReadBytes(NumBones);

            BasePoses = new Matrix4x4[NumBones];
            for (var i = 0; i < NumBones; ++i)
            {
                BasePoses[i] = reader.ReadMatrix4x4();
            }

            Bones = new Bone[NumBones];
            for (var i = 0; i < NumBones; ++i)
            {
                Bones[i] = new Bone();
                Bones[i].Read(reader);
                Bones[i].Index = (short)i;
            }

            UnkBasePoses = new AffineTransform[NumBones];
            for (var i = 0; i < NumBones; ++i)
                UnkBasePoses[i] = reader.ReadAffineTransform();

            Indices = new short[NumBones];
            for (var i = 0; i < NumBones; ++i)
                Indices[i] = reader.ReadInt16();

            reader.BaseStream.Position += someSkip2;

            // Pointer array without data to skip allocation
            reader.BaseStream.Position += 4 * NumBones;

            return true;
        }
    }
}
