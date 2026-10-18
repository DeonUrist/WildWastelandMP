using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.Weather
{
    internal static class WeatherDiagnostics
    {
        internal static readonly string[] Presets = { "Normal", "Engine baseline", "Regional atmosphere", "Wall only", "Canopy only", "Wall and canopy", "One hurricane - shape", "One hurricane - full", "Local clouds - native tint", "Local clouds - brown tint", "Native fog scope OFF", "Native fog scope ON" };
        private static readonly string[] Outputs = { "Normal", "Layer IDs", "Opacity", "Entry/exit distances", "Linear scene depth", "World ray direction", "Density only" };
        internal static bool Enabled, Freeze, PendingCamera;
        internal static int Preset, Output, SelectedRegion = -1, SelectedColumn = -1;
        private static bool _visible;
        private static string _caption = "";
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        internal static bool Frozen { get { return Enabled && Freeze; } }
        internal static bool Full { get { return !Enabled || Preset == 0 || Preset >= 10; } }
        private static bool ScreenData { get { return Enabled && (Output == 4 || Output == 5); } }
        internal static bool Wall { get { return !ScreenData && (Full || Preset == 3 || Preset == 5); } }
        internal static bool Canopy { get { return !ScreenData && (Full || Preset == 4 || Preset == 5); } }
        internal static bool Core { get { return !ScreenData && (Full || Preset == 6 || Preset == 7); } }
        internal static bool Clouds { get { return !ScreenData && (Full || Preset == 8 || Preset == 9); } }
        internal static bool Atmosphere { get { return Full || Preset == 2; } }
        internal static bool SkyScope { get { return Atmosphere; } }
        internal static bool FogScope { get { return SkyScope && (!Enabled || Preset != 10); } }
        internal static bool Single { get { return Enabled && (Preset == 6 || Preset == 7); } }
        internal static bool ShapeOnly { get { return Enabled && Preset == 6; } }
        internal static bool CloudPair { get { return Enabled && (Preset == 8 || Preset == 9); } }

        internal static void Reset()
        {
            Enabled = false; Freeze = false; Preset = 0; Output = 0; SelectedRegion = SelectedColumn = -1;
            PendingCamera = false; _visible = false; Apply();
        }
        internal static void Changed()
        {
            Apply(); PendingCamera = Enabled;
            _caption = "WEATHER DIAGNOSTICS | " + Presets[Preset] + " | " + Outputs[Output] + (Frozen ? " | FROZEN" : " | RUNNING");
            Log.Always("[WeatherDiag] " + (Enabled ? _caption : "OFF - normal renderer") + "; no cause established by changing this view.");
        }
        private static void Apply()
        {
            if (Enabled) Shader.EnableKeyword("WW_DIAGNOSTICS_ON"); else Shader.DisableKeyword("WW_DIAGNOSTICS_ON");
            Shader.SetGlobalInt("_WWDiagOutput",Enabled ? Output : 0);
            Shader.SetGlobalFloat("_WWDiagShape",ShapeOnly ? 1 : 0);
            Shader.SetGlobalFloat("_WWDiagNoAtmosphere",Enabled && !Atmosphere ? 1 : 0);
            Shader.SetGlobalFloat("_WWDiagSmokeLayer",Enabled && Preset == 3 ? 1 : Enabled && Preset == 4 ? 2 : 0);
        }
        internal static void Show(bool visible) { _visible = visible; }
        internal static bool WantsOverlay() { return Enabled && _visible; }
        internal static void DrawOverlay() { GUI.Box(new Rect(12,85,780,30),_caption); }
        internal static void Controls(ToolLayout l)
        {
            l.Heading("Weather diagnosis");
            bool enabled = l.Toggle("Enable diagnostic controls (temporary)",Enabled);
            if (enabled != Enabled) { Enabled = enabled; if (!enabled) Freeze = false; Changed(); }
            if (!Enabled) return;
            l.Value("Layer preset",Presets[Preset]);
            int hit = l.Buttons("Previous layer", "Next layer");
            if (hit >= 0) { Preset = (Preset+(hit == 0 ? Presets.Length-1 : 1))%Presets.Length; Changed(); }
            l.Value("Shader data view",Outputs[Output]);
            hit = l.Buttons("Previous data view", "Next data view");
            if (hit >= 0) { Output = (Output+(hit == 0 ? Outputs.Length-1 : 1))%Outputs.Length; Changed(); }
            bool freeze = l.Toggle("Freeze owned storm state / particle simulation",Freeze);
            if (freeze != Freeze) { Freeze = freeze; Changed(); }
            l.Text("Normal output is unchanged with diagnostics off. Freeze leaves the player/camera and native sky/time running; native sand shader UV time also continues. Local-cloud paired views share source parameters and differ only in tint. Shader data views apply to WW shaders, not the borrowed native sand material.");
            if (Single) l.Value("Selected hurricane",SelectedColumn < 0 ? "Waiting for a critical storm" : "region "+SelectedRegion+", column "+SelectedColumn);
        }
        internal static void CameraRecord(Camera camera, float clock)
        {
            if (!Enabled || !PendingCamera || camera == null) return;
            PendingCamera = false;
            Log.Always("[WeatherDiag] Camera "+camera.name+"; clock="+clock.ToString("R",Culture)+"; "+Screen.width+"x"+Screen.height+"; FOV="+camera.fieldOfView.ToString("R",Culture)+"; quality="+QualitySettings.GetQualityLevel()+"; colorSpace="+QualitySettings.activeColorSpace+"; MSAA="+QualitySettings.antiAliasing+"; projection="+camera.projectionMatrix.ToString("F6")+"; view="+camera.worldToCameraMatrix.ToString("F6"));
        }

        [Serializable]
        internal sealed class Snapshot
        {
            public int Version = 1, WorldSeed, SelectedRegion, SelectedColumn, ResolutionX, ResolutionY;
            public string Slot, CapturedUtc, CameraName, Policy;
            public float Clock, Width, Depth, Density, Fov;
            public WeatherRegion[] Regions;
            public float[] Ages, GroundOffsets;
            public bool[] GroundValid;
            public Vector3 CameraWorld, CameraForward, ParticleRoot;
            public Vector4 DustColor, AirColor, SunDirection;
            public Quaternion ParticleRotation;
            public Matrix4x4 Projection, View;
            public uint ParticleSeed;
            public Particle[] Particles;
        }
        [Serializable]
        internal struct Particle
        {
            public Vector3 World, Velocity, Size, Rotation;
            public Color32 Color;
            public float Life, StartLife;
            public uint Seed;
        }
        internal static int WorldSeed()
        {
            var fsm = GameRefs.SaveLoadGame;
            var value = fsm != null ? fsm.FsmVariables.FindFsmInt("seed") : null;
            return value != null ? value.Value : int.MinValue;
        }
        internal static string SnapshotPath { get { return Path.Combine(Assets.ModuleConfigDir("Weather"),"diagnostic-snapshot.bin"); } }
        internal static void Write(Snapshot snapshot)
        {
            using (var writer = new BinaryWriter(File.Create(SnapshotPath))) WriteSnapshot(writer,snapshot);
            Log.Always("[WeatherDiag] Captured snapshot: "+SnapshotPath+"; slot="+snapshot.Slot+"; world seed="+snapshot.WorldSeed+"; camera world="+snapshot.CameraWorld.ToString("F3")+"; camera is recorded, never teleported.");
        }
        internal static Snapshot Read()
        {
            using (var reader = new BinaryReader(File.OpenRead(SnapshotPath)))
            {
                if (reader.ReadInt32() != 0x57574431) throw new InvalidDataException("Not a WW diagnostic snapshot");
                var d = new Snapshot(); d.Version=reader.ReadInt32();
                if (d.Version != 1) throw new InvalidDataException("Unsupported diagnostic snapshot");
                d.WorldSeed=reader.ReadInt32(); d.SelectedRegion=reader.ReadInt32(); d.SelectedColumn=reader.ReadInt32();
                d.ResolutionX=reader.ReadInt32(); d.ResolutionY=reader.ReadInt32();
                d.Slot=reader.ReadString(); d.CapturedUtc=reader.ReadString(); d.CameraName=reader.ReadString(); d.Policy=reader.ReadString();
                d.Clock=reader.ReadSingle(); d.Width=reader.ReadSingle(); d.Depth=reader.ReadSingle(); d.Density=reader.ReadSingle(); d.Fov=reader.ReadSingle();
                d.CameraWorld=ReadVector(reader); d.CameraForward=ReadVector(reader); d.ParticleRoot=ReadVector(reader);
                d.ParticleRotation=new Quaternion(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                d.Projection=ReadMatrix(reader); d.View=ReadMatrix(reader); d.ParticleSeed=reader.ReadUInt32();
                d.DustColor=ReadFour(reader); d.AirColor=ReadFour(reader); d.SunDirection=ReadFour(reader);
                int count=ReadCount(reader,WeatherModule.MaxRegions); d.Regions=new WeatherRegion[count]; d.Ages=new float[count];
                for (int i=0;i<count;i++)
                {
                    d.Regions[i]=new WeatherRegion { WorldCenter=ReadVector(reader), Radii=new Vector2(reader.ReadSingle(),reader.ReadSingle()),
                        Velocity=ReadVector(reader), Heading=reader.ReadSingle(), Hurricane=reader.ReadBoolean() };
                    d.Ages[i]=reader.ReadSingle();
                }
                count=ReadCount(reader,WeatherModule.MaxRegions*80); d.GroundOffsets=new float[count]; d.GroundValid=new bool[count];
                for (int i=0;i<count;i++) { d.GroundOffsets[i]=reader.ReadSingle(); d.GroundValid[i]=reader.ReadBoolean(); }
                if (reader.ReadBoolean())
                {
                    count=ReadCount(reader,1000); d.Particles=new Particle[count];
                    for (int i=0;i<count;i++) d.Particles[i]=new Particle { World=ReadVector(reader), Velocity=ReadVector(reader),
                        Size=ReadVector(reader), Rotation=ReadVector(reader), Color=new Color32(reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadByte()),
                        Life=reader.ReadSingle(), StartLife=reader.ReadSingle(), Seed=reader.ReadUInt32() };
                }
                return d;
            }
        }
        private static void WriteSnapshot(BinaryWriter w, Snapshot d)
        {
            w.Write(0x57574431); w.Write(d.Version); w.Write(d.WorldSeed); w.Write(d.SelectedRegion); w.Write(d.SelectedColumn);
            w.Write(d.ResolutionX); w.Write(d.ResolutionY); w.Write(d.Slot ?? ""); w.Write(d.CapturedUtc ?? ""); w.Write(d.CameraName ?? ""); w.Write(d.Policy ?? "");
            w.Write(d.Clock); w.Write(d.Width); w.Write(d.Depth); w.Write(d.Density); w.Write(d.Fov);
            WriteVector(w,d.CameraWorld); WriteVector(w,d.CameraForward); WriteVector(w,d.ParticleRoot);
            w.Write(d.ParticleRotation.x); w.Write(d.ParticleRotation.y); w.Write(d.ParticleRotation.z); w.Write(d.ParticleRotation.w);
            WriteMatrix(w,d.Projection); WriteMatrix(w,d.View); w.Write(d.ParticleSeed);
            WriteFour(w,d.DustColor); WriteFour(w,d.AirColor); WriteFour(w,d.SunDirection);
            w.Write(d.Regions.Length);
            for (int i=0;i<d.Regions.Length;i++)
            {
                var r=d.Regions[i]; WriteVector(w,r.WorldCenter); w.Write(r.Radii.x); w.Write(r.Radii.y); WriteVector(w,r.Velocity);
                w.Write(r.Heading); w.Write(r.Hurricane); w.Write(d.Ages[i]);
            }
            w.Write(d.GroundOffsets.Length);
            for (int i=0;i<d.GroundOffsets.Length;i++) { w.Write(d.GroundOffsets[i]); w.Write(d.GroundValid[i]); }
            w.Write(d.Particles != null);
            if (d.Particles == null) return;
            w.Write(d.Particles.Length);
            foreach (var p in d.Particles)
            {
                WriteVector(w,p.World); WriteVector(w,p.Velocity); WriteVector(w,p.Size); WriteVector(w,p.Rotation);
                w.Write(p.Color.r); w.Write(p.Color.g); w.Write(p.Color.b); w.Write(p.Color.a); w.Write(p.Life); w.Write(p.StartLife); w.Write(p.Seed);
            }
        }
        private static int ReadCount(BinaryReader r, int max) { int count=r.ReadInt32(); if (count<0 || count>max) throw new InvalidDataException("Invalid snapshot array length"); return count; }
        private static void WriteVector(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static Vector3 ReadVector(BinaryReader r) { return new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()); }
        private static void WriteFour(BinaryWriter w, Vector4 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
        private static Vector4 ReadFour(BinaryReader r) { return new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle()); }
        private static void WriteMatrix(BinaryWriter w, Matrix4x4 m) { for (int i=0;i<16;i++) w.Write(m[i]); }
        private static Matrix4x4 ReadMatrix(BinaryReader r) { var m=new Matrix4x4(); for (int i=0;i<16;i++) m[i]=r.ReadSingle(); return m; }
    }
}
