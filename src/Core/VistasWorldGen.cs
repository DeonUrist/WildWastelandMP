using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MapMagic.Core;
using MapMagic.Terrains;
using UnityEngine;

namespace WildWasteland.Core
{
    // Optional World Amp 1.0.7 bridge. Its obfuscated overloads must be resolved by signature.
    internal static class VistasWorldGen
    {
        internal static event Action<string, string> PlaceEntered;
        internal static event Action<TerrainTile> TerrainEdited;
        internal static Func<bool> CleanBanners;
        internal static bool Present;
        internal static int RoadRevision;
        private static FieldInfo _placeName, _placeKind, _landmarkName;
        private static readonly ConcurrentDictionary<string, Vector3[]> Roads = new ConcurrentDictionary<string, Vector3[]>();
        internal static IEnumerable<Vector3[]> RoadLines { get { return Roads.Values; } }
        internal static string RoadIdentity = "";
        private static ConfigFile _config;
        private static Assembly _assembly;
        private static ManualLogSource _logger;
        private static string _assemblyId;
        private static ConfigEntry<float> _bannerHeight;
        internal static float BannerHeight { get { return _bannerHeight != null ? _bannerHeight.Value : .18f; } }
        internal static ConfigFile Config { get { return _config; } }
        // Forced off for every peer while a multiplayer session is active (MultiplayerSettings.Changed): both are rolled from
        // each machine's own player position (spawn track) or camera and random seed (dust devils), so they cannot match.
        internal static ConfigEntryBase SpawnTrack, DustDevils;
        internal static bool Forced(ConfigEntryBase e) { return e != null && (ReferenceEquals(e, SpawnTrack) || ReferenceEquals(e, DustDevils)); }
        // Vistas' public weather network API (a.z): running, follow the host, capture, apply.
        internal static Func<bool> WeatherRunning;
        internal static Action<bool> WeatherFollow;
        internal static Func<byte[]> WeatherCapture;
        internal static Action<byte[]> WeatherApply;
        static FieldInfo _heatActive, _dewTransform;
        static MethodInfo _heatUnregister;
        static readonly Dictionary<int, KeyValuePair<float, bool>> HeatDecisions = new Dictionary<int, KeyValuePair<float, bool>>();
        static readonly HashSet<int> HeatUnregistered = new HashSet<int>();
        // Host-owned Vistas values: everything that shapes the ground, roads, camps, vehicle physics and the weather rotation.
        static readonly HashSet<string> WorldKeys = new HashSet<string>(StringComparer.Ordinal) {
            "Regions:Enabled", "Regions:Scale", "Regions:Blend", "Regions:ExtraTypes", "Regions:Landmarks",
            "Roads:Network", "Roads:Density", "Roads:Width", "Roads:Stitch", "Roads:Clean", "Tiles:WeldBoost", "Seams:CrossDetail",
            "World:ElevationTiers", "World:TierStrength", "Water:AltitudeDew",
            "Vehicles:EngineHeat", "Vehicles:LooseSlopes", "Vehicles:RoadLayers", "Vehicles:RoadBoost", "Vehicles:HillAssist",
            "Weather:Enabled", "Weather:Gameplay", "Weather:GameplayStrength", "Weather:Haboob", "Weather:Thunderstorms",
            "Weather:Aurora", "Weather:MeteorShower", "Weather:Nebula" };
        internal static bool Owns(ConfigEntryBase e) { return _config != null && e != null && ReferenceEquals(e.ConfigFile, _config); }
        internal static bool IsWorld(ConfigEntryBase e)
        { return Owns(e) && (e.Definition.Section == "Terrain" || WorldKeys.Contains(e.Definition.Section + ":" + e.Definition.Key)); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Init()
        {
            PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue("apocalypter.worldamp", out info) || info.Instance == null) return;
            Present = true;
            var assembly = info.Instance.GetType().Assembly;
            _config = info.Instance.Config; _assemblyId = assembly.ManifestModule.ModuleVersionId.ToString();
            SettingsChanged(null, null); _config.SettingChanged += SettingsChanged;
            ConfigEntry<bool> forced;
            if (_config.TryGetEntry<bool>(new ConfigDefinition("Roads", "SpawnTrack"), out forced)) SpawnTrack = forced;
            if (_config.TryGetEntry<bool>(new ConfigDefinition("Weather", "DustDevils"), out forced)) DustDevils = forced;
            if (info.Metadata.Version.ToString() != "1.0.7")
                Log.Warn("[Vistas] Version " + info.Metadata.Version + " differs from the tested 1.0.7; attempting compatibility with this version.");
            try
            {
                _logger = (ManualLogSource)AccessTools.Property(typeof(BaseUnityPlugin), "Logger").GetValue(info.Instance, null);
                Hook(Find(typeof(ManualLogSource), "Log", typeof(void), typeof(LogLevel), typeof(object)), prefix: "QuietLog");
                _assembly = assembly;
                // MonoMod's resolver ignores field types; Vistas overloads field names by type.
                Hook(Find(typeof(MonoMod.Utils.ReflectionHelper), "_ResolveReflection", typeof(MemberInfo),
                    typeof(Mono.Cecil.MemberReference), typeof(Module[])), prefix: "ResolveField");
                ConfigEntry<bool> overlay;
                if (info.Instance.Config.TryGetEntry<bool>(new ConfigDefinition("Debug", "Overlay"), out overlay)) overlay.Value = false;
                info.Instance.Config.TryGetEntry<float>(new ConfigDefinition("Regions", "BannerHeight"), out _bannerHeight);
                var places = assembly.GetType("A.E", true);
                var place = assembly.GetType("A.c", true);
                var landmark = assembly.GetType("A.y", true);
                _placeName = Field(place, "A", typeof(string)); _placeKind = Field(place, "a", typeof(string));
                _landmarkName = Field(landmark, "A", typeof(string));
                Hook(Find(places, "A", typeof(void), place, typeof(float)), transpiler: "Entered");
                Hook(Find(places, "A", typeof(bool), landmark, typeof(bool)), prefix: "NearLandmark", transpiler: "Landmark");
                Hook(Find(places, "d", typeof(void)), prefix: "OriginalBanner");
                Hook(Find(assembly.GetType("a.t", true), "A", typeof(void), typeof(float)), prefix: "NoScouting");
                var weather = assembly.GetType("a.y", true); var kind = assembly.GetType("a.f", true);
                Hook(Find(weather, "e", typeof(bool)), postfix: "DustAllowed");
                Hook(Find(weather, "B", typeof(bool), kind), transpiler: "HaboobAllowed");
                Hook(Find(weather, "A", typeof(void), kind, typeof(float)), transpiler: "WeatherTransition");
                Hook(Find(assembly.GetType("A.q", true), "a", typeof(int), typeof(double[]), typeof(double[]), typeof(int), typeof(bool), typeof(string)), postfix: "RoadCreated");
                Hook(Find(assembly.GetType("A.S", true), "A", typeof(void), assembly.GetType("A.q", true), typeof(MapMagicObject), typeof(TerrainTile), typeof(Terrain), typeof(TerrainData), assembly.GetType("A.S+A", true), typeof(int), typeof(float)), postfix: "GroundEdited");
                Hook(Find(assembly.GetType("ApocalpyterWorldAmp.WorldAmpDriver", true), "ClearWorld", typeof(void)), postfix: "ClearRoads");
                // Network reset (SpawnTrack/Density/Width change) bypasses ClearWorld; drop the captured lines with it.
                Hook(Find(assembly.GetType("A.q", true), "A", typeof(void)), postfix: "ClearRoads");
                // Engine heat: AI-held cars and cars another machine simulates are left alone (their own owner decides).
                var heat = assembly.GetType("ApocalpyterWorldAmp.EngineHeat", true);
                _heatActive = AccessTools.Field(heat, "Active"); _heatUnregister = AccessTools.Method(heat, "Unregister");
                if (_heatActive == null || _heatUnregister == null) throw new MissingMemberException(heat.FullName, "Active/Unregister");
                Hook(AccessTools.Method(heat, "FixedUpdate"), prefix: "HeatAllowed");
                Hook(AccessTools.Method(heat, "PickActive"), postfix: "HeatReadout");
                // Wind, hill assist and dust-devil forces only push bodies this machine simulates.
                Hook(Find(assembly.GetType("a.y", true), "H", typeof(void)), transpiler: "LocalBodies");
                Hook(Find(assembly.GetType("A.V", true), "A", typeof(void)), transpiler: "LocalBodies");
                Hook(Find(assembly.GetType("B.E", true), "d", typeof(void)), transpiler: "LocalBodies");
                // Dew collectors: the machine that owns a replicated collector sets its water; copies follow the owner.
                var dew = assembly.GetType("a.T", true); var collector = assembly.GetType("a.T+A", true);
                _dewTransform = Field(collector, "A", typeof(Transform));
                Hook(Find(dew, "A", typeof(void), collector, typeof(bool)), prefix: "DewAllowed");
                // Wind direction and gusts are functions of the local clock: guests use the host's.
                Hook(Find(assembly.GetType("a.g", true), "A", typeof(float), typeof(float), typeof(int)), prefix: "SharedClock");
                Hook(Find(assembly.GetType("a.H", true), "g", typeof(float), typeof(float)), prefix: "SharedClock");
                var net = assembly.GetType("a.z", true);
                WeatherRunning = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), Find(net, "a", typeof(bool)));
                WeatherFollow = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), Find(net, "A", typeof(void), typeof(bool)));
                WeatherCapture = (Func<byte[]>)Delegate.CreateDelegate(typeof(Func<byte[]>), Find(net, "B", typeof(byte[])));
                WeatherApply = (Action<byte[]>)Delegate.CreateDelegate(typeof(Action<byte[]>), Find(net, "A", typeof(void), typeof(byte[])));
                Log.Always("[Vistas] " + info.Metadata.Version + " compatibility ready: overlay defaults off; WW scouting, CleanHUD banners, WW sandstorms and replacement-road capture; routine logging follows WW VerboseLog.");
            }
            catch (Exception e) { Log.Error("[Vistas] Compatibility installation failed", e); }
        }
        private static bool QuietLog(ManualLogSource __instance, LogLevel __0)
        { return !ReferenceEquals(__instance, _logger) || Log.Verbose || (__0 & ~(LogLevel.Info | LogLevel.Debug)) != 0; }
        private static bool ResolveField(Mono.Cecil.MemberReference __0, ref MemberInfo __result)
        {
            var field = __0 as Mono.Cecil.FieldReference;
            if (field == null || _assembly == null || field.DeclaringType == null) return true;
            string scope = field.DeclaringType.Scope.Name;
            if (scope != _assembly.GetName().Name && scope != _assembly.ManifestModule.Name) return true;
            var type = _assembly.GetType(field.DeclaringType.FullName.Replace('/', '+'), false, false);
            if (type == null) return true;
            var signature = field.FieldType;
            while (signature is Mono.Cecil.RequiredModifierType || signature is Mono.Cecil.OptionalModifierType)
                signature = ((Mono.Cecil.TypeSpecification)signature).ElementType;
            var valueType = MonoMod.Utils.ReflectionHelper.ResolveReflection(signature);
            foreach (var candidate in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (candidate.Name == field.Name && candidate.FieldType == valueType)
                { __result = candidate; return false; }
            throw new MissingFieldException(type.FullName, field.Name + " : " + signature.FullName);
        }
        private static void SettingsChanged(object sender, SettingChangedEventArgs args)
        {
            var values = new List<string>();
            foreach (var entry in _config)
                if (entry.Key.Section == "Terrain" || entry.Key.Section == "Regions" && (entry.Key.Key == "Enabled" || entry.Key.Key == "Scale" || entry.Key.Key == "Blend" || entry.Key.Key == "ExtraTypes" || entry.Key.Key == "Landmarks") || entry.Key.Section == "Roads" || entry.Key.Section == "Tiles" || entry.Key.Section == "Seams")
                    values.Add(entry.Key.Section + ":" + entry.Key.Key + "=" + TomlTypeConverter.ConvertToString(entry.Value.BoxedValue, entry.Value.SettingType));
            values.Sort(StringComparer.Ordinal); RoadIdentity = _assemblyId + "|" + string.Join("|", values.ToArray());
        }
        private static FieldInfo Field(Type type, string name, Type value)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (field.Name == name && field.FieldType == value) return field;
            throw new MissingFieldException(type.FullName, name + " : " + value.Name);
        }
        private static MethodInfo Find(Type type, string name, Type result, params Type[] args)
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != name || method.ReturnType != result) continue;
                var parameters = method.GetParameters(); if (parameters.Length != args.Length) continue;
                bool match = true; for (int i = 0; i < args.Length; i++) if (parameters[i].ParameterType != args[i]) match = false;
                if (match) return method;
            }
            throw new MissingMethodException(type.FullName, name);
        }
        private static void Hook(MethodInfo original, string prefix = null, string postfix = null, string transpiler = null)
        {
            if (!Patches.Patch(null, original, prefix: prefix == null ? null : AccessTools.Method(typeof(VistasWorldGen), prefix),
                postfix: postfix == null ? null : AccessTools.Method(typeof(VistasWorldGen), postfix),
                transpiler: transpiler == null ? null : AccessTools.Method(typeof(VistasWorldGen), transpiler)))
                throw new InvalidOperationException("Cannot patch " + original.DeclaringType.FullName + "." + original.Name);
        }
        private static bool OriginalBanner() { return CleanBanners == null || !CleanBanners(); }
        private static bool NoScouting() { return false; }
        private static bool NearLandmark(bool __1) { return !__1; }
        // WW owns sandstorms when its Weather runs, and always in multiplayer (Vistas weather is rolled per machine).
        private static bool WwDust { get { return Services.Weather != null || Multiplayer.SessionActive; } }
        private static void DustAllowed(ref bool __result) { if (WwDust) __result = false; }
        private static bool Allowed(bool original, int kind) { return original && (kind != 7 || !WwDust); }
        private static int Transition(int kind) { return WwDust && (kind == 4 || kind == 7) ? 2 : kind; }
        private static bool HeatSkipped(GameObject car)
        {
            int id = car.GetInstanceID(); float now = Time.unscaledTime; KeyValuePair<float, bool> known;
            if (HeatDecisions.TryGetValue(id, out known) && now < known.Key) return known.Value;
            if (HeatDecisions.Count > 1024) HeatDecisions.Clear();
            var crew = car.GetComponent<Modules.Patrols.Crew>(); var marker = car.GetComponent<Modules.Patrols.PatrolMarker>();
            bool skip = crew != null && crew.Locked || marker != null && !marker.PlayerEntered || VistasMultiplayer.Remote(car);
            HeatDecisions[id] = new KeyValuePair<float, bool>(now + 1f, skip); return skip;
        }
        private static bool HeatAllowed(MonoBehaviour __instance)
        {
            int id = __instance.GetInstanceID();
            if (!HeatSkipped(__instance.gameObject)) { if (HeatUnregistered.Count != 0) HeatUnregistered.Remove(id); return true; }
            // Drop the power modifier once; FixedUpdate registers it again when the car becomes eligible.
            if (HeatUnregistered.Add(id)) _heatUnregister.Invoke(__instance, null);
            return false;
        }
        private static void HeatReadout()
        { var active = _heatActive.GetValue(null) as Component; if (active != null && HeatSkipped(active.gameObject)) _heatActive.SetValue(null, null); }
        private static bool Passive(Rigidbody body) { return body.isKinematic || VistasMultiplayer.Remote(body.gameObject); }
        private static IEnumerable<CodeInstruction> LocalBodies(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Rigidbody), "isKinematic"); int replaced = 0;
            var list = new List<CodeInstruction>(instructions);
            foreach (var instruction in list)
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) && Equals(instruction.operand, getter))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(VistasWorldGen), "Passive"); replaced++; }
            if (replaced == 0) throw new InvalidOperationException("Vistas force loop changed: no isKinematic check");
            return list;
        }
        private static bool DewAllowed(object __0)
        { var at = __0 != null ? _dewTransform.GetValue(__0) as Transform : null; return at == null || !VistasMultiplayer.Remote(at.gameObject); }
        private static void SharedClock(ref float __0) { __0 += VistasMultiplayer.ClockOffset; }
        private static IEnumerable<CodeInstruction> HaboobAllowed(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    yield return load; yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VistasWorldGen), "Allowed"));
                }
                yield return instruction;
            }
        }
        private static IEnumerable<CodeInstruction> WeatherTransition(IEnumerable<CodeInstruction> instructions)
        {
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VistasWorldGen), "Transition"));
            yield return new CodeInstruction(OpCodes.Starg_S, (byte)0);
            foreach (var instruction in instructions) yield return instruction;
        }
        private static IEnumerable<CodeInstruction> Entered(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    var load = new CodeInstruction(OpCodes.Ldarga_S, (byte)0); load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    yield return load; yield return new CodeInstruction(OpCodes.Ldfld, _placeName);
                    yield return new CodeInstruction(OpCodes.Ldarga_S, (byte)0); yield return new CodeInstruction(OpCodes.Ldfld, _placeKind);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VistasWorldGen), "Notify"));
                }
                yield return instruction;
            }
        }
        private static IEnumerable<CodeInstruction> Landmark(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    var skip = generator.DefineLabel(); var dup = new CodeInstruction(OpCodes.Dup);
                    dup.labels.AddRange(instruction.labels); instruction.labels.Clear(); yield return dup;
                    yield return new CodeInstruction(OpCodes.Brfalse, skip);
                    yield return new CodeInstruction(OpCodes.Ldarga_S, (byte)0); yield return new CodeInstruction(OpCodes.Ldfld, _landmarkName);
                    yield return new CodeInstruction(OpCodes.Ldstr, "LANDMARK DISCOVERED");
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VistasWorldGen), "Notify"));
                    var end = new CodeInstruction(OpCodes.Nop); end.labels.Add(skip); yield return end;
                }
                yield return instruction;
            }
        }
        private static void Notify(string name, string detail)
        { var handler = PlaceEntered; if (handler != null) handler(name, detail); }
        private static void RoadCreated(double[] __0, double[] __1)
        {
            if (__0 == null || __1 == null || __0.Length < 2 || __0.Length > 20000 || __0.Length != __1.Length || Roads.Count >= 4096) return;
            var points = new Vector3[__0.Length];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector3((float)__0[i], 0, (float)__1[i]);
            string key = points[0].ToString("R") + ":" + points[points.Length - 1].ToString("R");
            Roads[key] = points; System.Threading.Interlocked.Increment(ref RoadRevision);
        }
        private static void ClearRoads() { Roads.Clear(); System.Threading.Interlocked.Increment(ref RoadRevision); }
        private static void GroundEdited(TerrainTile __2, Terrain __3)
        {
            if (__2 == null || __2.main == null || __2.main.terrain != __3) return;
            var handler = TerrainEdited; if (handler != null) try { handler(__2); } catch (Exception e) { Log.Error("[Vistas] Terrain snapshot callback", e); }
        }
    }
}
