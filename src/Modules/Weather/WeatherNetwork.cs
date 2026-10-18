using System;
using System.IO;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    public sealed partial class WeatherModule
    {
        MultiplayerSnapshot _network;
        void StartNetwork()
        {
            if (Multiplayer.Present) _network = new MultiplayerSnapshot(this, "Weather world snapshot", 72, 73, CaptureNetwork, ApplyNetwork);
        }
        void IMultiplayerPresentation.InitClient()
        {
            if (!Core.Patches.Patch(this, Core.Patches.Method(typeof(UnityEngine.AzureSky.AzureFogScattering), "OnRenderImage"),
                prefix: Core.Patches.Method(typeof(WeatherSky), "NativeFogBefore"), finalizer: Core.Patches.Method(typeof(WeatherSky), "NativeFogAfter")))
                throw new InvalidOperationException("Weather requires the native Azure fog scope.");
            _running = true; _count = 0; _clock = 0;
            _audio = new WeatherAudio(Path.Combine(Dir, "sound", "sandstorm.wav"));
            Services.Weather = this;
            _draw = Scheduler.EveryFrame(this, "Weather network presentation", Tick, Phase.LateUpdateLast);
            _draw.Enabled = false;
            // The native helper hides the replicated encounter graphics; no client hazard job is installed.
            _native = new WeatherNative(this);
            StartNetwork();
        }
        void IMultiplayerPresentation.ShutdownClient() { Shutdown(); }
        byte[] CaptureNetwork()
        {
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write(_count); w.Write(_clock);
                for (int i = 0; i < _count; i++)
                {
                    var r = _regions[i]; w.Write(r.WorldCenter.x); w.Write(r.WorldCenter.y); w.Write(r.WorldCenter.z);
                    w.Write(r.Radii.x); w.Write(r.Radii.y); w.Write(r.Velocity.x); w.Write(r.Velocity.y); w.Write(r.Velocity.z);
                    w.Write(r.Heading); w.Write(r.Hurricane); w.Write(_ages[i]);
                }
                return stream.ToArray();
            }
        }
        void ApplyNetwork(byte[] bytes)
        {
            var regions = new WeatherRegion[MaxRegions]; var ages = new float[MaxRegions]; int count; float clock;
            using (var r = new BinaryReader(new MemoryStream(bytes, false)))
            {
                count = r.ReadInt32(); clock = r.ReadSingle(); if (count < 0 || count > MaxRegions || float.IsNaN(clock) || float.IsInfinity(clock)) throw new InvalidDataException("Invalid storm snapshot");
                for (int i = 0; i < count; i++)
                {
                    regions[i] = new WeatherRegion { WorldCenter = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), Radii = new Vector2(r.ReadSingle(), r.ReadSingle()), Velocity = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), Heading = r.ReadSingle(), Hurricane = r.ReadBoolean() };
                    ages[i] = r.ReadSingle();
                    var region = regions[i];
                    if (float.IsNaN(region.WorldCenter.sqrMagnitude) || float.IsInfinity(region.WorldCenter.sqrMagnitude) || float.IsNaN(region.Velocity.sqrMagnitude) || float.IsInfinity(region.Velocity.sqrMagnitude)
                        || float.IsNaN(region.Heading) || float.IsInfinity(region.Heading) || !(region.Radii.x > 0 && region.Radii.x <= 20000) || !(region.Radii.y > 0 && region.Radii.y <= 20000) || !(ages[i] >= 0 && ages[i] <= 600)) throw new InvalidDataException("Invalid storm geometry");
                }
                if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Trailing storm data");
            }
            if (count > 0 && _visuals == null) { _visuals = new WeatherVisuals(_audio); _audio.Load(); }
            if (_visuals != null) _visuals.ClearGround();
            _count = count; _clock = clock; Array.Copy(regions, _regions, MaxRegions); Array.Copy(ages, _ages, MaxRegions); Array.Clear(_anchors, 0, MaxRegions);
            _draw.Enabled = count > 0;
        }
    }
}
