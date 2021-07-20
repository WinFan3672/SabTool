﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SabTool.Data.GameTemplates
{
    using Blueprint;
    using Lua;
    using Utils;

    public class GameTemplatesDump : GameTemplatesBase
    {
        private const int NamePad = -30;

        private TextWriter Output { get; }

        static GameTemplatesDump()
        {
            var toAdd = new HashSet<string>();

            while (true)
            {
                var didAnything = false;

                foreach (var entry in Parents)
                {
                    entry.Value.Add("Common");
                    entry.Value.Add("Unknown");

                    foreach (var parent in entry.Value)
                    {
                        if (!Parents.ContainsKey(parent))
                            continue;

                        foreach (var parentVal in Parents[parent])
                        {
                            toAdd.Add(parentVal);
                        }
                    }

                    var startCount = entry.Value.Count;

                    foreach (var newEntry in toAdd)
                    {
                        entry.Value.Add(newEntry);
                    }

                    toAdd.Clear();

                    didAnything = didAnything || startCount != entry.Value.Count;
                }

                if (!didAnything)
                    break;
            }
        }

        public GameTemplatesDump(TextWriter output = null)
        {
            Output = output;

            if (Output == null)
            {
                Output = Console.Out;
            }
        }

        protected override void ReadBlueprint(string type, string name, BluaReader reader)
        {
            Output.WriteLine($"Blueprint({type}, {name}):");

            HashSet<string> parents = null;

            foreach (var entry in Parents)
            {
                if (entry.Key.ToLowerInvariant() == type.ToLowerInvariant())
                {
                    parents = entry.Value;
                    break;
                }
            }

            if (parents == null)
            {
                Output.WriteLine("Unknown blueprint type!");
            }

            while (reader.Offset < reader.Size)
            {
                var crc = reader.ReadUInt();
                var size = reader.ReadInt();

                // Store the starting position
                var startOff = reader.Offset;

                // Read the property
                ReadProperty(reader, type, crc, size, parents);

                // Assign the valid offset to the reader, even if the ReadProperty read too little or too much
                reader.Offset = startOff + size;
            }

            Output.WriteLine();
        }

        private void ReadProperty(BluaReader reader, string type, uint crc, int size, HashSet<string> parents)
        {
            var typeCrc = Hash.FNV32string(type);

            var name = Hash.HashToString(crc);

            TypeEntry entry;

            if (!PropertyTypes.TryGetValue(new(typeCrc, crc), out entry) && parents != null)
            {
                foreach (var parent in parents)
                {
                    if (PropertyTypes.TryGetValue(new(Hash.FNV32string(parent), crc), out entry))
                    {
                        type = parent;
                        break;
                    }
                }
            }

            if (entry != null)
            {
                var propName = entry.OverrideName;

                if (!string.IsNullOrEmpty(name))
                {
                    if (string.IsNullOrEmpty(propName))
                        propName = name;
                    else
                        propName = $"{propName} ({name})";
                }

                if (string.IsNullOrEmpty(propName))
                    propName = "UNKNOWN";

                Output.WriteLine($"[0x{crc:X8}][{type,NamePad}][{propName,NamePad}]: {ReadPropertyValue(reader, entry.Type, size)}");
            }
            else
            {
                if (string.IsNullOrEmpty(name))
                    name = "UNKNOWN";

                var bytes = reader.ReadBytes(size);

                Output.WriteLine($"[0x{crc:X8}][{type,NamePad}][{name,NamePad}]: {FormatEmptyType(bytes)}");
            }
        }

        private static string FormatEmptyType(byte[] bytes)
        {
            var bytesStr = BitConverter.ToString(bytes).Replace('-', ' ');

            string guessStr = "";
            if (bytes.Length >= 4)
            {
                var intStr = bytes.Length >= 4 ? BitConverter.ToInt32(bytes, 0).ToString() : "";
                var floatStr = bytes.Length >= 4 ? BitConverter.ToSingle(bytes, 0).ToString("0.00") : "";
                var stringVal = Encoding.UTF8.GetString(bytes);
                var crcVal = bytes.Length >= 4 ? new Crc(BitConverter.ToUInt32(bytes, 0)).ToString() : "";

                for (var i = 0; i < bytes.Length; ++i)
                {
                    // Don't check nulltermination
                    if (i == bytes.Length - 1 && bytes[i] == 0)
                        break;

                    // Check every character to be valid
                    if (bytes[i] < 0x20 || bytes[i] > 0x7E)
                    {
                        stringVal = null;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(stringVal))
                {
                    guessStr = $" (I: {intStr,-15} | F: {floatStr,-15} | Crc: {crcVal})";
                }
                else
                {
                    if (stringVal[^1] == '\0')
                        stringVal = stringVal[0..^1];

                    guessStr = $" (I: {intStr,-15} | F: {floatStr,-15} | S: {stringVal,-15} | Crc: {crcVal})";
                }
            }

            return $"{bytesStr,-30}{guessStr}";
        }

        private static object ReadPropertyValue(BluaReader reader, Type type, int size)
        {
            if (type == null)
                return FormatEmptyType(reader.ReadBytes(size));

            return type.Name switch
            {
                "Int32" => reader.ReadInt(),
                "UInt32" => reader.ReadUInt(),
                "Boolean" => reader.ReadBool() != 0,
                "Byte" => reader.ReadByte(),
                "String" => reader.ReadString(size),
                "Crc" => new Crc(reader.ReadUInt()),
                "LuaParam" => new LuaParam(reader),
                "Single" => reader.ReadFloat().ToString("0.00"),
                "Double" => reader.ReadDouble().ToString("0.00"),
                "Byte[]" => BitConverter.ToString(reader.ReadBytes(size)).Replace('-', ' '),
                "FloatVector2" => new FloatVector2(reader.ReadFloat(), reader.ReadFloat()),
                "FloatVector3" => new FloatVector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()),
                "FloatVector4" => new FloatVector4(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()),
                "FloatVector3_2" => new FloatVector3_2(new FloatVector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat()), new FloatVector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat())),
                "Color" => new Color(reader.ReadInt()),
                "UnkBirdType" => new UnkBirdType(reader.ReadString(reader.ReadInt())),
                _ => null
            };
        }

        record TypeCrcKey(uint TypeCrc, uint Crc);
        record TypeEntry(string OverrideName, Type Type);
        record FloatVector2(float Val1, float Val2)
        {
            public override string ToString()
            {
                return $"({Val1}, {Val2})";
            }
        }
        record FloatVector3(float Val1, float Val2, float Val3)
        {
            public override string ToString()
            {
                return $"({Val1}, {Val2}, {Val3})";
            }
        }
        record FloatVector3_2(FloatVector3 Vec1, FloatVector3 Vec2)
        {
            public override string ToString()
            {
                return $"({Vec1}, {Vec2})";
            }
        }
        record FloatVector4(float Val1, float Val2, float Val3, float Val4)
        {
            public override string ToString()
            {
                return $"({Val1}, {Val2}, {Val3}, {Val4})";
            }
        }
        record Color(int Value)
        {
            public override string ToString()
            {
                return $"(R: {Value & 0xFF:X2}, G: {(Value >> 8) & 0xFF:X2}, B: {(Value >> 16) & 0xFF:X2}, A: {(Value >> 24) & 0xFF:X2})";
            }
        }
        record UnkBirdType(string Str)
        {
            public override string ToString()
            {
                return $"({Str})";
            }
        }

        private static Dictionary<string, HashSet<string>> Parents { get; } = new()
        {
            { "Weapon", new() { "Item", "Damageable", "Audible" } },
            { "Searcher", new() { "Item", "Damageable", "Audible" } },
            { "MeleeWeapon", new() { "Item" } },
            { "Ammo", new() { "Item" } },
            { "bullet", new() { "Ordnance" } },
            { "FlameOrdnance", new() { "Ordnance" } },
            { "PhysicalOrdnance", new() { "Ordnance", "Targetable", "Audible" } },
            { "Rocket", new() { "Ordnance", "Audible" } },
            { "HumanPhysics", new() { } },
            { "Player", new() { "Human", "CameraSet" } },
            { "Car", new() { "Vehicle" } },
            { "Truck", new() { "Car" } },
            { "APC", new() { "Car" } },
            { "Tank", new() { "Vehicle" } },
            { "Human", new() { "Aimer", "Damageable", "HumanPhysics", "Targetable", "Controllable", "Audible" } },
            { "Spore", new() { "Controllable" } },
            { "VehicleCollision", new() { } },
            { "PlayerCollision", new() { } },
            { "Targetable", new() { } },
            { "Prop", new() { "ModelRenderable", "Damageable", "LightAttachement", "LightHaloAttachment", "AttrPtAttachable", "Audible" } },
            { "TrainList", new() { } },
            { "Train", new() { } },
            { "Foliage", new() { } },
            { "AIAttractionPt", new() { "Controllable", "Trappable" } },
            { "DamageSphere", new() { "DamageRegion" } },
            { "Explosion", new() { "Audible" } },
            { "AIController", new() { } },
            { "ScriptController", new() { "Controllable" } },
            { "AISpawner", new() { "Controllable" } },
            { "AICombatParams", new() { } },
            { "AICrowdBlocker", new() { } },
            { "GlobalHumanParams", new() { } },
            { "Melee", new() { } },
            { "ParticleEffect", new() { } },
            { "RadialBlur", new() { } },
            { "Sound", new() { "Audible" } },
            { "Turret", new() { "Prop", "GunnerSeat" } },
            { "SearchTurret", new() { "Prop", "SearcherSeat" } },
            { "TrainCarriage", new() { "Vehicle" } },
            { "TrainEngine", new() { "TrainCarriage" } },
            { "TrainItem", new() { "Prop" } },
            { "SlowMotionCamera", new() { } },
            { "ElasticTransition", new() { } },
            { "AnimatedTransition", new() { "ElasticTransition" } },
            { "GroupTransition", new() { "ElasticTransition" } },
            { "ScopeTransition", new() { "ElasticTransition" } },
            { "CameraSettings", new() { } },
            { "GroupCameraSettings", new() { "CameraSettings" } },
            { "CameraSettingsMisc", new() { } },
            { "ToneMapping", new() { } },
            { "LightSettings", new() { "LightAttachement", "LightHaloAttachment", "sub_7FB4D0" } },
            { "LightHalo", new() { } },
            { "LightVolume", new() { } },
            { "ClothObject", new() { "ModelRenderable" } },
            { "ClothForce", new() { } },
            { "WillToFight", new() { } },
            { "WillToFightNode", new() { } },
            { "HealthEffectFilter", new() { } },
            { "ParticleEffectSpawner", new() { "Audible" } },
            { "ButtonPrompt", new() { } },
            { "Item", new() { "LightAttachement", "LightHaloAttachment" } },
            { "FxBoneStateList", new() { } },
            { "FxHumanHead", new() { } },
            { "FxHumanBodyPart", new() { } },
            { "FxHumanBodySetup", new() { } },
            { "FaceExpression", new() { } },
            { "HumanBodyPart", new() { } },
            { "HumanBodySetup", new() { } },
            { "HumanSkeletonScale", new() { } },
            { "AnimatedObject", new() { "BonePair", "AttrPtAttachable", "Damageable", "Audible", "Trappable" } },
            { "RandomObj", new() { } },
            { "BridgeController", new() { } },
            { "Ricochet", new() { } },
            { "AIRoad", new() { } },
            { "Highlight", new() { } },
            { "MiniGame", new() { } },
            { "SabotageTarget", new() { } },
            { "BigMap", new() { } },
            { "FlashMovie", new() { } },
            { "ItemCache", new() { "ModelRenderable" } },
            { "VirVehicleWheel", new() { } },
            { "VirVehicleTransmission", new() { } },
            { "VirVehicleEngine", new() { } },
            { "VirVehicleChassis", new() { } },
            { "VirVehicleSetup", new() { } },
            { "VehicleWheelFX", new() { } },
            { "Difficulty", new() { } },
            { "Shop", new() { } },
            { "Perks", new() { } },
            { "PerkFactors", new() { } },
            { "PhysicsParticleSet", new() { "ModelRenderable" } },
            { "PhysicsParticle", new() { "ModelRenderable" } },
            { "Decal", new() { } },
            { "DetailObject", new() { } },
            { "CivilianProp", new() { } },
            { "Bird", new() { "Audible", "BirdSpawner" } },
            { "Escalation", new() { } },
            { "EscHWTF", new() { } },
            { "AIChatterSet", new() { } },
            { "AIChatter", new() { } },
            { "SeatAnimations", new() { } },
            { "SeatAnimationsPassenger", new() { } },
            { "SeatAnimationsGunner", new() { } },
            { "SeatAnimationsSearcher", new() { } },
            { "SeatAnimationsDriver", new() { } },
            { "FoliageFx", new() { } },
            { "WaterController", new() { } },
            { "WaterTexture", new() { } },
            { "LeafSpawner", new() { } },
            { "VerletBoneObject", new() { } },
            { "WaterParticleFx", new() { } },
            { "Cinematics", new() { } },
            { "Rumble", new() { } },
            { "ImageFolder", new() { } },
            { "CreditName", new() { } },
            { "WaterFlow", new() { } },

            // Shared
            { "Damageable", new() { "DamageableRoot" } },
            { "DamageableRoot", new() { "DamageablePart" } },
            { "Vehicle", new() { "ModelRenderable", "Damageable", "LightAttachement", "LightHaloAttachment", "Audible", "PhysicsVehicle", "Trappable", "GunnerSeat", "AttrPtAttachable", "TweakableColors", "Seat", "SearcherSeat" } },
            { "PhysicsVehicle", new() { "PhysicsVehicleWheel" } },
            { "GunnerSeat", new() { "SeatWithMount" } },
            { "SearcherSeat", new() { "SeatWithMount" } },
            { "SeatWithMount", new() { "Seat", "Aimer" } },
            { "Seat", new() { "Controllable" } },
        };

        private static Dictionary<TypeCrcKey, TypeEntry> PropertyTypes { get; } = new()
        {
            // AIAttractionPt
            { new(0x92473A24, 0x7EF2B668), new(null, typeof(bool)) },
            { new(0x92473A24, 0x7E4E77E0), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x7ED98262), new(null, typeof(float)) },
            { new(0x92473A24, 0xE773FC1F), new(null, typeof(bool)) },
            { new(0x92473A24, 0xE29D36B0), new(null, typeof(int)) },
            { new(0x92473A24, 0xE66CED59), new(null, typeof(bool)) },
            { new(0x92473A24, 0xE1291EAE), new(null, typeof(float)) },
            { new(0x92473A24, 0xD411C23C), new(null, typeof(bool)) },
            { new(0x92473A24, 0xDE634E2F), new(null, typeof(bool)) },
            { new(0x92473A24, 0xDF0B2054), new(null, typeof(float)) },
            { new(0x92473A24, 0xCF88DE36), new(null, typeof(float)) },
            { new(0x92473A24, 0xD235DA20), new(null, typeof(int)) },
            { new(0x92473A24, 0xCE1A4A79), new(null, typeof(int)) },
            { new(0x92473A24, 0xC6E0F627), new(null, typeof(double)) },
            { new(0x92473A24, 0xC8DDD177), new(null, typeof(float)) },
            { new(0x92473A24, 0xCBD8745E), new(null, typeof(float)) },
            { new(0x92473A24, 0xD27568AD), new(null, typeof(Crc)) },
            { new(0x92473A24, 0xF01F08A8), new(null, typeof(int)) },
            { new(0x92473A24, 0xEA73DB2F), new(null, typeof(bool)) },
            { new(0x92473A24, 0xECF2E7AE), new(null, typeof(float)) },
            { new(0x92473A24, 0xE9CACFF7), new(null, typeof(bool)) },
            { new(0x92473A24, 0xE7B1D609), new(null, typeof(Crc)) },
            { new(0x92473A24, 0xE94E8608), new(null, typeof(int)) },
            { new(0x92473A24, 0xE9A5BC9C), new(null, typeof(bool)) },
            { new(0x92473A24, 0xFB14F042), new(null, typeof(float)) },
            { new(0x92473A24, 0xFB31F1EF), new(null, typeof(int)) },
            { new(0x92473A24, 0xF8AFE581), new(null, typeof(int)) },
            { new(0x92473A24, 0xF0C7DA5F), new(null, typeof(string)) },
            { new(0x92473A24, 0xF10C031B), new(null, typeof(int)) },
            { new(0x92473A24, 0xF49F0182), new(null, typeof(float)) },
            { new(0x92473A24, 0xC54E8CAE), new(null, typeof(bool)) },
            { new(0x92473A24, 0x9EDB6BD9), new(null, typeof(int)) },
            { new(0x92473A24, 0x99A54CBF), new(null, typeof(bool)) },
            { new(0x92473A24, 0x9AAC5788), new(null, typeof(float)) },
            { new(0x92473A24, 0x982589A0), new(null, typeof(byte[])) },
            { new(0x92473A24, 0x923BA76B), new(null, typeof(byte[])) },
            { new(0x92473A24, 0x92D0A9BA), new(null, typeof(float)) },
            { new(0x92473A24, 0x97AC8632), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x8FD2512F), new(null, typeof(bool)) },
            { new(0x92473A24, 0x8C8D6435), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x8D89FAD9), new(null, typeof(bool)) },
            { new(0x92473A24, 0x8DB13A88), new(null, typeof(int)) },
            { new(0x92473A24, 0x888505C3), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x07F79C5D), new(null, typeof(float)) },
            { new(0x92473A24, 0x84C77E96), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x84D70C5E), new(null, typeof(int)) },
            { new(0x92473A24, 0xB3AF2A59), new(null, typeof(bool)) },
            { new(0x92473A24, 0xA8DB360C), new(null, typeof(int)) },
            { new(0x92473A24, 0xAEEA42F8), new(null, typeof(Crc)) },
            { new(0x92473A24, 0xB2E588D7), new(null, typeof(double)) },
            { new(0x92473A24, 0xB40462AD), new(null, typeof(int)) },
            { new(0x92473A24, 0xC1D0622B), new(null, typeof(float)) },
            { new(0x92473A24, 0xA8079DD8), new(null, typeof(double)) },
            { new(0x92473A24, 0xA5A71B8A), new(null, typeof(float)) },
            { new(0x92473A24, 0xA7E0D198), new(null, typeof(Crc)) },
            { new(0x92473A24, 0xA3FBC25C), new(null, typeof(bool)) },
            { new(0x92473A24, 0x9FDC0AE6), new(null, typeof(int)) },
            { new(0x92473A24, 0xA0A39AA4), new(null, typeof(float)) },
            { new(0x92473A24, 0xA2FF7739), new(null, typeof(float)) },
            { new(0x92473A24, 0x7E1D5456), new(null, typeof(bool)) },
            { new(0x92473A24, 0x658D48E2), new(null, typeof(int)) },
            { new(0x92473A24, 0x506DE042), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x4785F2A3), new(null, typeof(int)) },
            { new(0x92473A24, 0x4A67BCC0), new(null, typeof(int)) },
            { new(0x92473A24, 0x4D44EFE0), new(null, typeof(int)) },
            { new(0x92473A24, 0x47751B18), new(null, typeof(float)) },
            { new(0x92473A24, 0x4172D6FB), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x41B4ACE8), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x460E7F77), new(null, typeof(bool)) },
            { new(0x92473A24, 0x55D89223), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x507A477F), new(null, typeof(bool)) },
            { new(0x92473A24, 0x5253FC93), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x530D6414), new(null, typeof(bool)) },
            { new(0x92473A24, 0x5E58EAC8), new(null, typeof(int)) },
            { new(0x92473A24, 0x5E77F41F), new(null, typeof(float)) },
            { new(0x92473A24, 0x733ED3B2), new(null, typeof(int)) },
            { new(0x92473A24, 0x6C6A0DB8), new(null, typeof(int)) },
            { new(0x92473A24, 0x6E15FFFF), new(null, typeof(float)) },
            { new(0x92473A24, 0x726F6DCC), new(null, typeof(bool)) },
            { new(0x92473A24, 0x6BE83213), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x67277C2C), new(null, typeof(bool)) },
            { new(0x92473A24, 0x6935C0FD), new(null, typeof(float)) },
            { new(0x92473A24, 0x6A099303), new(null, typeof(float)) },
            { new(0x92473A24, 0x7C0D1F8E), new(null, typeof(bool)) },
            { new(0x92473A24, 0x7CD0DF87), new(null, typeof(string)) },
            { new(0x92473A24, 0x7A3E99B4), new(null, typeof(int)) },
            { new(0x92473A24, 0x7794DACB), new(null, typeof(bool)) },
            { new(0x92473A24, 0x787A47A5), new(null, typeof(float)) },
            { new(0x92473A24, 0x7A157568), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x4070B35E), new(null, typeof(bool)) },
            { new(0x92473A24, 0x239B1E1B), new(null, typeof(bool)) },
            { new(0x92473A24, 0x0F12604C), new(null, typeof(float)) },
            { new(0x92473A24, 0x06273164), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x07AD6EBF), new(null, typeof(float)) },
            { new(0x92473A24, 0x09E38146), new(null, typeof(float)) },
            { new(0x92473A24, 0x054A8FB1), new(null, typeof(float)) },
            { new(0x92473A24, 0x019EF97E), new(null, typeof(bool)) },
            { new(0x92473A24, 0x01D505A2), new(null, typeof(bool)) },
            { new(0x92473A24, 0x04FBB1EE), new(null, typeof(string)) },
            { new(0x92473A24, 0x1DE59C2F), new(null, typeof(bool)) },
            { new(0x92473A24, 0x16930AFE), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x192A3633), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x1B78F12E), new(null, typeof(int)) },
            { new(0x92473A24, 0x1DE5C824), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x23518B03), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x3DCC7355), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x3DFE6182), new(null, typeof(int)) },
            { new(0x92473A24, 0x397B6A20), new(null, typeof(bool)) },
            { new(0x92473A24, 0x331D2AA8), new(null, typeof(bool)) },
            { new(0x92473A24, 0x3566AE45), new(null, typeof(float)) },
            { new(0x92473A24, 0x365FB660), new(null, typeof(bool)) },
            { new(0x92473A24, 0x32D97474), new(null, typeof(float)) },
            { new(0x92473A24, 0x2D5B659B), new(null, typeof(bool)) },
            { new(0x92473A24, 0x2ED2EC21), new(null, typeof(bool)) },
            { new(0x92473A24, 0x3217CDF0), new(null, typeof(float)) },
            { new(0x92473A24, 0x2C652AF7), new(null, typeof(Crc)) },
            { new(0x92473A24, 0x2523B4EE), new(null, typeof(bool)) },
            { new(0x92473A24, 0x29EC9F57), new(null, typeof(bool)) },
            { new(0x92473A24, 0x2A4802C1), new(null, typeof(bool)) },

            // AIChatter
            { new(0x669746CA, 0xCBE8ED58), new(null, typeof(float)) },
            { new(0x669746CA, 0xBDB6564E), new(null, typeof(float)) },
            { new(0x669746CA, 0xC187EFD3), new(null, typeof(bool)) },
            { new(0x669746CA, 0xDC83F68F), new(null, typeof(FloatVector2)) },
            { new(0x669746CA, 0x87519019), new(null, typeof(int)) },
            { new(0x669746CA, 0x70C454D5), new(null, typeof(FloatVector2)) },
            { new(0x669746CA, 0x7213E378), new(null, typeof(int)) },
            { new(0x669746CA, 0x2C504344), new(null, typeof(Crc)) },
            { new(0x669746CA, 0x076B2507), new(null, typeof(FloatVector2)) },
            { new(0x669746CA, 0x22CC5267), new(null, typeof(bool)) },

            // AIChatterSet
            { new(0xD414DC12, 0xDF9C6FEF), new(null, typeof(Crc)) },
            { new(0xD414DC12, 0x8D9F39B1), new(null, typeof(Crc)) },
            { new(0xD414DC12, 0xBBA6EBA6), new(null, typeof(Crc)) },
            { new(0xD414DC12, 0xD5638D90), new(null, typeof(int)) },

            // AICombatParams
            { new(0xC43B697D, 0x5EA66EEA), new(null, typeof(float)) },
            { new(0xC43B697D, 0x54D3213D), new(null, typeof(FloatVector2)) },
            { new(0xC43B697D, 0x4EBED1B7), new(null, typeof(float)) },
            { new(0xC43B697D, 0x468668D7), new(null, typeof(float)) },
            { new(0xC43B697D, 0x46D82BEA), new(null, typeof(float)) },
            { new(0xC43B697D, 0x3CD07B0B), new(null, typeof(float)) },
            { new(0xC43B697D, 0x18E0180B), new(null, typeof(float)) },
            { new(0xC43B697D, 0x362FC205), new(null, typeof(float)) },
            { new(0xC43B697D, 0x16B61A99), new(null, typeof(bool)) },
            { new(0xC43B697D, 0x0EC2F55D), new(null, typeof(Crc)) },
            { new(0xC43B697D, 0x16794F6C), new(null, typeof(byte)) },
            { new(0xC43B697D, 0xBF3FBA12), new(null, typeof(bool)) },
            { new(0xC43B697D, 0x8BB6AD21), new(null, typeof(float)) },
            { new(0xC43B697D, 0x91DEEDFA), new(null, typeof(float)) },
            { new(0xC43B697D, 0x96854078), new(null, typeof(float)) },
            { new(0xC43B697D, 0x734F2BA0), new(null, typeof(bool)) },
            { new(0xC43B697D, 0x72E6C7ED), new(null, typeof(float)) },
            { new(0xC43B697D, 0xE9869138), new(null, typeof(float)) },
            { new(0xC43B697D, 0xC76AF905), new(null, typeof(float)) },
            { new(0xC43B697D, 0xE47C51B0), new(null, typeof(float)) },
            { new(0xC43B697D, 0xFA03EB28), new(null, typeof(float)) },

            // AIController
            { new(0x001BAB2B, 0xF2F462B6), new(null, typeof(float)) },
            { new(0x001BAB2B, 0xE0B01D9F), new(null, typeof(string)) },
            { new(0x001BAB2B, 0x9627B5A7), new(null, typeof(byte)) },
            { new(0x001BAB2B, 0x9793CEAA), new(null, typeof(float)) },
            { new(0x001BAB2B, 0x87C3B66F), new(null, typeof(float)) },
            { new(0x001BAB2B, 0x49E6BFF9), new(null, typeof(float)) },
            { new(0x001BAB2B, 0x7E4E77E0), new(null, typeof(Crc)) },
            { new(0x001BAB2B, 0x316CC898), new(null, typeof(float)) },
            { new(0x001BAB2B, 0x03C5822E), new(null, typeof(float)) },
            { new(0x001BAB2B, 0x26510ECF), new(null, typeof(float)) },

            // AICrowdBlocker
            { new(0x4751C524, 0x888F7A10), new(null, typeof(bool)) },

            // Aimer
            { new(0x1F18A531, 0x6DA3DB23), new(null, typeof(float)) },
            { new(0x1F18A531, 0x42BC590B), new(null, typeof(float)) },
            { new(0x1F18A531, 0x576A2A93), new(null, typeof(Crc)) },
            { new(0x1F18A531, 0x63EC5E50), new(null, typeof(float)) },
            { new(0x1F18A531, 0x3258D692), new(null, typeof(float)) },
            { new(0x1F18A531, 0x1379EBE2), new(null, typeof(float)) },
            { new(0x1F18A531, 0x28506E5C), new(null, typeof(float)) },
            { new(0x1F18A531, 0x30DF4F04), new(null, typeof(FloatVector3)) },
            { new(0x1F18A531, 0xF4CFC0E9), new(null, typeof(FloatVector2)) },
            { new(0x1F18A531, 0xAC3A2DAB), new(null, typeof(Crc)) },
            { new(0x1F18A531, 0xDD4E3954), new(null, typeof(Crc)) },
            { new(0x1F18A531, 0xE88FD118), new(null, typeof(float)) },
            { new(0x1F18A531, 0xF8640116), new(null, typeof(FloatVector2)) },
            { new(0x1F18A531, 0xFC971D7F), new(null, typeof(float)) },

            // AIRoad
            { new(0x2E0750D5, 0xCCC7E339), new(null, typeof(Crc)) },
            { new(0x2E0750D5, 0xEA927AFE), new(null, typeof(int)) },
            { new(0x2E0750D5, 0xCAD661E0), new(null, typeof(Crc)) },
            { new(0x2E0750D5, 0xA1C6D3A0), new(null, typeof(bool)) },
            { new(0x2E0750D5, 0xB1796D79), new(null, typeof(bool)) },
            { new(0x2E0750D5, 0x921E50FF), new(null, typeof(Crc)) },
            { new(0x2E0750D5, 0x41F557B1), new(null, typeof(float)) },
            { new(0x2E0750D5, 0x0181869C), new(null, typeof(bool)) },
            { new(0x2E0750D5, 0x0DD6CDB2), new(null, typeof(float)) },
            { new(0x2E0750D5, 0x2B8D1FDF), new(null, typeof(Crc)) },
            { new(0x2E0750D5, 0x5B8F052C), new(null, typeof(Crc)) },
            { new(0x2E0750D5, 0x830107DA), new(null, typeof(int)) },

            // AISpawner
            { new(0xAD3EFBE5, 0x6D0C6486), new(null, typeof(int)) },
            { new(0xAD3EFBE5, 0x61DEE14D), new(null, typeof(bool)) },
            { new(0xAD3EFBE5, 0x5E58EAC8), new(null, typeof(Crc)) },
            { new(0xAD3EFBE5, 0x2C40A12B), new(null, typeof(bool)) },
            { new(0xAD3EFBE5, 0x43B9013B), new(null, typeof(Crc)) },
            { new(0xAD3EFBE5, 0x7E1F64C8), new(null, typeof(float)) },
            { new(0xAD3EFBE5, 0xA90ECBBB), new(null, typeof(int)) },
            { new(0xAD3EFBE5, 0xC3496538), new(null, typeof(bool)) },

            // Ammo
            { new(0xBA16485F, 0x6596A232), new(null, typeof(int)) },
            { new(0xBA16485F, 0x0AA3E5D9), new(null, typeof(int)) },
            { new(0xBA16485F, 0x1CC87777), new(null, typeof(Crc)) },
            { new(0xBA16485F, 0x524FE4FC), new(null, typeof(int)) },
            { new(0xBA16485F, 0xBF4C7045), new(null, typeof(int)) },
            { new(0xBA16485F, 0xC84E7BEF), new(null, typeof(int)) },
            { new(0xBA16485F, 0xDE11CA90), new(null, typeof(int)) },
            { new(0xBA16485F, 0x71798A24), new(null, typeof(Crc)) },

            // AnimatedObject
            { new(0x3F615DD3, 0x6BEC5CB4), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0x60DED0ED), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x5C6BB173), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0x5AAB9E8A), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0x5B724250), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0x56CD780E), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x504894BF), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x5081076D), new(null, typeof(FloatVector3)) },
            { new(0x3F615DD3, 0x4077884B), new(null, typeof(FloatVector3)) },
            { new(0x3F615DD3, 0x3E495387), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0x3F3514B7), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x37308AF1), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x309589E8), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x2CD9F28F), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x2CE48D05), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0x34BF9491), new(null, typeof(FloatVector3)) },
            { new(0x3F615DD3, 0x36DC5038), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x2B9BF786), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0x24D568C9), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x2511CC68), new(null, typeof(FloatVector3)) },
            { new(0x3F615DD3, 0x1EE86C0E), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0x0AD30144), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x17D0A018), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0xC0431B07), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0xB77F637A), new(null, typeof(float)) },
            { new(0x3F615DD3, 0xBD2D8C4C), new(null, typeof(float)) },
            { new(0x3F615DD3, 0xB5290286), new(null, typeof(float)) },
            { new(0x3F615DD3, 0xAEFF60B8), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0xAF87143A), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0xACF16FBF), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0x92D3DD4C), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0x6D36B289), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0x82CDADFE), new(null, typeof(float)) },
            { new(0x3F615DD3, 0x89FFDADE), new(null, typeof(float)) },
            { new(0x3F615DD3, 0xA42DAB74), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0xAC2B9A92), new(null, typeof(FloatVector3)) },
            { new(0x3F615DD3, 0xDFE169BE), new(null, typeof(FloatVector3)) },
            { new(0x3F615DD3, 0xD8248865), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0xDF77EDBB), new(null, typeof(int)) },
            { new(0x3F615DD3, 0xF015A3A3), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0xF5572421), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0xD617B94C), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0xD2425420), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0xD260F3A7), new(null, typeof(FloatVector3)) },
            { new(0x3F615DD3, 0xD19D14C4), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0xC04EDCE4), new(null, typeof(FloatVector3)) },
            { new(0x3F615DD3, 0xC0D957DF), new(null, typeof(float)) },
            { new(0x3F615DD3, 0xCE132F86), new(null, typeof(bool)) },
            { new(0x3F615DD3, 0x2D07DA6F), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0xE6023240), new(null, typeof(Crc)) },
            { new(0x3F615DD3, 0x6ADE730A), new(null, typeof(Crc)) },

            // AnimatedTransition
            { new(0x153A0DF7, 0x1EDFEE31), new(null, typeof(FloatVector3)) },
            { new(0x153A0DF7, 0x5570EF5A), new(null, typeof(float)) },
            { new(0x153A0DF7, 0xEB4DFB24), new(null, typeof(Crc)) },

            // AttrPtAttachable
            { new(0xB88094A3, 0x92473A24), new(null, typeof(int)) },
            { new(0xB88094A3, 0xDC46BC02), new(null, typeof(float)) },
            { new(0xB88094A3, 0xA1B6ED7B), new(null, typeof(Crc)) },
            { new(0xB88094A3, 0x25580EC1), new(null, typeof(FloatVector3)) },
            { new(0xB88094A3, 0x28745834), new(null, typeof(Crc)) },

            // Audible
            { new(0xCC1EE14D, 0xCC1EE14D), new(null, typeof(Crc)) },
            { new(0xCC1EE14D, 0x42C6DA9D), new(null, typeof(int)) },
            { new(0xCC1EE14D, 0x246DB06C), new(null, typeof(Crc)) },
            { new(0xCC1EE14D, 0x3217CDF0), new(null, typeof(float)) },
            { new(0xCC1EE14D, 0x212A9DB0), new(null, typeof(bool)) },
            { new(0xCC1EE14D, 0x02D3EB9F), new(null, typeof(Crc)) },
            { new(0xCC1EE14D, 0x04FBB1EE), new(null, typeof(string)) },
            { new(0xCC1EE14D, 0x1E59E605), new(null, typeof(bool)) },
            { new(0xCC1EE14D, 0xD76D7747), new(null, typeof(Crc)) },
            { new(0xCC1EE14D, 0x85B86BD7), new(null, typeof(bool)) },
            { new(0xCC1EE14D, 0xAFCE4BA2), new(null, typeof(int)) },
            { new(0xCC1EE14D, 0xEC8964B3), new(null, typeof(int)) },
            { new(0xCC1EE14D, 0xE94E8608), new(null, typeof(int)) },

            // BigMap
            { new(0x3E85904D, 0xF2BF3D0A), new(null, typeof(Crc)) },

            // Bird
            { new(0xFF22E748, 0x7741D08C), new(null, typeof(float)) },
            { new(0xFF22E748, 0x1B8ABD66), new(null, typeof(float)) },
            { new(0xFF22E748, 0x285807C8), new(null, typeof(Crc)) },
            { new(0xFF22E748, 0x5B724250), new(null, typeof(Crc)) },
            { new(0xFF22E748, 0xE8FBAB1F), new(null, typeof(float)) },
            { new(0xFF22E748, 0xBD4A934E), new(null, typeof(float)) },
            { new(0xFF22E748, 0xCD922F16), new(null, typeof(float)) },
            { new(0xFF22E748, 0xEEE1A341), new(null, typeof(float)) },
            { new(0xFF22E748, 0xAC11B453), new(null, typeof(Crc)) },
            { new(0xFF22E748, 0x9F1233CA), new(null, typeof(float)) },
            { new(0xFF22E748, 0xAAD8CCD8), new(null, typeof(FloatVector2)) },
            { new(0xFF22E748, 0x86BF6C5B), new(null, typeof(Crc)) },
            { new(0xFF22E748, 0x7D09FBF0), new(null, typeof(float)) },
            { new(0xFF22E748, 0x8415C708), new(null, typeof(FloatVector2)) },

            // BirdSpawner
            { new(0x7E2A6F48, 0x3CDEDBED), new(null, typeof(bool)) },
            { new(0x7E2A6F48, 0x5D78EDE2), new(null, typeof(UnkBirdType)) },
            { new(0x7E2A6F48, 0x9FC92EBA), new(null, typeof(bool)) },

            // BonePair
            { new(0xFD1DAA2F, 0xFD1DAA2F), new(null, typeof(Crc)) },
            { new(0xFD1DAA2F, 0xAF96D677), new(null, typeof(int)) },
            { new(0xFD1DAA2F, 0x2A4BADFD), new(null, typeof(Crc)) },
            { new(0xFD1DAA2F, 0x39346352), new(null, typeof(Crc)) },

            // BridgeController
            { new(0x22DC02B6, 0x6A582E8F), new(null, typeof(float)) },
            { new(0x22DC02B6, 0x30057699), new(null, typeof(string)) },
            { new(0x22DC02B6, 0x2BF29A61), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0x2B81C6D2), new(null, typeof(int)) },
            { new(0x22DC02B6, 0x082C9DEA), new(null, typeof(float)) },
            { new(0x22DC02B6, 0x20BDCCE5), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0x537F205A), new(null, typeof(float)) },
            { new(0x22DC02B6, 0x51C72ACE), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0x41754A65), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0x45C52D5C), new(null, typeof(byte)) },
            { new(0x22DC02B6, 0xF15282D0), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0xDE72B5FA), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0xD81FD9CA), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0xDCDC8BCA), new(null, typeof(float)) },
            { new(0x22DC02B6, 0xA6E31CA5), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0x9DC09A87), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0xA3454BFF), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0x9BDA60CA), new(null, typeof(float)) },
            { new(0x22DC02B6, 0x6D32B59F), new(null, typeof(Crc)) },
            { new(0x22DC02B6, 0x9AD419A5), new(null, typeof(Crc)) },

            // bullet
            { new(0x74FD1BA9, 0x69DCA2B6), new(null, typeof(Crc)) },
            { new(0x74FD1BA9, 0x5E6A93DB), new(null, typeof(bool)) },
            { new(0x74FD1BA9, 0x4F7CB322), new(null, typeof(int)) },
            { new(0x74FD1BA9, 0x3FF4F976), new(null, typeof(Color)) },
            { new(0x74FD1BA9, 0x4C2B3699), new(null, typeof(float)) },
            { new(0x74FD1BA9, 0x32FDED30), new(null, typeof(bool)) },
            { new(0x74FD1BA9, 0x232BC103), new(null, typeof(float)) },
            { new(0x74FD1BA9, 0x231C955C), new(null, typeof(FloatVector2)) },
            { new(0x74FD1BA9, 0x08775F0D), new(null, typeof(float)) },
            { new(0x74FD1BA9, 0x15BA509E), new(null, typeof(float)) },
            { new(0x74FD1BA9, 0xD2D52E54), new(null, typeof(float)) },
            { new(0x74FD1BA9, 0xBA52C3D5), new(null, typeof(float)) },
            { new(0x74FD1BA9, 0xAF0D6C64), new(null, typeof(FloatVector2)) },
            { new(0x74FD1BA9, 0x73393C4A), new(null, typeof(Crc)) },
            { new(0x74FD1BA9, 0x785523D3), new(null, typeof(FloatVector2)) },
            { new(0x74FD1BA9, 0xE47A9E34), new(null, typeof(float)) },
            { new(0x74FD1BA9, 0xD4F11960), new(null, typeof(float)) },
            { new(0x74FD1BA9, 0xD850F531), new(null, typeof(Crc)) },
            { new(0x74FD1BA9, 0xF2D66F4B), new(null, typeof(FloatVector2)) },
            { new(0x74FD1BA9, 0x81349EE6), new(null, typeof(float)) }, // unused?
            { new(0x74FD1BA9, 0x80B6A0E5), new(null, typeof(float)) }, // unused?

            // ButtonPrompt
            { new(0x35899B4F, 0x067FDFF6), new(null, typeof(float)) },
            { new(0x35899B4F, 0x216E8465), new(null, typeof(float)) },
            { new(0x35899B4F, 0x888505C3), new(null, typeof(Crc)) },

            // CameraSet
            { new(0xF66B2194, 0xE9B2AF5C), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xEAC9D1D3), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xEBFD18F8), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xB9E56DFB), new(null, typeof(float)) },
            { new(0xF66B2194, 0xBEB8563D), new(null, typeof(FloatVector2)) },
            { new(0xF66B2194, 0xD990F6D4), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xE15ACEDF), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xB8A54683), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xA87C312F), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xAB273060), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xACD1E254), new(null, typeof(bool)) },
            { new(0xF66B2194, 0xA140B48B), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x7C2247EB), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x8DAF7BB0), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x9553EE07), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x7A5F2528), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x7846B1F5), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x7922C257), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x7310C1E4), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x477B3FF1), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x5FD829AF), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x63784F61), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x3EACD53A), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x28774735), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x2C662E84), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x39E594EB), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x21FD66DE), new(null, typeof(float)) },
            { new(0xF66B2194, 0x18AA5AD1), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0x1CFA0467), new(null, typeof(float)) },
            { new(0xF66B2194, 0x1EA825AC), new(null, typeof(Crc)) },
            { new(0xF66B2194, 0xB5C6777D), new(null, typeof(Crc)) },

            // CameraSettings
            { new(0x0BB693A3, 0x0BB693A3), new(null, typeof(Crc)) },
            { new(0x0BB693A3, 0x70092C52), new(null, typeof(Crc)) },
            { new(0x0BB693A3, 0x2B88CF92), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0x1B457A75), new(null, typeof(bool)) },
            { new(0x0BB693A3, 0x1C5A8313), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x225331E5), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x14804B69), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0x0F98B2A1), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x0F5A6D51), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0x05D2A6D9), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x0EEC368F), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0x4C38DE5E), new(null, typeof(bool)) },
            { new(0x0BB693A3, 0x38940C6B), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x3634D93E), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x30C25EA4), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x3258D692), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x5021D09E), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x58265A64), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x5832E0D8), new(null, typeof(Crc)) },
            { new(0x0BB693A3, 0x7B70B843), new(null, typeof(bool)) },
            { new(0x0BB693A3, 0x7228C1E9), new(null, typeof(float)) },
            { new(0x0BB693A3, 0xAB302655), new(null, typeof(FloatVector2)) },
            { new(0x0BB693A3, 0x9D03C800), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x9E6C6E7C), new(null, typeof(FloatVector2)) },
            { new(0x0BB693A3, 0xA4F21228), new(null, typeof(bool)) },
            { new(0x0BB693A3, 0x8D52E4E6), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0x7D41B349), new(null, typeof(Crc)) },
            { new(0x0BB693A3, 0x85622E42), new(null, typeof(float)) },
            { new(0x0BB693A3, 0x8D4ED835), new(null, typeof(float)) },
            { new(0x0BB693A3, 0xEA87B9F7), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0xAF555903), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0xC3C4BCD8), new(null, typeof(float)) },
            { new(0x0BB693A3, 0xE3B96771), new(null, typeof(float)) },
            { new(0x0BB693A3, 0xF2795A1E), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0xFA7DE3E4), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0xFBC31529), new(null, typeof(FloatVector3)) },
            { new(0x0BB693A3, 0x94114E8C), new(null, typeof(Crc)) }, // unused
            { new(0x0BB693A3, 0x58BCB46C), new(null, typeof(int)) }, // unused?
            { new(0x0BB693A3, 0xEACA428D), new(null, typeof(Crc)) }, // unused?
            { new(0x0BB693A3, 0xF5C8F95D), new(null, typeof(Crc)) }, // unused?
            { new(0x0BB693A3, 0x79AD9888), new(null, typeof(FloatVector3)) }, // unused?
            { new(0x0BB693A3, 0xB2AE0289), new(null, typeof(float)) }, // unused?
            { new(0x0BB693A3, 0x652B734C), new(null, typeof(float)) }, // unused?
            { new(0x0BB693A3, 0xD43131F3), new(null, typeof(float)) }, // unused?

            // CameraSettingsMisc
            { new(0xB8F740AF, 0x61F41981), new(null, typeof(float)) },
            { new(0xB8F740AF, 0x4CA29FD3), new(null, typeof(float)) },
            { new(0xB8F740AF, 0x2B9B7B11), new(null, typeof(float)) },
            { new(0xB8F740AF, 0x069650EE), new(null, typeof(float)) },
            { new(0xB8F740AF, 0x2387D0AD), new(null, typeof(float)) },
            { new(0xB8F740AF, 0xB0F78533), new(null, typeof(float)) },
            { new(0xB8F740AF, 0x930F51B6), new(null, typeof(FloatVector2)) },
            { new(0xB8F740AF, 0x88E6A6CA), new(null, typeof(float)) },
            { new(0xB8F740AF, 0x75DED125), new(null, typeof(FloatVector2)) },
            { new(0xB8F740AF, 0x87107D9E), new(null, typeof(float)) },
            { new(0xB8F740AF, 0xDD0719F5), new(null, typeof(float)) },
            { new(0xB8F740AF, 0xB99955C4), new(null, typeof(float)) },
            { new(0xB8F740AF, 0xCDD597DD), new(null, typeof(float)) },
            { new(0xB8F740AF, 0xF4219F95), new(null, typeof(float)) },
            { new(0xB8F740AF, 0x94114E8C), new(null, typeof(Crc)) }, // unused

            // Car
            { new(0xCE27C791, 0x12649314), new(null, typeof(bool)) },

            // Cinematics
            { new(0x0F7BD3F7, 0x77B757B0), new(null, typeof(Crc)) },
            { new(0x0F7BD3F7, 0x6018F238), new(null, typeof(byte)) },
            { new(0x0F7BD3F7, 0xC79442F1), new(null, typeof(byte)) },
            { new(0x0F7BD3F7, 0xE5746957), new(null, typeof(byte)) },
            { new(0x0F7BD3F7, 0xF21424F2), new(null, typeof(byte)) },
            { new(0x0F7BD3F7, 0x33D47464), new(null, typeof(Crc)) }, // unused?
            { new(0x0F7BD3F7, 0x9965E3D4), new(null, typeof(int)) }, // unused?

            // CivilianProp
            { new(0xE7B1D609, 0x5E93C871), new(null, typeof(bool)) },
            { new(0xE7B1D609, 0x8C4CDBA1), new(null, typeof(Crc)) },
            { new(0xE7B1D609, 0x5B724250), new(null, typeof(Crc)) },
            { new(0xE7B1D609, 0x06DA8775), new(null, typeof(Crc)) },
            { new(0xE7B1D609, 0x19B0426A), new(null, typeof(Crc)) },
            { new(0xE7B1D609, 0x2D0720BF), new(null, typeof(Crc)) },

            // ClothForce
            { new(0x060FD1B4, 0x40359952), new(null, typeof(Crc)) },
            { new(0x060FD1B4, 0xE0AA99D6), new(null, typeof(float)) },
            { new(0x060FD1B4, 0xD88D5899), new(null, typeof(float)) },
            { new(0x060FD1B4, 0xCEDABF19), new(null, typeof(float)) },
            { new(0x060FD1B4, 0xCFF89665), new(null, typeof(float)) },
            { new(0x060FD1B4, 0x810318DB), new(null, typeof(float)) },
            { new(0x060FD1B4, 0x6DCDF8F7), new(null, typeof(float)) },
            { new(0x060FD1B4, 0x50C3B0B7), new(null, typeof(float)) },
            { new(0x060FD1B4, 0x08D78FD0), new(null, typeof(float)) },
            { new(0x060FD1B4, 0x47A03D28), new(null, typeof(float)) },

            // ClothObject
            { new(0x56A984DE, 0x5B724250), new(null, typeof(Crc)) },
            { new(0x56A984DE, 0x2FBC51C3), new(null, typeof(Crc)) },
            { new(0x56A984DE, 0x5700AB72), new(null, typeof(int)) },
            { new(0x56A984DE, 0x523AFE65), new(null, typeof(Crc)) },
            { new(0x56A984DE, 0x060FD1B4), new(null, typeof(Crc)) },
            { new(0x56A984DE, 0x8A7EA61A), new(null, typeof(byte)) },
            { new(0x56A984DE, 0xE0B51584), new(null, typeof(Crc)) },
            { new(0x56A984DE, 0xE3AEC3CD), new(null, typeof(int)) },
            { new(0x56A984DE, 0x3EEACE1A), new(null, typeof(int)) },
            { new(0x56A984DE, 0x283C9626), new(null, typeof(int)) },
            { new(0x56A984DE, 0x23A31F18), new(null, typeof(int)) },
            { new(0x56A984DE, 0x6C89C416), new(null, typeof(int)) },
            { new(0x56A984DE, 0xE11B7999), new(null, typeof(Crc)) }, // unused?
            // ClothObject - sub_7F7580
            { new(0x56A984DE, 0x1D679AEB), new(null, typeof(float)) },
            { new(0x56A984DE, 0x305856DF), new(null, typeof(float)) },
            // ClothObject - sub_7F7480
            { new(0x56A984DE, 0xEFE5D990), new(null, typeof(Crc)) },
            { new(0x56A984DE, 0xEAFC3A7D), new(null, typeof(Crc)) },
            { new(0x56A984DE, 0x69A86828), new(null, typeof(int)) },
            { new(0x56A984DE, 0x5AC2612B), new(null, typeof(FloatVector3)) },
            { new(0x56A984DE, 0xA0D456CB), new(null, typeof(int)) },
            { new(0x56A984DE, 0xC3AEB321), new(null, typeof(float)) },
            { new(0x56A984DE, 0xCBE8ED58), new(null, typeof(float)) },
            // ClothObject - sub_7F6F10
            { new(0x56A984DE, 0x982589A0), new(null, typeof(float)) },
            { new(0x56A984DE, 0xB9C2B47A), new(null, typeof(Crc)) },

            // Common
            { new(0x2447DFFA, 0x6302F1CC), new(null, typeof(bool)) },
            { new(0x2447DFFA, 0x8C9928FB), new(null, typeof(bool)) },
            { new(0x2447DFFA, 0x87519019), new(null, typeof(int)) },
            { new(0x2447DFFA, 0xBF83D3AF), new(null, typeof(Crc)) },
            { new(0x2447DFFA, 0x0C2DB3B4), new(null, typeof(bool)) },

            // Controllable
            { new(0x2C70C910, 0xFB31F1EF), new(null, typeof(Crc)) },
            { new(0x2C70C910, 0x404D1343), new(null, typeof(Crc)) },
            { new(0x2C70C910, 0xDB0F705C), new(null, typeof(LuaParam)) },

            // CreditName
            { new(0xFE44888F, 0x6C72334B), new(null, typeof(int)) },
            { new(0xFE44888F, 0xBFC5652D), new(null, typeof(float)) },
            { new(0xFE44888F, 0xE1AE04B8), new(null, typeof(float)) },

            // Damageable
            { new(0x6068DB46, 0xA80C9145), new(null, typeof(Crc)) },
            { new(0x6068DB46, 0xB37BF5B2), new(null, typeof(float)) },
            { new(0x6068DB46, 0x97E0DB0F), new(null, typeof(float)) },
            { new(0x6068DB46, 0xA1B82BA7), new(null, typeof(Crc)) },
            { new(0x6068DB46, 0xAC0F66B8), new(null, typeof(float)) },
            { new(0x6068DB46, 0x08ECB69D), new(null, typeof(float)) },
            { new(0x6068DB46, 0x8CBC442D), new(null, typeof(float)) },
            { new(0x6068DB46, 0x82E62211), new(null, typeof(Crc)) },
            { new(0x6068DB46, 0x7C203814), new(null, typeof(float)) },
            { new(0x6068DB46, 0x81CD9A01), new(null, typeof(float)) },
            { new(0x6068DB46, 0xDD7EAE2C), new(null, typeof(float)) },
            { new(0x6068DB46, 0xDA3A3781), new(null, typeof(float)) },
            { new(0x6068DB46, 0xD0E3A1A1), new(null, typeof(float)) },
            { new(0x6068DB46, 0xC11C102E), new(null, typeof(float)) },
            { new(0x6068DB46, 0xC86877FD), new(null, typeof(int)) },
            { new(0x6068DB46, 0xF7C72618), new(null, typeof(float)) },
            { new(0x6068DB46, 0xF9C458E7), new(null, typeof(float)) },
            { new(0x6068DB46, 0xFD4869CD), new(null, typeof(FloatVector2)) },
            { new(0x6068DB46, 0x75791974), new(null, typeof(float)) },
            { new(0x6068DB46, 0x55817592), new(null, typeof(Crc)) },
            { new(0x6068DB46, 0x6AC12B1E), new(null, typeof(float)) },
            { new(0x6068DB46, 0x73FF7621), new(null, typeof(float)) },
            { new(0x6068DB46, 0x54019561), new(null, typeof(float)) },
            { new(0x6068DB46, 0x4F18FA7A), new(null, typeof(float)) },
            { new(0x6068DB46, 0x496A25D2), new(null, typeof(byte)) },
            { new(0x6068DB46, 0x49C74990), new(null, typeof(Crc)) },
            { new(0x6068DB46, 0x4B186A63), new(null, null) },
            { new(0x6068DB46, 0x416E074F), new(null, typeof(float)) },
            { new(0x6068DB46, 0x2D96C4F2), new(null, typeof(float)) },
            { new(0x6068DB46, 0x2A3029F3), new(null, typeof(float)) },
            { new(0x6068DB46, 0x24E96E81), new(null, typeof(float)) },
            { new(0x6068DB46, 0x0E29263B), new(null, typeof(float)) },
            { new(0x6068DB46, 0x12066797), new(null, typeof(float)) },
            { new(0x6068DB46, 0x3121EECD), new(null, typeof(Crc)) },
            { new(0x6068DB46, 0x3D960EA8), new(null, typeof(float)) },
            { new(0x6068DB46, 0x414631B3), new(null, typeof(int)) },
            { new(0x6068DB46, 0x8ECB69DB), new(null, typeof(float)) },

            // DamageablePart
            { new(0xAEE470F5, 0x6DD070B1), new(null, typeof(float)) },
            { new(0xAEE470F5, 0x30812467), new(null, typeof(string)) },
            { new(0xAEE470F5, 0x3885F292), new(null, typeof(bool)) },
            { new(0xAEE470F5, 0x558C91D7), new(null, typeof(string)) },
            { new(0xAEE470F5, 0x22650220), new(null, typeof(bool)) },
            { new(0xAEE470F5, 0x03E19511), new(null, typeof(float)) },
            { new(0xAEE470F5, 0x187B88F6), new(null, typeof(string)) },
            { new(0xAEE470F5, 0x207FBA52), new(null, typeof(Crc)) },
            { new(0xAEE470F5, 0x9465294B), new(null, typeof(float)) },
            { new(0xAEE470F5, 0x7DB13D1F), new(null, typeof(string)) },
            { new(0xAEE470F5, 0x86DE6639), new(null, typeof(bool)) },
            { new(0xAEE470F5, 0x8D117B98), new(null, typeof(float)) },
            { new(0xAEE470F5, 0xC4C441BD), new(null, typeof(float)) },
            { new(0xAEE470F5, 0xDD2AA7AB), new(null, typeof(Crc)) },

            // DamageableRoot
            { new(0x755FC33E, 0x1AAC9531), new(null, typeof(Crc)) },
            { new(0x755FC33E, 0xBB0BF4A2), new(null, typeof(Crc)) },
            { new(0x755FC33E, 0xE3EB3242), new(null, typeof(int)) },
            { new(0x755FC33E, 0xC8D0F617), new(null, typeof(Crc)) },
            { new(0x755FC33E, 0x3C56B891), new(null, typeof(Crc)) },
            { new(0x755FC33E, 0x49D5C2AE), new(null, typeof(Crc)) },

            // DamageRegion
            { new(0xC87F672A, 0x31C25320), new(null, typeof(float)) },
            { new(0xC87F672A, 0x216E8465), new(null, typeof(float)) },
            { new(0xC87F672A, 0x28E10525), new(null, typeof(Crc)) },
            { new(0xC87F672A, 0x3F955DB8), new(null, typeof(float)) },
            { new(0xC87F672A, 0x848096A5), new(null, typeof(Crc)) },

            // DamageSphere
            { new(0x6898A77D, 0x354694DB), new(null, typeof(float)) },
            { new(0x6898A77D, 0x7617FD5B), new(null, typeof(float)) },

            // Decal
            { new(0x209311CC, 0xD3485F14), new(null, typeof(float)) },
            { new(0x209311CC, 0xC6EC1E86), new(null, typeof(float)) },
            { new(0x209311CC, 0x9DB73D8F), new(null, typeof(float)) },
            { new(0x209311CC, 0x7172B7AE), new(null, typeof(Crc)) },
            { new(0x209311CC, 0x8C3654C2), new(null, typeof(float)) },
            { new(0x209311CC, 0xF011157A), new(null, typeof(Crc)) },
            { new(0x209311CC, 0xD7D8D464), new(null, typeof(float)) },
            { new(0x209311CC, 0xDF3262F2), new(null, typeof(FloatVector3)) },
            { new(0x209311CC, 0xFA4C29B8), new(null, typeof(FloatVector4)) },

            // DetailObject
            { new(0xFC92396B, 0xA90C7E48), new(null, typeof(float)) },
            { new(0xFC92396B, 0x440E11D7), new(null, typeof(float)) },
            { new(0xFC92396B, 0x5B724250), new(null, typeof(Crc)) },
            { new(0xFC92396B, 0x6DA91CF2), new(null, typeof(float)) },
            { new(0xFC92396B, 0xB7A38021), new(null, typeof(float)) },
            { new(0xFC92396B, 0xE0193B96), new(null, typeof(bool)) },
            { new(0xFC92396B, 0xF0FA376C), new(null, typeof(float)) },
            { new(0xFC92396B, 0x87DD9796), new(null, typeof(bool)) }, // unused?
            { new(0xFC92396B, 0x8A09514E), new(null, typeof(bool)) }, // unused?

            // Difficulty
            { new(0x4C38AB2E, 0x7F1A3641), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0x3208D523), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0x54586368), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0x6CA5A28E), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0x30ECF5DC), new(null, typeof(int)) },
            { new(0x4C38AB2E, 0x224AD76D), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0x1C985C43), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0x01EB8596), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0x1045459A), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0xB0D67841), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0x80235C30), new(null, typeof(int)) },
            { new(0x4C38AB2E, 0x860E70A3), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0xA892B18B), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0xCC919414), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0xD7C38E8D), new(null, typeof(float)) },
            { new(0x4C38AB2E, 0xDFB27B14), new(null, typeof(int)) },

            // ElasticTransition
            { new(0xDC07C4C3, 0xDC07C4C3), new(null, typeof(Crc)) },
            { new(0xDC07C4C3, 0x6F6C645F), new(null, typeof(FloatVector3)) },
            { new(0xDC07C4C3, 0x4EF5360B), new(null, typeof(float)) },
            { new(0xDC07C4C3, 0x55DD23A8), new(null, typeof(FloatVector3)) },
            { new(0xDC07C4C3, 0x2DD64D12), new(null, typeof(FloatVector3)) },
            { new(0xDC07C4C3, 0x04D112AC), new(null, typeof(float)) },
            { new(0xDC07C4C3, 0x0E3ABED0), new(null, typeof(float)) },
            { new(0xDC07C4C3, 0xF73B0955), new(null, typeof(FloatVector3)) },
            { new(0xDC07C4C3, 0x8197FA7F), new(null, typeof(byte)) },
            { new(0xDC07C4C3, 0xD6F1AB54), new(null, typeof(FloatVector3)) },
            { new(0xDC07C4C3, 0xF741FF8D), new(null, typeof(float)) },
            { new(0xDC07C4C3, 0xF823CD96), new(null, typeof(FloatVector3)) },

            // Escalation
            { new(0xF69DEC26, 0x7305C643), new(null, typeof(float)) },
            { new(0xF69DEC26, 0x2B8D1FDF), new(null, typeof(Crc)) },
            { new(0xF69DEC26, 0x18A92078), new(null, typeof(bool)) },
            { new(0xF69DEC26, 0x07E2752A), new(null, typeof(int)) },
            { new(0xF69DEC26, 0x06F0936C), new(null, typeof(float)) },
            { new(0xF69DEC26, 0x1D5DD1C0), new(null, typeof(int)) },
            { new(0xF69DEC26, 0x67740C82), new(null, typeof(float)) },
            { new(0xF69DEC26, 0x62780BA3), new(null, typeof(int)) },
            { new(0xF69DEC26, 0x3204FD62), new(null, typeof(float)) },
            { new(0xF69DEC26, 0x3A527ED5), new(null, typeof(Crc)) },
            { new(0xF69DEC26, 0xE04DAEEB), new(null, typeof(bool)) },
            { new(0xF69DEC26, 0xE8020177), new(null, typeof(Crc)) },
            { new(0xF69DEC26, 0xEA355018), new(null, typeof(Crc)) },
            { new(0xF69DEC26, 0xE00E1D57), new(null, typeof(Crc)) },
            { new(0xF69DEC26, 0xB4F87903), new(null, typeof(Crc)) },
            { new(0xF69DEC26, 0xBD40121F), new(null, typeof(bool)) },
            { new(0xF69DEC26, 0xBE01F853), new(null, typeof(float)) },
            { new(0xF69DEC26, 0xAAADE1A8), new(null, typeof(bool)) },
            { new(0xF69DEC26, 0x99F65298), new(null, typeof(int)) },
            { new(0xF69DEC26, 0x9CF94678), new(null, typeof(float)) },
            { new(0xF69DEC26, 0xAA7CEDE2), new(null, typeof(Crc)) },
            { new(0xF69DEC26, 0x97303F8D), new(null, typeof(float)) },
            { new(0xF69DEC26, 0x7A0C1851), new(null, typeof(Crc)) },
            { new(0xF69DEC26, 0x89BCD2AA), new(null, typeof(int)) },
            { new(0xF69DEC26, 0x8C1C4A5B), new(null, typeof(bool)) },

            // EscHWTF
            { new(0x954C883D, 0xF661BD0B), new(null, typeof(FloatVector2)) },
            { new(0x954C883D, 0xF705A3AA), new(null, typeof(float)) },
            { new(0x954C883D, 0xDA6B4D87), new(null, typeof(int)) },
            { new(0x954C883D, 0x89FAEAC2), new(null, typeof(Crc)) },
            { new(0x954C883D, 0xC37246F2), new(null, typeof(Crc)) },
            { new(0x954C883D, 0xD9A213BF), new(null, typeof(float)) },
            { new(0x954C883D, 0x7C0CF3AA), new(null, typeof(Crc)) },
            { new(0x954C883D, 0x49212E5A), new(null, typeof(int)) },
            { new(0x954C883D, 0x56D60972), new(null, typeof(Crc)) },

            // EventConversation

            // Explosion
            { new(0x16930AFE, 0xEE0E25BA), new(null, typeof(float)) },
            { new(0x16930AFE, 0xEBB0A63E), new(null, typeof(float)) },
            { new(0x16930AFE, 0xD8727836), new(null, typeof(bool)) },
            { new(0x16930AFE, 0xDD62BA1A), new(null, typeof(string)) },
            { new(0x16930AFE, 0xA1ED7451), new(null, typeof(float)) },
            { new(0x16930AFE, 0x9F710C1B), new(null, typeof(float)) },
            { new(0x16930AFE, 0x848096A5), new(null, typeof(Crc)) },
            { new(0x16930AFE, 0x79BA2FD0), new(null, typeof(float)) },
            { new(0x16930AFE, 0x82EDC825), new(null, typeof(bool)) },
            { new(0x16930AFE, 0x74F54615), new(null, typeof(float)) },
            { new(0x16930AFE, 0x47E338FE), new(null, typeof(float)) },
            { new(0x16930AFE, 0x5E244327), new(null, typeof(float)) },
            { new(0x16930AFE, 0x62E0AFDE), new(null, typeof(bool)) },
            { new(0x16930AFE, 0x349BF324), new(null, typeof(float)) },
            { new(0x16930AFE, 0x2EBA9DF8), new(null, typeof(float)) },
            { new(0x16930AFE, 0x14282B50), new(null, typeof(int)) },
            { new(0x16930AFE, 0x0FCE73F0), new(null, typeof(float)) },
            { new(0x16930AFE, 0x12BB20A1), new(null, typeof(float)) },

            // FaceExpression
            { new(0x5A8EE34C, 0x216E8465), new(null, typeof(float)) },
            { new(0x5A8EE34C, 0xC023ACD3), new(null, typeof(Crc)) },
            { new(0x5A8EE34C, 0xC2161E01), new(null, typeof(float)) },

            // FlameOrdnance
            { new(0x9961B9C6, 0x2F2D4561), new(null, typeof(Crc)) },
            { new(0x9961B9C6, 0x84C839C8), new(null, typeof(float)) },
            { new(0x9961B9C6, 0xC6F33E78), new(null, typeof(float)) },

            // FlashMovie
            { new(0x9CC86FE3, 0x9CAA0ADF), new(null, typeof(Crc)) },
            { new(0x9CC86FE3, 0xA1F61387), new(null, typeof(int)) },

            // Foliage
            { new(0x34612F86, 0x7D4B28A4), new(null, typeof(float)) },
            { new(0x34612F86, 0x4C77AA6B), new(null, typeof(float)) },
            { new(0x34612F86, 0x33A5B563), new(null, typeof(float)) },
            { new(0x34612F86, 0x28E10525), new(null, typeof(Crc)) },
            { new(0x34612F86, 0x19A14C78), new(null, typeof(Crc)) },
            { new(0x34612F86, 0x1AAC9531), new(null, typeof(Crc)) },
            { new(0x34612F86, 0x1C0AEE5E), new(null, typeof(FloatVector3)) },
            { new(0x34612F86, 0x2B39F30E), new(null, typeof(float)) },
            { new(0x34612F86, 0x30C712EE), new(null, typeof(Color)) },
            { new(0x34612F86, 0x333C2126), new(null, typeof(FloatVector2)) },
            { new(0x34612F86, 0x41CE3C45), new(null, typeof(float)) },
            { new(0x34612F86, 0x442FED33), new(null, typeof(float)) },
            { new(0x34612F86, 0x44416D71), new(null, typeof(bool)) },
            { new(0x34612F86, 0x3FF4F976), new(null, typeof(FloatVector3_2)) },
            { new(0x34612F86, 0x342F95F3), new(null, typeof(float)) },
            { new(0x34612F86, 0x354694DB), new(null, typeof(float)) },
            { new(0x34612F86, 0x366C9D39), new(null, typeof(float)) },
            { new(0x34612F86, 0x6540AEA9), new(null, typeof(float)) },
            { new(0x34612F86, 0x5B724250), new(null, typeof(Crc)) },
            { new(0x34612F86, 0x5F7E3483), new(null, typeof(float)) },
            { new(0x34612F86, 0x5B5DA35C), new(null, typeof(float)) },
            { new(0x34612F86, 0x5651715A), new(null, typeof(float)) },
            { new(0x34612F86, 0x58CDFFFA), new(null, typeof(float)) },
            { new(0x34612F86, 0x5A32105C), new(null, typeof(float)) },
            { new(0x34612F86, 0x772B0B2F), new(null, typeof(FloatVector2)) },
            { new(0x34612F86, 0x7A0C0640), new(null, typeof(Crc)) },
            { new(0x34612F86, 0x706C9B59), new(null, typeof(float)) },
            { new(0x34612F86, 0x6A759B0E), new(null, typeof(float)) },
            { new(0x34612F86, 0x6B451DDE), new(null, typeof(float)) },
            { new(0x34612F86, 0x6B58EE52), new(null, typeof(float)) },
            { new(0x34612F86, 0xBF58B73D), new(null, typeof(float)) },
            { new(0x34612F86, 0xB858C765), new(null, typeof(float)) },
            { new(0x34612F86, 0xBB4D8134), new(null, typeof(Crc)) },
            { new(0x34612F86, 0xBC653EB9), new(null, typeof(int)) },
            { new(0x34612F86, 0xB66A6C44), new(null, typeof(int)) },
            { new(0x34612F86, 0xB276FBA3), new(null, typeof(float)) },
            { new(0x34612F86, 0xAF895BE5), new(null, typeof(FloatVector3)) },
            { new(0x34612F86, 0xA5FBBC36), new(null, typeof(int)) },
            { new(0x34612F86, 0xAD96C3CD), new(null, typeof(float)) },
            { new(0x34612F86, 0x9DE9171D), new(null, typeof(float)) },
            { new(0x34612F86, 0x98508662), new(null, typeof(bool)) },
            { new(0x34612F86, 0x982222A6), new(null, typeof(float)) },
            { new(0x34612F86, 0x8B266E12), new(null, typeof(bool)) },
            { new(0x34612F86, 0x8C3654C2), new(null, typeof(FloatVector4)) },
            { new(0x34612F86, 0x8AC3D33F), new(null, typeof(FloatVector3)) },
            { new(0x34612F86, 0x85EF4DEF), new(null, typeof(float)) },
            { new(0x34612F86, 0x83944318), new(null, typeof(float)) },
            { new(0x34612F86, 0x7ED37781), new(null, typeof(float)) },
            { new(0x34612F86, 0x835BA097), new(null, typeof(float)) },
            { new(0x34612F86, 0xEE7814DA), new(null, typeof(int)) },
            { new(0x34612F86, 0xE3C7C6F2), new(null, typeof(int)) },
            { new(0x34612F86, 0xEAD94CF1), new(null, typeof(FloatVector2)) },
            { new(0x34612F86, 0xEB506256), new(null, typeof(FloatVector2)) },
            { new(0x34612F86, 0xE3518BA6), new(null, typeof(FloatVector2)) },
            { new(0x34612F86, 0xD9DA3DC3), new(null, typeof(float)) },
            { new(0x34612F86, 0xC37D6213), new(null, typeof(FloatVector2)) },
            { new(0x34612F86, 0xCCA9F9FC), new(null, typeof(float)) },
            { new(0x34612F86, 0xE1F9CCD5), new(null, typeof(FloatVector3)) },
            { new(0x34612F86, 0xFB91EBD1), new(null, typeof(FloatVector2)) },
            { new(0x34612F86, 0xF6D9B967), new(null, typeof(float)) },
            { new(0x34612F86, 0xF55BB92D), new(null, typeof(float)) },
            { new(0x34612F86, 0xF011157A), new(null, typeof(Crc)) },
            { new(0x34612F86, 0xF37790E1), new(null, typeof(FloatVector3_2)) },
            { new(0x34612F86, 0xFC3E92A7), new(null, typeof(int)) },
            { new(0x34612F86, 0xFC48FDD6), new(null, typeof(FloatVector3)) },
            { new(0x34612F86, 0xFD44ECDE), new(null, typeof(float)) },
            { new(0x34612F86, 0x1B4C6669), new(null, typeof(float)) }, // unused?
            { new(0x34612F86, 0x5E34DB53), new(null, typeof(FloatVector3)) }, // unused?
            { new(0x34612F86, 0x359DAEF0), new(null, typeof(int)) }, // unused?
            { new(0x34612F86, 0x6B118329), new(null, typeof(int)) }, // unused?
            { new(0x34612F86, 0x0397834F), new(null, typeof(int)) }, // unused?
            { new(0x34612F86, 0x9AB5E710), new(null, typeof(int)) }, // unused?

            // FoliageFx
            { new(0xD26EF074, 0x664DE09A), new(null, typeof(int)) },
            { new(0xD26EF074, 0x0C8091C3), new(null, typeof(int)) },
            { new(0xD26EF074, 0x12AABDBB), new(null, typeof(int)) },
            { new(0xD26EF074, 0x8E499CBD), new(null, typeof(int)) },
            { new(0xD26EF074, 0xC3D9DCE4), new(null, typeof(int)) },
            { new(0xD26EF074, 0xE33015E8), new(null, typeof(int)) },

            // FxBoneStateList
            { new(0x6D73FB6C, 0xC5F9CE77), new(null, typeof(int)) },
            { new(0x6D73FB6C, 0xC187105B), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x705C4508), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x9B1CAB9F), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x11D097C4), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x7C933EC3), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xA95EFF50), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xDF7ED3F7), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x69062F4A), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xA338F0A3), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xD0F118E9), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x75770E10), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x9C30A261), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xFCEDE148), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x4A67F77F), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xF582D3DE), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x30103344), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x9AB50943), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xDDA96923), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xE813E0CA), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x7EEE4AE7), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xB1D46D66), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x59A665BC), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xE61CF0FB), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x8CBDA906), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x907A0795), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x2FB357E5), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x80C93F04), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x62B99207), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x6A2D0D06), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xEE442EEC), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x00B9682B), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xA850FE37), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x826BBC66), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xB32727CC), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0xD44E722A), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x95E011B8), new(null, typeof(Crc)) },
            { new(0x6D73FB6C, 0x64C4150A), new(null, typeof(Crc)) },

            // FxHumanBodyPart
            { new(0x08271F91, 0x679CD9A1), new(null, typeof(int)) },
            { new(0x08271F91, 0x1CBA8055), new(null, typeof(Crc)) },
            { new(0x08271F91, 0x05FBAB16), new(null, typeof(Crc)) },
            { new(0x08271F91, 0x18281B3B), new(null, typeof(byte)) },
            { new(0x08271F91, 0x3FF4F976), new(null, typeof(Color)) },
            { new(0x08271F91, 0x62404569), new(null, typeof(Crc)) },
            { new(0x08271F91, 0xD9725C55), new(null, typeof(Crc)) },
            { new(0x08271F91, 0xCBA8D62C), new(null, typeof(Crc)) },
            { new(0x08271F91, 0xD6A5096B), new(null, typeof(float)) },
            { new(0x08271F91, 0xE5955136), new(null, typeof(int)) },
            { new(0x08271F91, 0xF2F6EAEC), new(null, typeof(Crc)) },

            // FxHumanBodySetup
            { new(0xD9F7FCE1, 0x622DD842), new(null, typeof(Crc)) },
            { new(0xD9F7FCE1, 0x1E233FA7), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0x37A9525D), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0x44C27FCB), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0xCBA8D62C), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0xC3A43714), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0xC57D2B43), new(null, typeof(Crc)) },
            { new(0xD9F7FCE1, 0xA1757F0A), new(null, typeof(Crc)) },
            { new(0xD9F7FCE1, 0x79691A2F), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0x922CC098), new(null, typeof(Crc)) },
            { new(0xD9F7FCE1, 0x9E288655), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0xDB9C23DC), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0xD0541403), new(null, typeof(float)) },
            { new(0xD9F7FCE1, 0xD594DB5B), new(null, typeof(float)) },
            { new(0xD9F7FCE1, 0xDD1296DA), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0xFC20CB8A), new(null, typeof(int)) },
            { new(0xD9F7FCE1, 0xD4D4470A), new(null, typeof(int)) },

            // FxHumanHead
            { new(0x7DAB4B80, 0xE5955136), new(null, typeof(int)) },
            { new(0x7DAB4B80, 0xA714324C), new(null, typeof(int)) },
            { new(0x7DAB4B80, 0xD6A5096B), new(null, typeof(float)) },
            { new(0x7DAB4B80, 0xD9725C55), new(null, typeof(Crc)) },
            { new(0x7DAB4B80, 0xEE7D0BA3), new(null, typeof(Crc)) },
            { new(0x7DAB4B80, 0xF2F6EAEC), new(null, typeof(Crc)) },
            { new(0x7DAB4B80, 0xF83F6C44), new(null, typeof(Crc)) },
            { new(0x7DAB4B80, 0x6DA79337), new(null, typeof(int)) },
            { new(0x7DAB4B80, 0x3FF4F976), new(null, typeof(Color)) },
            { new(0x7DAB4B80, 0x62404569), new(null, typeof(Crc)) },
            { new(0x7DAB4B80, 0x679CD9A1), new(null, typeof(int)) },
            { new(0x7DAB4B80, 0x1DCCDB2F), new(null, typeof(Crc)) },
            { new(0x7DAB4B80, 0x08C3EE6A), new(null, typeof(float)) },
            { new(0x7DAB4B80, 0x1CBA8055), new(null, typeof(int)) },
            { new(0x7DAB4B80, 0x1CF649BB), new(null, typeof(Crc)) },

            // GlobalHumanParams
            { new(0x9BD2B975, 0x79FF37D8), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x796C0168), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x734725B4), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x6E6C1E76), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x7266A3E0), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x6B5C5A52), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x6B0C9EF7), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x6A342CFD), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x69F626E3), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x6A2053B0), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x63534A45), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x620638F7), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x61B679B5), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x5E69205A), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x5EF3826E), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x5E139320), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x5B46E91F), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x5B344D5B), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x46D714E4), new(null, typeof(bool)) },
            { new(0x9BD2B975, 0x49CD57F2), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x43D513EA), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x3DB0B46D), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x398561D0), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x31BAF546), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x39716AB8), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x3146D71C), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x2FF6676A), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x2EA7EE38), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x1A768B82), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x1EB46739), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x18095A8F), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x14BD1BBD), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x12FD7382), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x0DB140D4), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x0EB616D3), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x0C8F30D1), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x0BB652B3), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x0903D3E8), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x0434677B), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x0574A76B), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xB3B1347F), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xB0925545), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xB13CC96E), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xB0241374), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xACE10817), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xAE8F3E25), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xAEA0FB1D), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xAB5952AC), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xA7617930), new(null, typeof(FloatVector2)) },
            { new(0x9BD2B975, 0xAA5854F8), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xA5FCD9D6), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xA014BF31), new(null, typeof(Crc)) },
            { new(0x9BD2B975, 0xA02760FD), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xA3A2B043), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x99D71EF2), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x992FA377), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x99568063), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x94726DCA), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x8E908EC4), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x9275AB49), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x92A71E3A), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x8CD58A95), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x869FFF7A), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x87143759), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x84D48179), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x7A916EE9), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x80C277EB), new(null, typeof(float)) },
            { new(0x9BD2B975, 0x833B81B2), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xC978CC1C), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xC6903950), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xC7AD8A63), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xC5E15743), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xBF0AC069), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xC41759B1), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xC4A44419), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xBE1BBCAD), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xBD2ED957), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xBD9A1CCD), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xBA097275), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xB41F263E), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xB58687D2), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xB6F6601C), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xED5E15B6), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xE0BA07FA), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xE1B7A252), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xCD81C34B), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xCBA576F8), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xCC74DAD8), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xCD25C380), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xF577DA5F), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xF14855BC), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xF1E93440), new(null, typeof(Crc)) },
            { new(0x9BD2B975, 0xFA3C7DCB), new(null, typeof(float)) },
            { new(0x9BD2B975, 0xFE2A72E3), new(null, typeof(float)) },

            // GroupCameraSettings
            { new(0x175F15E4, 0x6C9D228B), new(null, typeof(float)) },
            { new(0x175F15E4, 0x28AE3225), new(null, typeof(FloatVector3)) },
            { new(0x175F15E4, 0x6A0804C0), new(null, typeof(float)) },
            { new(0x175F15E4, 0x88533AA0), new(null, typeof(float)) },

            // GroupTransition
            { new(0xAAA2D2A7, 0xEB4DFB24), new(null, typeof(Crc)) },

            // GunnerSeat
            { new(0x1A8CEE3F, 0x6CE6C69A), new(null, typeof(FloatVector2)) },
            { new(0x1A8CEE3F, 0x6C36BA37), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x699D6C8F), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x5D348D7A), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x64F9B346), new(null, typeof(byte)) },
            { new(0x1A8CEE3F, 0x585F7DB9), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x536AF4EA), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x55B62690), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x511EDE3F), new(null, typeof(int)) },
            { new(0x1A8CEE3F, 0x3E2755DD), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x4E77E9BD), new(null, typeof(FloatVector2)) },
            { new(0x1A8CEE3F, 0x28EC4C13), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x27B2EBEE), new(null, typeof(bool)) },
            { new(0x1A8CEE3F, 0x25CA9693), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x1B9993D5), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x215F258E), new(null, typeof(int)) },
            { new(0x1A8CEE3F, 0x1B64EFAD), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x1A080E0B), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x1A446AB1), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x049038EA), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x0107C0D3), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x027FAADE), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x7AACB067), new(null, typeof(int)) },
            { new(0x1A8CEE3F, 0x6EBF2C31), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xCD8D678E), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xBBDA869E), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xC047D9A5), new(null, typeof(int)) },
            { new(0x1A8CEE3F, 0xB0C2D055), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xAB971252), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xAED17721), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xA1CC5E48), new(null, typeof(int)) },
            { new(0x1A8CEE3F, 0x7E95E06C), new(null, typeof(Crc)) },
            { new(0x1A8CEE3F, 0x81DE132F), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x952F679E), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xDFD6597A), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xDF6B6D8D), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xD8F11E45), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xCEFCD0D9), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xCF9A64E9), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xEABA3AEF), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xF79298AE), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0xFB9E20AE), new(null, typeof(float)) },
            { new(0x1A8CEE3F, 0x31E1A9B0), new(null, typeof(Crc)) },

            // HealthEffectFilter

            // Highlight
            { new(0x1CC87777, 0xEB42BD07), new(null, typeof(int)) },
            { new(0x1CC87777, 0x64CD994E), new(null, typeof(Crc)) },
            { new(0x1CC87777, 0xDED701DD), new(null, typeof(bool)) },
            { new(0x1CC87777, 0x9DF1F45D), new(null, typeof(bool)) },
            { new(0x1CC87777, 0x2B6FE1AE), new(null, typeof(float)) },
            { new(0x1CC87777, 0xD2D52E54), new(null, typeof(float)) },
            { new(0x1CC87777, 0x674AE58A), new(null, typeof(float)) },
            { new(0x1CC87777, 0x22F2C045), new(null, typeof(float)) },
            { new(0x1CC87777, 0x43B43E44), new(null, typeof(Color)) },
            { new(0x1CC87777, 0x09CA3AD5), new(null, typeof(float)) },
            { new(0x1CC87777, 0x02CF30C4), new(null, typeof(Color)) },
            { new(0x1CC87777, 0x0952A055), new(null, typeof(float)) },
            { new(0x1CC87777, 0x9CCC519B), new(null, typeof(Color)) },
            { new(0x1CC87777, 0x67C2800A), new(null, typeof(float)) },
            { new(0x1CC87777, 0x9469454A), new(null, typeof(float)) },
            { new(0x1CC87777, 0xC0EB6A3A), new(null, typeof(float)) },
            { new(0x1CC87777, 0xDDB15F1B), new(null, typeof(Color)) },
            { new(0x1CC87777, 0xE823F441), new(null, typeof(float)) },
            { new(0x1CC87777, 0x661C6BD6), new(null, typeof(float)) },

            // Human
            { new(0xAD431BF0, 0x7E4E77E0), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x7D95D29D), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x7E05C01E), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xD5B7F2AF), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xC25EF593), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0xBCAF259A), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0xBB0C02D2), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xB84A3449), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xBA16485F), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xC43B697D), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xD2A2EC4D), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0xD58593A2), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xE4DC3EC5), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xE87F62EB), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xF15083D6), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xE3E53FE0), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0xE3982DED), new(null, typeof(float)) },
            { new(0xAD431BF0, 0xE04F2AA1), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xD62AB744), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xD930020E), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0xB4035D48), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xAD02C909), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0xA9D8589B), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0xA66C0F96), new(null, typeof(float)) },
            { new(0xAD431BF0, 0xA1EB4A60), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xA61D707E), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xB1170EEE), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xB1307170), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0xB33D5C94), new(null, typeof(int)) },
            { new(0xAD431BF0, 0xA0315577), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x8D2EEDB7), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x88277056), new(null, typeof(int)) },
            { new(0xAD431BF0, 0x8571DB18), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x7FD9A169), new(null, typeof(float)) },
            { new(0xAD431BF0, 0x84FF2AB2), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x9F7E509D), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x92408EE1), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x8FDD5479), new(null, typeof(int)) },
            { new(0xAD431BF0, 0x9196CE7D), new(null, typeof(float)) },
            { new(0xAD431BF0, 0x7A052B7C), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x79473CAE), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x790553C3), new(null, typeof(float)) },
            { new(0xAD431BF0, 0x787C0871), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x789946E1), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x771C9E1F), new(null, typeof(string)) },
            { new(0xAD431BF0, 0x74E5FB06), new(null, typeof(int)) },
            { new(0xAD431BF0, 0x735FED0A), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x6FDC0334), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x71798A24), new(null, typeof(int)) },
            { new(0xAD431BF0, 0x6D63FBFA), new(null, typeof(float)) },
            { new(0xAD431BF0, 0x6A080FC9), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x69D8EF02), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x66F90769), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x6755404E), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x63F4B09E), new(null, typeof(int)) },
            { new(0xAD431BF0, 0x60AF9818), new(null, typeof(byte)) },
            { new(0xAD431BF0, 0x5218EB52), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x48330E09), new(null, typeof(float)) },
            { new(0xAD431BF0, 0x4C1FEF5B), new(null, typeof(int)) },
            { new(0xAD431BF0, 0x4700FEE0), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x3E5E2614), new(null, typeof(int)) },
            { new(0xAD431BF0, 0x377E4246), new(null, null) },
            { new(0xAD431BF0, 0x37A088D3), new(null, typeof(float)) },
            { new(0xAD431BF0, 0x43F1B4A2), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x37562519), new(null, typeof(float)) },
            { new(0xAD431BF0, 0x2A222281), new(null, null) },
            { new(0xAD431BF0, 0x2ADDC0F9), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x2EFB7E33), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x2FB8840A), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x29C2A39C), new(null, typeof(int)) },
            { new(0xAD431BF0, 0x0C0174DA), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x0B03B982), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x0B7F6BF2), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x0A9CD9E0), new(null, typeof(float)) },
            { new(0xAD431BF0, 0x061397DE), new(null, null) },
            { new(0xAD431BF0, 0x063C7CE7), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x1E89A51C), new(null, typeof(bool)) },
            { new(0xAD431BF0, 0x187A572D), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x14A81A29), new(null, typeof(Crc)) },
            { new(0xAD431BF0, 0x16E3526A), new(null, typeof(Crc)) },

            // HumanBodyPart
            { new(0x52EF7807, 0x5B724250), new(null, typeof(Crc)) },
            { new(0x52EF7807, 0x331CDBFD), new(null, typeof(Crc)) },
            { new(0x52EF7807, 0x3FF4F976), new(null, typeof(Color)) },
            { new(0x52EF7807, 0xD6A5096B), new(null, typeof(float)) },
            { new(0x52EF7807, 0x1CBA8055), new(null, typeof(Crc)) },
            { new(0x52EF7807, 0x7B7EDB56), new(null, typeof(int)) },
            { new(0x52EF7807, 0xD6D31661), new(null, typeof(int)) },

            // HumanBodySetup
            { new(0x4DED4763, 0x7D02A1B6), new(null, typeof(FloatVector2)) },
            { new(0x4DED4763, 0xA1757F0A), new(null, typeof(Crc)) },
            { new(0x4DED4763, 0x622DD842), new(null, typeof(Crc)) },
            { new(0x4DED4763, 0xED8FC40C), new(null, typeof(float)) },
            { new(0x4DED4763, 0xD594DB5B), new(null, typeof(float)) },
            { new(0x4DED4763, 0x2BD6448A), new(null, typeof(Crc)) },
            { new(0x4DED4763, 0xC57D2B43), new(null, typeof(Crc)) },
            { new(0x4DED4763, 0x5E2436B9), new(null, typeof(int)) },
            { new(0x4DED4763, 0x44C27FCB), new(null, typeof(int)) },
            { new(0x4DED4763, 0xBEE9A66D), new(null, typeof(int)) },
            { new(0x4DED4763, 0x2A485698), new(null, typeof(int)) },
            { new(0x4DED4763, 0x9E288655), new(null, typeof(int)) },
            { new(0x4DED4763, 0xFC20CB8A), new(null, typeof(int)) },
            { new(0x4DED4763, 0x1E233FA7), new(null, typeof(int)) },

            // HumanPhysics
            { new(0xCB248263, 0xCB248263), new(null, typeof(Crc)) },
            { new(0xCB248263, 0x725A3658), new(null, typeof(bool)) },

            // HumanSkeletonScale
            { new(0x5822D085, 0x5A8132A3), new(null, typeof(float)) },
            { new(0x5822D085, 0x69435911), new(null, typeof(float)) },
            { new(0x5822D085, 0xC5C50C7F), new(null, typeof(float)) },
            { new(0x5822D085, 0x5B724250), new(null, typeof(Crc)) },
            { new(0x5822D085, 0x1598D00C), new(null, typeof(int)) },

            // ImageFolder

            // Item
            { new(0x71798A24, 0x953C60F5), new(null, typeof(float)) },
            { new(0x71798A24, 0x8303620E), new(null, typeof(int)) },
            { new(0x71798A24, 0x75060ED9), new(null, typeof(bool)) },
            { new(0x71798A24, 0x6ADE730A), new(null, typeof(Crc)) },
            { new(0x71798A24, 0x70785B2A), new(null, typeof(bool)) },
            { new(0x71798A24, 0x97AC8632), new(null, typeof(int)) },
            { new(0x71798A24, 0xC6676245), new(null, typeof(int)) },
            { new(0x71798A24, 0xF7CEEE0B), new(null, typeof(bool)) },
            { new(0x71798A24, 0x650A47ED), new(null, typeof(bool)) },
            { new(0x71798A24, 0x2D1BCD5A), new(null, typeof(Crc)) },
            { new(0x71798A24, 0x5B724250), new(null, typeof(int)) },
            { new(0x71798A24, 0x5D4608B5), new(null, typeof(bool)) },
            { new(0x71798A24, 0x1E757409), new(null, typeof(Crc)) },
            { new(0x71798A24, 0x13F47172), new(null, typeof(bool)) },
            { new(0x71798A24, 0x0F59CD06), new(null, typeof(Crc)) },
            { new(0x71798A24, 0x06DA8775), new(null, typeof(Crc)) },
            { new(0x71798A24, 0x0D111AB7), new(null, typeof(Crc)) },

            // ItemCache
            { new(0x50C4254A, 0x97AC8632), new(null, typeof(Crc)) },
            { new(0x50C4254A, 0x97EE12C6), new(null, typeof(bool)) },

            // LeafSpawner
            { new(0x32B6B82F, 0x14E198F5), new(null, typeof(Crc)) },
            { new(0x32B6B82F, 0x354694DB), new(null, typeof(float)) },
            { new(0x32B6B82F, 0x9DB73D8F), new(null, typeof(float)) },

            // LightAttachement
            { new(0xA3613F4D, 0xC25641DF), new(null, typeof(int)) },
            { new(0xA3613F4D, 0x9AB4F3ED), new(null, typeof(int)) },
            { new(0xA3613F4D, 0xA3613F4D), new(null, typeof(int)) },
            { new(0xA3613F4D, 0x8ED97E12), new(null, typeof(float)) },
            { new(0xA3613F4D, 0x6C9D982B), new(null, typeof(int)) },
            { new(0xA3613F4D, 0x81EB0E4F), new(null, typeof(float)) },
            { new(0xA3613F4D, 0xA873293A), new(null, typeof(int)) },

            // LightHalo
            { new(0x438C44F5, 0xCB7BA603), new(null, typeof(float)) },
            { new(0x438C44F5, 0xC5B44A23), new(null, typeof(float)) },
            { new(0x438C44F5, 0xB93F2EFB), new(null, typeof(byte)) },
            { new(0x438C44F5, 0xAD13EC8C), new(null, typeof(float)) },
            { new(0x438C44F5, 0xAEC1569B), new(null, typeof(float)) },
            { new(0x438C44F5, 0x927A8AE2), new(null, typeof(float)) },
            { new(0x438C44F5, 0x905B3312), new(null, typeof(float)) },
            { new(0x438C44F5, 0x917335AD), new(null, typeof(float)) },
            { new(0x438C44F5, 0x8ACA5BCA), new(null, typeof(float)) },
            { new(0x438C44F5, 0x772B0B2F), new(null, typeof(float)) },
            { new(0x438C44F5, 0x8978B4C4), new(null, typeof(Color)) },
            { new(0x438C44F5, 0xE7F46F43), new(null, typeof(float)) },
            { new(0x438C44F5, 0xE30917DE), new(null, typeof(Crc)) },
            { new(0x438C44F5, 0xE1AE04B8), new(null, typeof(float)) },
            { new(0x438C44F5, 0xD0822FCD), new(null, typeof(Color)) },
            { new(0x438C44F5, 0xD3D07581), new(null, typeof(float)) },
            { new(0x438C44F5, 0xFCC5D7DE), new(null, typeof(float)) },
            { new(0x438C44F5, 0xED3DCB8A), new(null, typeof(float)) },
            { new(0x438C44F5, 0xF011157A), new(null, typeof(Crc)) },
            { new(0x438C44F5, 0xFF8ABD17), new(null, typeof(byte)) },
            { new(0x438C44F5, 0x7702A37E), new(null, typeof(float)) },
            { new(0x438C44F5, 0x76E3E09E), new(null, typeof(float)) },
            { new(0x438C44F5, 0x753DC701), new(null, typeof(float)) },
            { new(0x438C44F5, 0x7186F0B7), new(null, typeof(float)) },
            { new(0x438C44F5, 0x74E42872), new(null, typeof(float)) },
            { new(0x438C44F5, 0x6BED3D76), new(null, typeof(Crc)) },
            { new(0x438C44F5, 0x67F3F28B), new(null, typeof(byte)) },
            { new(0x438C44F5, 0x6302F1CC), new(null, typeof(byte)) },
            { new(0x438C44F5, 0x3FF4F976), new(null, typeof(Color)) },
            { new(0x438C44F5, 0x5D71589D), new(null, typeof(Color)) },
            { new(0x438C44F5, 0x354694DB), new(null, typeof(float)) },
            { new(0x438C44F5, 0x26F78C7B), new(null, typeof(float)) },
            { new(0x438C44F5, 0x1F89A84E), new(null, typeof(Color)) },
            { new(0x438C44F5, 0x21B7E323), new(null, typeof(float)) },
            { new(0x438C44F5, 0x147A3E5D), new(null, typeof(byte)) },
            { new(0x438C44F5, 0x0A91041C), new(null, typeof(Color)) },
            { new(0x438C44F5, 0x0DEC56C7), new(null, typeof(int)) },
            { new(0x438C44F5, 0x2FD9B489), new(null, typeof(float)) },
            { new(0x438C44F5, 0x27B412FF), new(null, typeof(float)) },
            { new(0x438C44F5, 0x280BF82C), new(null, typeof(float)) },
            { new(0x438C44F5, 0x3229D516), new(null, typeof(Crc)) },
            { new(0x438C44F5, 0x4D1DD4D1), new(null, typeof(float)) },

            // LightHaloAttachment
            { new(0x0B213D8E, 0x54F0B858), new(null, typeof(float)) },
            { new(0x0B213D8E, 0xB4A80855), new(null, typeof(int)) },
            { new(0x0B213D8E, 0xF80ABFB7), new(null, typeof(int)) },
            { new(0x0B213D8E, 0x438C44F5), new(null, typeof(int)) },
            { new(0x0B213D8E, 0x0B213D8E), new(null, typeof(int)) },
            { new(0x0B213D8E, 0x1C128785), new(null, typeof(float)) },
            { new(0x0B213D8E, 0x2A8F3B51), new(null, typeof(int)) },

            // LightSettings
            { new(0x7A55C8F8, 0x76308672), new(null, typeof(byte)) },
            { new(0x7A55C8F8, 0x456A2D80), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x3E6F5C10), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0x3FF4F976), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0x38D0AD05), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x32E060B4), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0x354694DB), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x35B6696C), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x2CFA7B6B), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x15667836), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x0033984F), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0x0EC38247), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0x0F2823AC), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x1C41B160), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x2BF2D35E), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x69282DF5), new(null, typeof(byte)) },
            { new(0x7A55C8F8, 0x5B89B397), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x63A27078), new(null, typeof(byte)) },
            { new(0x7A55C8F8, 0x586891F5), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x4BF5F9E3), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0x52FCF5D4), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x566B3F52), new(null, typeof(int)) },
            { new(0x7A55C8F8, 0x73E741E3), new(null, typeof(byte)) },
            { new(0x7A55C8F8, 0x762D7CD9), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x73514F39), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x6BED3D76), new(null, typeof(Crc)) },
            { new(0x7A55C8F8, 0x725EAA9B), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x726047C6), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x7A9D55F3), new(null, typeof(byte)) },
            { new(0x7A55C8F8, 0x76C3FCF2), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0x79AEA90A), new(null, typeof(byte)) },
            { new(0x7A55C8F8, 0xC61EDBD1), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x99327D6F), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x917335AD), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x976E00A1), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x8900B33F), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x7CBBCA09), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0x836D3DFC), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0x85A5B4DD), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xACFFC219), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xA0716ED6), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xACDFC789), new(null, typeof(string)) },
            { new(0x7A55C8F8, 0xAF9F46EA), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xB7CFC8DD), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xFBABFD4C), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xF467D031), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xF7688793), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xFF244A07), new(null, typeof(Crc)) },
            { new(0x7A55C8F8, 0xFF27868E), new(null, typeof(bool)) },
            { new(0x7A55C8F8, 0xF0758D60), new(null, typeof(byte)) },
            { new(0x7A55C8F8, 0xD7DBF28C), new(null, typeof(byte)) },
            { new(0x7A55C8F8, 0xE1AE04B8), new(null, typeof(float)) },
            { new(0x7A55C8F8, 0xD1488AB4), new(null, typeof(int)) },
            { new(0x7A55C8F8, 0xC8AF0E75), new(null, typeof(Color)) },
            { new(0x7A55C8F8, 0xCB7BA603), new(null, typeof(float)) },

            // LightVolume
            { new(0xD3DFD699, 0x7D90365D), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x782741C7), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x69AB9F30), new(null, typeof(bool)) },
            { new(0xD3DFD699, 0x46CCAA90), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x6086BEE4), new(null, typeof(bool)) },
            { new(0xD3DFD699, 0x353B440D), new(null, typeof(FloatVector2)) },
            { new(0xD3DFD699, 0x2DDA9E53), new(null, typeof(bool)) },
            { new(0xD3DFD699, 0x3484922D), new(null, typeof(FloatVector2)) },
            { new(0xD3DFD699, 0x1CC87777), new(null, typeof(Crc)) },
            { new(0xD3DFD699, 0x0BDC2B41), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x1098118E), new(null, typeof(float)) },
            { new(0xD3DFD699, 0xB333BBA2), new(null, typeof(FloatVector2)) },
            { new(0xD3DFD699, 0xB258547F), new(null, typeof(FloatVector3)) },
            { new(0xD3DFD699, 0xB27D09C2), new(null, typeof(FloatVector2)) },
            { new(0xD3DFD699, 0xB1AC0908), new(null, typeof(FloatVector2)) },
            { new(0xD3DFD699, 0x7E3618EC), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x82E72ED1), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x99A76612), new(null, typeof(FloatVector2)) },
            { new(0xD3DFD699, 0xD8A0A867), new(null, typeof(Crc)) },
            { new(0xD3DFD699, 0xBBA9DA2F), new(null, typeof(FloatVector2)) },
            { new(0xD3DFD699, 0xCEC7707A), new(null, typeof(float)) },
            { new(0xD3DFD699, 0xCF2FE183), new(null, typeof(float)) },
            { new(0xD3DFD699, 0xEBD77BB3), new(null, typeof(float)) },
            { new(0xD3DFD699, 0xFB88ADF2), new(null, typeof(float)) },
            // LightVolume - sub_970FA0
            { new(0xD3DFD699, 0x6BCD7814), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x3AD49195), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x03BCDA2A), new(null, typeof(Color)) },
            { new(0xD3DFD699, 0x1F89A84E), new(null, typeof(Color)) },
            { new(0xD3DFD699, 0x66C720F2), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x8C8BD14A), new(null, typeof(Color)) },
            { new(0xD3DFD699, 0xB1B3B678), new(null, typeof(float)) },
            { new(0xD3DFD699, 0xEDC59AF4), new(null, typeof(float)) },
            { new(0xD3DFD699, 0x622DA6EE), new(null, typeof(Crc)) },

            // Melee
            { new(0x0A9A5883, 0x5AAB6EAD), new(null, typeof(FloatVector2)) },
            { new(0x0A9A5883, 0x30BE46C0), new(null, typeof(float)) },
            { new(0x0A9A5883, 0x3136E3E8), new(null, typeof(float)) },
            { new(0x0A9A5883, 0x1A1FE631), new(null, typeof(Crc)) },
            { new(0x0A9A5883, 0x0248C942), new(null, typeof(FloatVector2)) },
            { new(0x0A9A5883, 0x199C9C74), new(null, typeof(float)) },
            { new(0x0A9A5883, 0xAA2917AB), new(null, typeof(bool)) },
            { new(0x0A9A5883, 0x9FD0A9F5), new(null, typeof(Crc)) },
            { new(0x0A9A5883, 0x9FD572BB), new(null, typeof(float)) },
            { new(0x0A9A5883, 0x8ADB7BC5), new(null, typeof(float)) },
            { new(0x0A9A5883, 0x6ED40AE1), new(null, typeof(float)) },
            { new(0x0A9A5883, 0x864A458B), new(null, typeof(FloatVector2)) },
            { new(0x0A9A5883, 0xCF5205EA), new(null, typeof(float)) },
            { new(0x0A9A5883, 0xAF867032), new(null, typeof(bool)) },
            { new(0x0A9A5883, 0xBB81AE95), new(null, typeof(float)) },
            { new(0x0A9A5883, 0xE04FC10A), new(null, typeof(float)) },
            { new(0x0A9A5883, 0xE4091E54), new(null, typeof(float)) },
            { new(0x0A9A5883, 0x723CFFB0), new(null, typeof(float)) },
            { new(0x0A9A5883, 0xAED9B020), new(null, typeof(float)) },

            // MeleeWeapon
            { new(0x07370953, 0x47B9DF6F), new(null, typeof(int)) },
            { new(0x07370953, 0x2543907B), new(null, typeof(bool)) },
            { new(0x07370953, 0xAE1ED17F), new(null, typeof(bool)) },
            { new(0x07370953, 0xD1DD6599), new(null, typeof(Crc)) },
            { new(0x07370953, 0xE7F9EA87), new(null, typeof(float)) },
            { new(0x07370953, 0xF535722A), new(null, typeof(Crc)) },

            // MiniGame
            { new(0xBBDF8DF6, 0x4FEAEE29), new(null, typeof(FloatVector3)) },
            { new(0xBBDF8DF6, 0x493400CD), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x4EDD8F8D), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0x35B4A4E2), new(null, typeof(float)) },
            { new(0xBBDF8DF6, 0x2C2E04F2), new(null, typeof(int)) },
            { new(0xBBDF8DF6, 0x2F28928B), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x344BEE18), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0x279D4B62), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x0CC154BC), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x0148B27F), new(null, typeof(FloatVector2)) },
            { new(0xBBDF8DF6, 0x040245EA), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0x0429A59B), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x0FB423B0), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x1BF1DDE7), new(null, typeof(float)) },
            { new(0xBBDF8DF6, 0xC76DDBF7), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0xA8DE63D6), new(null, typeof(float)) },
            { new(0xBBDF8DF6, 0x8D9B17F3), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x71AFA04E), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x864AC31B), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0x98625B98), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0x9B78D64E), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0xBDF3DEAE), new(null, typeof(float)) },
            { new(0xBBDF8DF6, 0xAE865FE0), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0xB66BEB74), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0xC55323E7), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0xC6F1C2E3), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0xE832FB02), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0xE4FB3946), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0xE25D4494), new(null, typeof(float)) },
            { new(0xBBDF8DF6, 0xE2612F04), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0xE5D20954), new(null, typeof(FloatVector2)) },
            { new(0xBBDF8DF6, 0xE60F34C5), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0xF4EEC45F), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0xEE032533), new(null, typeof(bool)) },
            { new(0xBBDF8DF6, 0xEF03C774), new(null, typeof(Crc)) },
            { new(0xBBDF8DF6, 0xFE015046), new(null, typeof(bool)) },

            // ModelRenderable
            { new(0x38D88BB8, 0x5B724250), new(null, typeof(Crc)) },
            { new(0x38D88BB8, 0xAE1ED17F), new(null, typeof(bool)) },

            // Ordnance
            { new(0x277B8B55, 0x58D3A5D8), new(null, typeof(Crc)) },
            { new(0x277B8B55, 0x336E0ADA), new(null, typeof(float)) },
            { new(0x277B8B55, 0x33E229EA), new(null, typeof(float)) },
            { new(0x277B8B55, 0x2D7DD5DB), new(null, typeof(Crc)) },
            { new(0x277B8B55, 0x01807ACA), new(null, typeof(Crc)) },
            { new(0x277B8B55, 0x0B33F994), new(null, typeof(Crc)) },
            { new(0x277B8B55, 0x28C035AC), new(null, typeof(Crc)) },
            { new(0x277B8B55, 0xD6663C5E), new(null, typeof(Crc)) },
            { new(0x277B8B55, 0xD2D52E54), new(null, typeof(float)) },
            { new(0x277B8B55, 0xD27E3887), new(null, typeof(Crc)) },
            { new(0x277B8B55, 0x6945C955), new(null, typeof(Crc)) },
            { new(0x277B8B55, 0x903BB20C), new(null, typeof(float)) },
            { new(0x277B8B55, 0xF251F970), new(null, typeof(float)) },
            { new(0x277B8B55, 0xE9DBFBD0), new(null, typeof(float)) },
            { new(0x277B8B55, 0xEFF83A31), new(null, typeof(FloatVector2)) },
            { new(0x277B8B55, 0xF4434ADE), new(null, typeof(float)) },
            { new(0x277B8B55, 0xBF4C7045), new(null, typeof(int)) },

            // ParticleEffect
            { new(0x4B19B750, 0x8479BBE5), new(null, typeof(int)) },
            { new(0x4B19B750, 0x848096A5), new(null, typeof(string)) },
            { new(0x4B19B750, 0x3B07AD90), new(null, typeof(int)) },
            { new(0x4B19B750, 0x1870458B), new(null, typeof(int)) },
            { new(0x4B19B750, 0x37077436), new(null, typeof(Crc)) },

            // ParticleEffectSpawner
            { new(0x9DCC91D0, 0x6633CC0A), new(null, typeof(Crc)) },
            { new(0x9DCC91D0, 0x4B19B750), new(null, typeof(Crc)) },
            { new(0x9DCC91D0, 0xD43DE771), new(null, typeof(FloatVector2)) },
            { new(0x9DCC91D0, 0xD2D52E54), new(null, typeof(float)) },
            { new(0x9DCC91D0, 0xA4C1B1C3), new(null, typeof(Crc)) },
            { new(0x9DCC91D0, 0xC8EA585D), new(null, typeof(int)) },

            // PerkFactors
            { new(0xBDAA3FB7, 0x75522F59), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x6447FB46), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x6F15FBE7), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x7387DF05), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x62BCEC63), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x4AD025E7), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x459DEE09), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0x43183CB4), new(null, typeof(int)) },
            { new(0xBDAA3FB7, 0x44FA72C8), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0x3BD59983), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0x3B650848), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0x38C95B83), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x2E2BA7C4), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x361DFE33), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0x2B5E9FBF), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x22D75E37), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x07153895), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x00AB9A20), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0x05A9A969), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xD4279D41), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xC9519C52), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xCCDD79CF), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xCF1AB351), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xC22609B3), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xA7D41104), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xA83E5FCB), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xA8E66F6B), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xA21776AC), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xA06B069D), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xA0AF0C71), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xA1D01048), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x9CFA471A), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0x795B115A), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0x968646DF), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0x9C7B628A), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xE6A76EDD), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xE505C244), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xE60187CB), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xE69A8743), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xE2669BE2), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xD653701A), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xDC9E11A2), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xDFA39497), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xFA3B5F67), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xEEA4FEF6), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xEF42C1C8), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xF0BB8F39), new(null, typeof(float)) },
            { new(0xBDAA3FB7, 0xFB10C187), new(null, typeof(Crc)) },
            { new(0xBDAA3FB7, 0xFD96D0B6), new(null, typeof(Crc)) },

            // Perks
            { new(0xA8CF8D40, 0x6EC8D450), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0x613BBB29), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0x23B995CA), new(null, typeof(int)) },
            { new(0xA8CF8D40, 0x4F1B0BC9), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0x6349214F), new(null, typeof(int)) },
            { new(0xA8CF8D40, 0x68707986), new(null, null) },
            { new(0xA8CF8D40, 0xC9709CBC), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0xB24A0D49), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0xB1FD379F), new(null, null) },
            { new(0xA8CF8D40, 0xAD3330EF), new(null, typeof(bool)) },
            { new(0xA8CF8D40, 0xB0F94B6E), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0x9D4695F9), new(null, typeof(int)) },
            { new(0xA8CF8D40, 0x8D8DE708), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0x8D3C0B47), new(null, typeof(int)) },
            { new(0xA8CF8D40, 0x77ED2684), new(null, typeof(int)) },
            { new(0xA8CF8D40, 0x832ED2D8), new(null, typeof(int)) },
            { new(0xA8CF8D40, 0xE8DA4DD0), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0xEA588FB9), new(null, typeof(float)) },
            { new(0xA8CF8D40, 0xFE8DD9CC), new(null, typeof(bool)) },
            { new(0xA8CF8D40, 0xE5943224), new(null, typeof(int)) },
            { new(0xA8CF8D40, 0xE1BA3193), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0xDEE6B121), new(null, typeof(Crc)) },
            { new(0xA8CF8D40, 0xCD0B7C7A), new(null, typeof(int)) },
            { new(0xA8CF8D40, 0xDB67DD79), new(null, typeof(int)) },

            // PhysicalOrdnance
            { new(0x284C8026, 0xC5A6FA42), new(null, typeof(float)) },
            { new(0x284C8026, 0x7BF01635), new(null, typeof(bool)) },
            { new(0x284C8026, 0x7FD9A169), new(null, typeof(float)) },
            { new(0x284C8026, 0x97AC8632), new(null, typeof(Crc)) },
            { new(0x284C8026, 0xC87F672A), new(null, typeof(Crc)) },
            { new(0x284C8026, 0xCBE8ED58), new(null, typeof(float)) },
            { new(0x284C8026, 0xE52E6A81), new(null, typeof(float)) },
            { new(0x284C8026, 0x7BE5AA43), new(null, typeof(float)) },
            { new(0x284C8026, 0x58B8D4B3), new(null, typeof(float)) },
            { new(0x284C8026, 0x4E03A742), new(null, typeof(FloatVector2)) },
            { new(0x284C8026, 0x354694DB), new(null, typeof(float)) },
            { new(0x284C8026, 0x05D171EA), new(null, typeof(float)) },
            { new(0x284C8026, 0x16930AFE), new(null, typeof(Crc)) },
            { new(0x284C8026, 0x5B724250), new(null, typeof(Crc)) },
            { new(0x284C8026, 0x6ADE730A), new(null, typeof(Crc)) },
            { new(0x284C8026, 0x6D93689C), new(null, typeof(float)) },
            { new(0x284C8026, 0x2D595F06), new(null, typeof(float)) },

            // PhysicsParticle
            { new(0xFC8B1DA2, 0x9C323CFD), new(null, typeof(float)) },
            { new(0xFC8B1DA2, 0x5B724250), new(null, typeof(Crc)) },
            { new(0xFC8B1DA2, 0x7BE5AA43), new(null, typeof(float)) },
            { new(0xFC8B1DA2, 0xADAE06C3), new(null, typeof(byte)) },
            { new(0xFC8B1DA2, 0x5608BD5A), new(null, typeof(Crc)) },
            { new(0xFC8B1DA2, 0x50F8BFFA), new(null, typeof(byte)) },
            { new(0xFC8B1DA2, 0x46EB60BF), new(null, typeof(float)) },
            { new(0xFC8B1DA2, 0x2E978372), new(null, typeof(Crc)) },
            { new(0xFC8B1DA2, 0x3904F4E4), new(null, typeof(int)) },
            { new(0xFC8B1DA2, 0xC3AEB321), new(null, typeof(float)) },
            { new(0xFC8B1DA2, 0xC6FF3C76), new(null, typeof(int)) },

            // PhysicsParticleSet
            { new(0xC30934DA, 0xB03F89C9), new(null, typeof(FloatVector2)) },
            { new(0xC30934DA, 0x9C323CFD), new(null, typeof(float)) },
            { new(0xC30934DA, 0xA26C3D31), new(null, typeof(Crc)) },
            { new(0xC30934DA, 0x95A91C36), new(null, typeof(FloatVector2)) },
            { new(0xC30934DA, 0x7DDC5783), new(null, typeof(float)) },
            { new(0xC30934DA, 0x91C23C14), new(null, typeof(FloatVector2)) },
            { new(0xC30934DA, 0xDD6A4A63), new(null, typeof(FloatVector2)) },
            { new(0xC30934DA, 0xC6E3EED3), new(null, typeof(FloatVector2)) },
            { new(0xC30934DA, 0xC877BD3B), new(null, typeof(FloatVector3_2)) },
            { new(0xC30934DA, 0xE7D5FF9D), new(null, typeof(FloatVector3)) },
            { new(0xC30934DA, 0xEEFE4FBF), new(null, typeof(float)) },
            { new(0xC30934DA, 0x59A23D57), new(null, typeof(int)) },
            { new(0xC30934DA, 0x1870458B), new(null, typeof(Crc)) },
            { new(0xC30934DA, 0x1CDE14DB), new(null, typeof(float)) },

            // PhysicsVehicle
            { new(0x0E6FC89C, 0xBF667004), new(null, typeof(Crc)) },
            { new(0x0E6FC89C, 0xA5F489DC), new(null, typeof(Crc)) },
            { new(0x0E6FC89C, 0x0A1ED360), new(null, typeof(float)) },
            { new(0x0E6FC89C, 0x763BB595), new(null, typeof(int)) },

            // PhysicsVehicleWheel
            { new(0x5B71D0F5, 0x31050AC4), new(null, typeof(Crc)) },
            { new(0x5B71D0F5, 0x0F132DD1), new(null, typeof(byte)) },
            { new(0x5B71D0F5, 0x2FC58DC9), new(null, typeof(int)) },
            { new(0x5B71D0F5, 0xA54722AC), new(null, typeof(float)) },
            { new(0x5B71D0F5, 0x8C0CB944), new(null, typeof(float)) },
            { new(0x5B71D0F5, 0x8DB64FA5), new(null, typeof(FloatVector2)) },
            { new(0x5B71D0F5, 0x9AE508A0), new(null, typeof(float)) },
            { new(0x5B71D0F5, 0x8AC02299), new(null, typeof(float)) },
            { new(0x5B71D0F5, 0x3B3B5B97), new(null, typeof(FloatVector2)) },
            { new(0x5B71D0F5, 0x85850491), new(null, typeof(float)) },
            { new(0x5B71D0F5, 0x873E5AC6), new(null, typeof(Crc)) },
            { new(0x5B71D0F5, 0xC6CD5F80), new(null, typeof(float)) },
            { new(0x5B71D0F5, 0xB8739CD5), new(null, typeof(Crc)) },
            { new(0x5B71D0F5, 0xB8A537BB), new(null, typeof(float)) },
            { new(0x5B71D0F5, 0xBF5E0AAC), new(null, typeof(byte)) },
            { new(0x5B71D0F5, 0xDAF1FC98), new(null, typeof(Crc)) },
            { new(0x5B71D0F5, 0xFEE0AC21), new(null, typeof(float)) },
            { new(0x5B71D0F5, 0x7C48323F), new(null, typeof(int)) },

            // Player
            { new(0x600E3B1E, 0x7E049E2A), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x6D696EE4), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x7C9C3FCA), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x69C018DD), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x393D8EE5), new(null, typeof(bool)) },
            { new(0x600E3B1E, 0x6212644C), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x37A1DA8A), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x24DF19FC), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x3016F5BF), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x245FCBA5), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x01D6CA73), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x06BE1ABF), new(null, typeof(Crc)) },
            { new(0x600E3B1E, 0x1771697D), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xD55D3ED5), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xC4B9F2B0), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xBA71C11C), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xBB64C692), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xC2E499FB), new(null, typeof(bool)) },
            { new(0x600E3B1E, 0xC8CF19B8), new(null, typeof(bool)) },
            { new(0x600E3B1E, 0xC98A63C7), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xF41F8B7C), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xE060C605), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xE7F416D3), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xF2AEAB3E), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xF66B2194), new(null, typeof(Crc)) },
            { new(0x600E3B1E, 0xFAD12345), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xBA56CF32), new(null, typeof(FloatVector2)) },
            { new(0x600E3B1E, 0xAAA9955E), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x9DB45444), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x7EDCA8E7), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x80AB81EB), new(null, typeof(float)) },
            { new(0x600E3B1E, 0x94B6E1DE), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xA86BA949), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xA873A763), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xB2C82EE0), new(null, typeof(FloatVector2)) },
            { new(0x600E3B1E, 0xB80AC9D9), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xB283E894), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xAB0B996F), new(null, typeof(int)) },
            { new(0x600E3B1E, 0xAC6A5E5F), new(null, typeof(float)) },
            { new(0x600E3B1E, 0xAE85A3E9), new(null, typeof(float)) },

            // PlayerCollision
            { new(0x05568616, 0xC0C16868), new(null, typeof(float)) },
            { new(0x05568616, 0x28BBFBF2), new(null, typeof(float)) },
            { new(0x05568616, 0x4ABE700F), new(null, typeof(float)) },
            { new(0x05568616, 0xD5C76750), new(null, typeof(bool)) },

            // Prop
            { new(0xCEB60D96, 0xC900D2CF), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xBED88577), new(null, typeof(float)) },
            { new(0xCEB60D96, 0xC6300246), new(null, typeof(float)) },
            { new(0xCEB60D96, 0xBABCB12D), new(null, null) },
            { new(0xCEB60D96, 0xA7121C17), new(null, typeof(FloatVector3)) },
            { new(0xCEB60D96, 0xAEFF60B8), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xA702DBDE), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xA52E4407), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xA5B32FB8), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x9ECE86A3), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x784B7D7D), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x98BC61FA), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xF25092A4), new(null, typeof(float)) },
            { new(0xCEB60D96, 0xD6CFED23), new(null, typeof(int)) },
            { new(0xCEB60D96, 0xC980220E), new(null, typeof(float)) },
            { new(0xCEB60D96, 0xCCD5561A), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xE4400C70), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xE7BA6724), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0xF89F5CA4), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xF3EAB72D), new(null, typeof(int)) },
            { new(0xCEB60D96, 0xF5572421), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0xFAFAB455), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x7729C979), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x61CC7540), new(null, typeof(FloatVector2)) },
            { new(0xCEB60D96, 0x5AAB9E8A), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x5E36BD15), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x588EBC7F), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x55E4B770), new(null, typeof(float)) },
            { new(0xCEB60D96, 0x5608BD5A), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x6ADE730A), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x62F3FCEB), new(null, typeof(float)) },
            { new(0xCEB60D96, 0x64B9769A), new(null, typeof(float)) },
            { new(0xCEB60D96, 0x725C3533), new(null, typeof(int)) },
            { new(0xCEB60D96, 0x4B5BD522), new(null, typeof(float)) },
            { new(0xCEB60D96, 0x381F4A7E), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x3DCC7355), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x3E495387), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x35EDDEB6), new(null, typeof(float)) },
            { new(0xCEB60D96, 0x302B2084), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x317EDAD4), new(null, typeof(int)) },
            { new(0xCEB60D96, 0x34225FE1), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x3016A5BA), new(null, null) },
            { new(0xCEB60D96, 0x2BA1D21A), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x2C0FD1B3), new(null, typeof(float)) },
            { new(0xCEB60D96, 0x2CE48D05), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x2A40D221), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x2947CB2D), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x22AAEE32), new(null, typeof(FloatVector3)) },
            { new(0xCEB60D96, 0x26CC0A2D), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x2955523D), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x2255030D), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x1DD10731), new(null, typeof(int)) },
            { new(0xCEB60D96, 0x1EDB3A4C), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x21550886), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x1C5D2A70), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x17B66269), new(null, typeof(int)) },
            { new(0xCEB60D96, 0x19A6973A), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x1A62C9AE), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x14282B50), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x0C0DD773), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x0F7CACF8), new(null, typeof(FloatVector2)) },
            { new(0xCEB60D96, 0x0FD41E7F), new(null, typeof(FloatVector3)) },
            { new(0xCEB60D96, 0x0B9BEF11), new(null, typeof(bool)) },
            { new(0xCEB60D96, 0x099A56FA), new(null, typeof(float)) },
            { new(0xCEB60D96, 0x07E20A15), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x002AAF13), new(null, typeof(Crc)) },
            { new(0xCEB60D96, 0x04F50EA4), new(null, typeof(bool)) },

            // RadialBlur
            //{ new(0x72F138CF, ), new(null, typeof()) },

            // RandomObj
            { new(0x75F73A4F, 0xDFAFCDA6), new(null, typeof(int)) },
            { new(0x75F73A4F, 0xF0C7DA5F), new(null, typeof(string)) },
            { new(0x75F73A4F, 0xF8A56E55), new(null, typeof(bool)) },
            { new(0x75F73A4F, 0x7FD9A169), new(null, typeof(float)) },
            { new(0x75F73A4F, 0x01CAB9A5), new(null, typeof(Crc)) },
            { new(0x75F73A4F, 0x0AA3E5D9), new(null, typeof(int)) },
            { new(0x75F73A4F, 0x3EECCF6B), new(null, typeof(Crc)) },
            { new(0x75F73A4F, 0x84F14707), new(null, typeof(Crc)) },

            // Ricochet
            //{ new(0x73393C4A, ), new(null, typeof()) },

            // Rocket
            //{ new(0x5434C7ED, ), new(null, typeof()) },

            // Rumble
            //{ new(0xF49470BA, ), new(null, typeof()) },

            // SabotageTarget
            //{ new(0xC5B4B2B6, ), new(null, typeof()) },

            // ScopeTransition
            //{ new(0x148C19AA, ), new(null, typeof()) },

            // ScriptController
            { new(0x7A6E9D28, 0xE0B01D9F), new(null, typeof(string)) },
            { new(0x7A6E9D28, 0xCA6B9057), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xC072E94A), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0xC4C412CD), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0xBF930289), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xBCFE6314), new(null, typeof(string)) },
            { new(0x7A6E9D28, 0xBE5952B1), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0xAE350719), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0xA3DBF09D), new(null, typeof(bool)) },
            { new(0x7A6E9D28, 0xA77EDAAA), new(null, typeof(bool)) },
            { new(0x7A6E9D28, 0x97EE12C6), new(null, typeof(bool)) },
            { new(0x7A6E9D28, 0x7A2AA5EF), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0x85B5E29F), new(null, typeof(string)) },
            { new(0x7A6E9D28, 0x89A84EF5), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xF4BADFDB), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0xE4E34549), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xF6B4FF0E), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xFE8BAE82), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xDDF1EE0F), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0xD72AA401), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xCACFD6AA), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xCE629E7E), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0xD6F4D903), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0xD730D0E1), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0xD79C2BE9), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0x77776D5A), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0x7154DC63), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0x722C0953), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0x6A9AB174), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0x4177A478), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0x44885A78), new(null, typeof(bool)) },
            { new(0x7A6E9D28, 0x37FC008D), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0x2F1820CC), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0x3570F09D), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0x19BAB4C9), new(null, typeof(float)) },
            { new(0x7A6E9D28, 0x0424DD32), new(null, typeof(bool)) },
            { new(0x7A6E9D28, 0x07B13063), new(null, typeof(int)) },
            { new(0x7A6E9D28, 0x165C1FD7), new(null, typeof(int)) },

            // Searcher
            //{ new(0xB6475758, ), new(null, typeof()) },

            // SearcherSeat
            { new(0xA95AADD7, 0x585F7DB9), new(null, typeof(float)) },
            { new(0xA95AADD7, 0x27B2EBEE), new(null, typeof(bool)) },
            { new(0xA95AADD7, 0x341FFD9C), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xB4E28AAF), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xA419D2AA), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xB0C2D055), new(null, typeof(float)) },
            { new(0xA95AADD7, 0x94207504), new(null, typeof(float)) },
            { new(0xA95AADD7, 0x5D348D7A), new(null, typeof(float)) },
            { new(0xA95AADD7, 0x804D355B), new(null, typeof(float)) },
            { new(0xA95AADD7, 0x81DE132F), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xDFD6597A), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xC8823FF8), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xD3330089), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xE4D991E9), new(null, typeof(int)) },
            { new(0xA95AADD7, 0xFB9E20AE), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xEF40F693), new(null, typeof(Crc)) },
            { new(0xA95AADD7, 0xA60E6230), new(null, typeof(float)) },
            { new(0xA95AADD7, 0xE6468A9E), new(null, typeof(bool)) },
            { new(0xA95AADD7, 0xF67559B9), new(null, typeof(Crc)) },
            { new(0xA95AADD7, 0x70FE02AB), new(null, typeof(Crc)) },
            { new(0xA95AADD7, 0x1BF8088F), new(null, typeof(Crc)) },
            { new(0xA95AADD7, 0x6F14A6AD), new(null, typeof(Crc)) },

            // SearchTurret
            //{ new(0x1F72E643, ), new(null, typeof()) },

            // Seat
            { new(0x10B39BE6, 0x6C9E76F1), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x3BF1CDB6), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x2CAA3B38), new(null, typeof(float)) },
            { new(0x10B39BE6, 0x330537AE), new(null, typeof(byte)) },
            { new(0x10B39BE6, 0x3B645216), new(null, typeof(byte)) },
            { new(0x10B39BE6, 0x137F0C86), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x02F5B417), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x0B3C9AEB), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x11EEDAB0), new(null, typeof(int)) },
            { new(0x10B39BE6, 0x560A68C4), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x5EB18F3E), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x516AA372), new(null, typeof(int)) },
            { new(0x10B39BE6, 0x43E5A73B), new(null, typeof(int)) },
            { new(0x10B39BE6, 0x4A09E56C), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x4C2D60BB), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xB8EE1BA8), new(null, typeof(int)) },
            { new(0x10B39BE6, 0xB2EF75E8), new(null, typeof(int)) },
            { new(0x10B39BE6, 0xB838A7E9), new(null, typeof(FloatVector2)) },
            { new(0x10B39BE6, 0xB2E3212D), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xAC256817), new(null, typeof(byte)) },
            { new(0x10B39BE6, 0xAC2CF95E), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xB1113237), new(null, typeof(float)) },
            { new(0x10B39BE6, 0xA7642F51), new(null, typeof(float)) },
            { new(0x10B39BE6, 0xA2ECF38A), new(null, typeof(float)) },
            { new(0x10B39BE6, 0x9EB6A878), new(null, typeof(float)) },
            { new(0x10B39BE6, 0x9D66FD52), new(null, typeof(byte)) },
            { new(0x10B39BE6, 0x99360455), new(null, typeof(int)) },
            { new(0x10B39BE6, 0x7FA0EFE1), new(null, typeof(float)) },
            { new(0x10B39BE6, 0x8B109314), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xDE642B29), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xD73C58BC), new(null, typeof(int)) },
            { new(0x10B39BE6, 0xD892AD22), new(null, typeof(byte)) },
            { new(0x10B39BE6, 0xCE4384BF), new(null, typeof(FloatVector2)) },
            { new(0x10B39BE6, 0xBDD764F3), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xC216DE38), new(null, typeof(float)) },
            { new(0x10B39BE6, 0xC615E301), new(null, typeof(int)) },
            { new(0x10B39BE6, 0xEDEB8754), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xE441FD9A), new(null, typeof(FloatVector2)) },
            { new(0x10B39BE6, 0xEB4F93E7), new(null, typeof(int)) },
            { new(0x10B39BE6, 0xF079328D), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xF23C0127), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x0ED9E1D3), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x8839711A), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xA9176462), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0xB1BD8BDA), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x6350DF48), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x1A61709C), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x5D2923DF), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x6486E117), new(null, typeof(Crc)) },
            { new(0x10B39BE6, 0x189208A6), new(null, typeof(Crc)) },


            // SeatAnimations
            //{ new(0xA964185F, ), new(null, typeof()) },

            // SeatAnimationsDriver
            //{ new(0x98F0845D, ), new(null, typeof()) },

            // SeatAnimationsGunner
            //{ new(0xC59EAF6A, ), new(null, typeof()) },

            // SeatAnimationsPassenger
            //{ new(0xD30EC2FD, ), new(null, typeof()) },

            // SeatAnimationsSearche
            //{ new(0xD6D46BCC, ), new(null, typeof()) },

            // SeatWithMount
            { new(0x77F3BA45, 0x62FF4512), new(null, typeof(Crc)) },

            // Shop
            //{ new(0x6C266E39, ), new(null, typeof()) },

            // SlowMotionCamera
            //{ new(0xE0BB7B09, ), new(null, typeof()) },

            // Sound
            //{ new(0xDD62BA1A, ), new(null, typeof()) },

            // Spore
            //{ new(0xBF708C00, ), new(null, typeof()) },

            // Tank
            //{ new(0xC686AE99, ), new(null, typeof()) },

            // Targetable
            { new(0xF0DDFE9E, 0x99327D6F), new(null, typeof(float)) },
            { new(0xF0DDFE9E, 0xE1AE04B8), new(null, typeof(float)) },

            // ToneMapping
            { new(0x0C8FC411, 0xEA229E08), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xED233E1A), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xE7D25889), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xCB9F3DB9), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xD0AAAE60), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xE6E5D038), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xC3D37FD0), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xAD6CB5F5), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xB781F21F), new(null, typeof(float)) },
            { new(0x0C8FC411, 0xBCF8745D), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x98BC3287), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x7C15A554), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x812C819E), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x87B59751), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x78DB0D8C), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x571B565F), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x5F353262), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x2EC570B5), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x183AD396), new(null, typeof(float)) },
            { new(0x0C8FC411, 0x2CB865CD), new(null, typeof(float)) },

            // Train
            //{ new(0xB1796D79, ), new(null, typeof()) },

            // TrainCarriage
            //{ new(0x28DBEF3F, ), new(null, typeof()) },

            // TrainEngine
            //{ new(0x060D8FC7, ), new(null, typeof()) },

            // TrainItem
            //{ new(0xE2E68598, ), new(null, typeof()) },

            // TrainList
            //{ new(0x3F7ED4CD, ), new(null, typeof()) },

            // Trappable
            { new(0xB2C8FC90, 0xE77DB9D6), new(null, typeof(float)) },

            // Truck
            //{ new(0xFA10EAC6, ), new(null, typeof()) },

            // Turret
            //{ new(0x01212327, ), new(null, typeof()) },

            // TweakableColors
            { new(0x1935B883, 0x734DDB10), new(null, typeof(int)) },
            { new(0x1935B883, 0xD6A5096B), new(null, typeof(float)) },
            { new(0x1935B883, 0x3FF4F976), new(null, typeof(Color)) },
            { new(0x1935B883, 0x04601549), new(null, typeof(int)) },
            { new(0x1935B883, 0x07165002), new(null, typeof(int)) },
            { new(0x1935B883, 0x1CBA8055), new(null, typeof(Crc)) },

            // Vehicle
            { new(0x2B8D1FDF, 0x7514BE0C), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x4E69EFB4), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x46DA6755), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x4BFC24F8), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x3E33738B), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x3DCC7355), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x3DE8A21D), new(null, typeof(string)) },
            { new(0x2B8D1FDF, 0x3CFEE6B4), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x372ECA29), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x389D5EAE), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x372A4CFB), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x36323226), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x36E14299), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x6EE8BF74), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x72ABC5F0), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x66C65590), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x62049CB7), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x6435EDF4), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x5D9068A3), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x54B0A527), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x4E6BA751), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x5167D9D0), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x5910EE2E), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x5B355766), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x3384C30B), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x2867754B), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x1A14BDDA), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x0BCDA30E), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x021FA107), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x06BA7208), new(null, typeof(uint)) },
            { new(0x2B8D1FDF, 0x085655BA), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x108CE590), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x151267C7), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x23B7A240), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x273B8CE7), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x2344BBC0), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x1B1C16DA), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x1D2C62A4), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x2BBB27C8), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x29C23A39), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x286AAA87), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x29AB6EDC), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x2A5AC5B3), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x2AC18B0D), new(null, typeof(byte)) },
            { new(0x2B8D1FDF, 0x2F4D73D1), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x2DC26872), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x2E659BA7), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x2FF354CB), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x3003EC45), new(null, typeof(FloatVector2)) },
            { new(0x2B8D1FDF, 0xBC58AA53), new(null, typeof(string)) },
            { new(0x2B8D1FDF, 0xB6041786), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xB85E6EF1), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xB4519A5B), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xAFEC20D2), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xB228DA78), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0xAF836DF1), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xA8413DF8), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0xAE911955), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xA7DCD5CC), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0xA69A8211), new(null, typeof(byte)) },
            { new(0x2B8D1FDF, 0xA7D26767), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xA46B0807), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x97AC8632), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x8BC7C405), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x8D701079), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x8A888A08), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x7A13EB63), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x80F02CA2), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x81CC5C7A), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0x9AC8DEAB), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0x97EB34DF), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x999A233F), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x9BE6D196), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x9FE5920F), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xF7F2C6BC), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xFEC86CAD), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xF1EE5038), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xEEF41706), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xF063A2FC), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0xEDBA8801), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xE34A94FB), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0xE0F0480C), new(null, typeof(byte)) },
            { new(0x2B8D1FDF, 0xE2CBDDAB), new(null, typeof(FloatVector2)) },
            { new(0x2B8D1FDF, 0xEAE4803D), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xECBE7BEF), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0xDAC8323D), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xD65937B5), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xD2601C86), new(null, typeof(bool)) },
            { new(0x2B8D1FDF, 0xD48F11EE), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xD9E95CF9), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xD9ED178A), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0xCA612893), new(null, typeof(int)) },
            { new(0x2B8D1FDF, 0xC6649C6E), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xC40E776E), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xC6300246), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xC79DD5E1), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xC7E44514), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x8B206832), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x37B68026), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x04965406), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x7F3BC5E3), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0x0A1AF7EF), new(null, typeof(Crc)) },
            { new(0x2B8D1FDF, 0xC795D87F), new(null, typeof(float)) },
            { new(0x2B8D1FDF, 0xBCECEBE4), new(null, typeof(float)) }, // unused?

            // VehicleCollision
            //{ new(0x8848D9D1, ), new(null, typeof()) },

            // VehicleWheelFX
            //{ new(0x873E5AC6, ), new(null, typeof()) },

            // VerletBoneObject
            //{ new(0x5C4D7FF4, ), new(null, typeof()) },

            // VirVehicleChassis
            //{ new(0x30970D40, ), new(null, typeof()) },

            // VirVehicleEngine
            { new(0x91EB2C3E, 0x62474AD0), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0x577141F3), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0x60CB691E), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0x4EADB17F), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0x00DA288D), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0x0B80AC5C), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0x1CAB1FCC), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0xAA61FF67), new(null, typeof(FloatVector3)) },
            { new(0x91EB2C3E, 0x80DFA4CE), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0x8ED55693), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0xB2A09C8C), new(null, typeof(float)) },
            { new(0x91EB2C3E, 0xDEC209DB), new(null, typeof(float)) },

            // VirVehicleSetup
            { new(0x798C1EC7, 0x04D46017), new(null, typeof(Crc)) },
            { new(0x798C1EC7, 0x072CEE8B), new(null, typeof(Crc)) },
            { new(0x798C1EC7, 0x0B187235), new(null, typeof(Crc)) },

            // VirVehicleTransmission
            //{ new(0xDD0A5AF8, ), new(null, typeof()) },

            // VirVehicleWheel
            //{ new(0xAFC35781, ), new(null, typeof()) },

            // WaterController
            { new(0x1AECDEB6, 0x95FF5441), new(null, typeof(Crc)) },
            { new(0x1AECDEB6, 0x3B2E2AA3), new(null, typeof(float)) },
            { new(0x1AECDEB6, 0x33175C7A), new(null, typeof(float)) },
            { new(0x1AECDEB6, 0x35AFA241), new(null, typeof(float)) },
            { new(0x1AECDEB6, 0x25D7DDC9), new(null, typeof(float)) },
            { new(0x1AECDEB6, 0x0EC38247), new(null, typeof(Color)) },
            { new(0x1AECDEB6, 0x1B9B037D), new(null, typeof(float)) },
            { new(0x1AECDEB6, 0xA491DFCA), new(null, typeof(Color)) },
            { new(0x1AECDEB6, 0x7F39F1BB), new(null, typeof(Color)) },
            { new(0x1AECDEB6, 0xA2300E26), new(null, typeof(Color)) },
            { new(0x1AECDEB6, 0xB9969F89), new(null, typeof(float)) },
            { new(0x1AECDEB6, 0xC61EDBD1), new(null, typeof(float)) },

            // WaterFlow
            { new(0xE9162F96, 0x33E229EA), new(null, typeof(float)) },

            // WaterParticleFx
            //{ new(0x67B79A3A, ), new(null, typeof()) },

            // WaterTexture
            //{ new(0xD0B706D3, ), new(null, typeof()) },

            // Weapon
            { new(0x787C0871, 0x7C0B86B9), new(null, typeof(bool)) },
            { new(0x787C0871, 0x334A7577), new(null, typeof(int)) },
            { new(0x787C0871, 0x1667D2C6), new(null, typeof(float)) },
            { new(0x787C0871, 0x0F3D263B), new(null, typeof(float)) },
            { new(0x787C0871, 0x142C5C7A), new(null, typeof(float)) },
            { new(0x787C0871, 0x1508D7DD), new(null, typeof(FloatVector4)) },
            { new(0x787C0871, 0x0B3830D6), new(null, typeof(float)) },
            { new(0x787C0871, 0x09C7D282), new(null, typeof(int)) },
            { new(0x787C0871, 0x079C2D5B), new(null, typeof(float)) },
            { new(0x787C0871, 0x06A053C3), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x074D0D0F), new(null, typeof(float)) },
            { new(0x787C0871, 0x05B6BA7C), new(null, typeof(float)) },
            { new(0x787C0871, 0x02057C8B), new(null, typeof(float)) },
            { new(0x787C0871, 0x0140B5C6), new(null, typeof(float)) },
            { new(0x787C0871, 0x00F23035), new(null, typeof(float)) },
            { new(0x787C0871, 0x0008DD0E), new(null, typeof(float)) },
            { new(0x787C0871, 0x001223FE), new(null, typeof(float)) },
            { new(0x787C0871, 0x04AD1998), new(null, typeof(bool)) },
            { new(0x787C0871, 0x0266D69F), new(null, typeof(float)) },
            { new(0x787C0871, 0x039589BC), new(null, typeof(bool)) },
            { new(0x787C0871, 0x04E3420D), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x2FD5EE93), new(null, typeof(float)) },
            { new(0x787C0871, 0x31E23AD4), new(null, typeof(int)) },
            { new(0x787C0871, 0x33202768), new(null, typeof(bool)) },
            { new(0x787C0871, 0x2FB64EFC), new(null, typeof(float)) },
            { new(0x787C0871, 0x2F5D8019), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0x2E022594), new(null, typeof(float)) },
            { new(0x787C0871, 0x2CAA4ACE), new(null, typeof(float)) },
            { new(0x787C0871, 0x2DD68A03), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x2A22FDC1), new(null, typeof(float)) },
            { new(0x787C0871, 0x26AE7598), new(null, typeof(float)) },
            { new(0x787C0871, 0x2652FAE8), new(null, typeof(float)) },
            { new(0x787C0871, 0x20C14C6D), new(null, typeof(float)) },
            { new(0x787C0871, 0x1724437A), new(null, typeof(float)) },
            { new(0x787C0871, 0x1B4B7AF6), new(null, typeof(float)) },
            { new(0x787C0871, 0x277B8B55), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x279A36E1), new(null, typeof(bool)) },
            { new(0x787C0871, 0x29C2A39C), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x64F0F584), new(null, typeof(int)) },
            { new(0x787C0871, 0x5CD15632), new(null, typeof(float)) },
            { new(0x787C0871, 0x63468C4B), new(null, typeof(float)) },
            { new(0x787C0871, 0x63FF9380), new(null, typeof(bool)) },
            { new(0x787C0871, 0x56D6092E), new(null, typeof(float)) },
            { new(0x787C0871, 0x560A68C4), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x544D5A73), new(null, typeof(float)) },
            { new(0x787C0871, 0x4E13EA36), new(null, typeof(float)) },
            { new(0x787C0871, 0x52619D52), new(null, typeof(float)) },
            { new(0x787C0871, 0x498C371F), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0x3BF1CDB6), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x379CD846), new(null, typeof(float)) },
            { new(0x787C0871, 0x375004CF), new(null, typeof(float)) },
            { new(0x787C0871, 0x36A350AD), new(null, typeof(float)) },
            { new(0x787C0871, 0x36F01F83), new(null, typeof(float)) },
            { new(0x787C0871, 0x491A1285), new(null, typeof(float)) },
            { new(0x787C0871, 0x44297E86), new(null, typeof(byte)) },
            { new(0x787C0871, 0x3C967A20), new(null, typeof(int)) },
            { new(0x787C0871, 0x3DE8A21D), new(null, typeof(string)) },
            { new(0x787C0871, 0x797A84EF), new(null, typeof(float)) },
            { new(0x787C0871, 0x7A909FD2), new(null, typeof(float)) },
            { new(0x787C0871, 0x7B7A641C), new(null, typeof(float)) },
            { new(0x787C0871, 0x78FA7E87), new(null, typeof(float)) },
            { new(0x787C0871, 0x76ECC8CD), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x74BEF49A), new(null, typeof(int)) },
            { new(0x787C0871, 0x761C3A09), new(null, typeof(float)) },
            { new(0x787C0871, 0x7777D77E), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x73A9FAE3), new(null, typeof(float)) },
            { new(0x787C0871, 0x6DB7B63D), new(null, typeof(int)) },
            { new(0x787C0871, 0x71DFBAEE), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x71E30E69), new(null, typeof(float)) },
            { new(0x787C0871, 0x6CA0E2FE), new(null, typeof(FloatVector4)) },
            { new(0x787C0871, 0x6C93DEA3), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0x69059D08), new(null, typeof(bool)) },
            { new(0x787C0871, 0x69DCA2B6), new(null, typeof(int)) },
            { new(0x787C0871, 0x6B484076), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xBC58AA53), new(null, typeof(string)) },
            { new(0x787C0871, 0xA56EE87E), new(null, typeof(bool)) },
            { new(0x787C0871, 0xA3868095), new(null, typeof(float)) },
            { new(0x787C0871, 0xA3C83F69), new(null, typeof(bool)) },
            { new(0x787C0871, 0xA4CA8665), new(null, typeof(float)) },
            { new(0x787C0871, 0xA3416E0A), new(null, typeof(bool)) },
            { new(0x787C0871, 0xA1953CD0), new(null, typeof(float)) },
            { new(0x787C0871, 0xA0C571DD), new(null, typeof(float)) },
            { new(0x787C0871, 0x9737DC5B), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0x9B889E4A), new(null, typeof(bool)) },
            { new(0x787C0871, 0x96DC09BD), new(null, typeof(float)) },
            { new(0x787C0871, 0x95F2D6BB), new(null, typeof(bool)) },
            { new(0x787C0871, 0x94FCF44A), new(null, typeof(Crc)) },
            { new(0x787C0871, 0x8FABBD6C), new(null, typeof(float)) },
            { new(0x787C0871, 0x94B5AED3), new(null, typeof(float)) },
            { new(0x787C0871, 0x8EB3B1A4), new(null, typeof(float)) },
            { new(0x787C0871, 0x89341658), new(null, typeof(float)) },
            { new(0x787C0871, 0x865EADCB), new(null, typeof(float)) },
            { new(0x787C0871, 0x7DB30B59), new(null, typeof(int)) },
            { new(0x787C0871, 0x85924A26), new(null, typeof(float)) },
            { new(0x787C0871, 0xB344CC74), new(null, typeof(float)) },
            { new(0x787C0871, 0xB182906E), new(null, typeof(float)) },
            { new(0x787C0871, 0xAE28D6E9), new(null, typeof(float)) },
            { new(0x787C0871, 0xAD451485), new(null, typeof(float)) },
            { new(0x787C0871, 0xADDEBDCA), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xAB3BE1A0), new(null, typeof(int)) },
            { new(0x787C0871, 0xA8AB10E2), new(null, typeof(float)) },
            { new(0x787C0871, 0xA75D5327), new(null, typeof(int)) },
            { new(0x787C0871, 0xA5BC45E2), new(null, typeof(float)) },
            { new(0x787C0871, 0xA6107C89), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0xB838A7E9), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0xB4D334D8), new(null, typeof(bool)) },
            { new(0x787C0871, 0xB4685717), new(null, typeof(int)) },
            { new(0x787C0871, 0xB4C6842E), new(null, typeof(float)) },
            { new(0x787C0871, 0xB5C71325), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xB8627450), new(null, typeof(float)) },
            { new(0x787C0871, 0xB91AF09E), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xBA157547), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xEC438E17), new(null, typeof(float)) },
            { new(0x787C0871, 0xEBB3F257), new(null, typeof(float)) },
            { new(0x787C0871, 0xE8E01A04), new(null, typeof(int)) },
            { new(0x787C0871, 0xE545FBF4), new(null, typeof(bool)) },
            { new(0x787C0871, 0xE79E08FE), new(null, typeof(float)) },
            { new(0x787C0871, 0xE3945C1D), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0xE1F8DAFC), new(null, typeof(bool)) },
            { new(0x787C0871, 0xE0AC4345), new(null, typeof(float)) },
            { new(0x787C0871, 0xDDF78D47), new(null, typeof(float)) },
            { new(0x787C0871, 0xE0428E0D), new(null, typeof(float)) },
            { new(0x787C0871, 0xFA9AD969), new(null, typeof(float)) },
            { new(0x787C0871, 0xFE9C1EEB), new(null, typeof(float)) },
            { new(0x787C0871, 0xF8F8275D), new(null, typeof(float)) },
            { new(0x787C0871, 0xF766AA6F), new(null, typeof(float)) },
            { new(0x787C0871, 0xF535722A), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xEFED376D), new(null, typeof(float)) },
            { new(0x787C0871, 0xF4162380), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xF20499A6), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xDC9E428F), new(null, typeof(float)) },
            { new(0x787C0871, 0xD43A1FE1), new(null, typeof(float)) },
            { new(0x787C0871, 0xD850F531), new(null, typeof(int)) },
            { new(0x787C0871, 0xDB4B8095), new(null, typeof(float)) },
            { new(0x787C0871, 0xD3F2828B), new(null, typeof(bool)) },
            { new(0x787C0871, 0xCE9447BC), new(null, typeof(float)) },
            { new(0x787C0871, 0xD01F6168), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xD1B71AE8), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xD1DD6599), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xCE4384BF), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0xCA55EEC9), new(null, typeof(byte)) },
            { new(0x787C0871, 0xC882AF1A), new(null, typeof(float)) },
            { new(0x787C0871, 0xC8E09549), new(null, typeof(int)) },
            { new(0x787C0871, 0xCAF505EA), new(null, typeof(Crc)) },
            { new(0x787C0871, 0xC6300246), new(null, typeof(float)) },
            { new(0x787C0871, 0xC451AB8D), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0xBD8F9C00), new(null, typeof(float)) },
            { new(0x787C0871, 0xBFA35CB2), new(null, typeof(FloatVector2)) },
            { new(0x787C0871, 0xC40ED65B), new(null, typeof(int)) },

            // WillToFight
            //{ new(0xFE6CDF30, ), new(null, typeof()) },

            // WillToFightNode
            //{ new(0x0584B7F4, ), new(null, typeof()) },

            // sub_7FB4D0
            { new(0xD0C6D015, 0xE813FE45), new(null, typeof(int)) },
            { new(0xD0C6D015, 0x772B0B2F), new(null, typeof(FloatVector3)) },
            { new(0xD0C6D015, 0x982589A0), new(null, typeof(FloatVector3)) },
            { new(0xD0C6D015, 0xD3DFD699), new(null, typeof(Crc)) },
            { new(0xD0C6D015, 0xF2CCAF79), new(null, typeof(Crc)) },
            { new(0xD0C6D015, 0xFD4C5C69), new(null, typeof(Crc)) },

            // Unknown
            //{ new(0x4D2A3469, ), new(null, null) }
        };
    }
}