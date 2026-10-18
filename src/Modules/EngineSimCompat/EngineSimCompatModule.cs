using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using SkrilStudio;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WildWasteland.Core;

namespace WildWasteland.Modules.EngineSimCompat
{
    public sealed class EngineSimCompatModule : ModuleBase, IVehicleIgnition
    {
        private static EngineSimCompatModule _owner;
        private static object _manager, _playerSwitch;
        private static IDictionary _active;
        private static Action<object> _update, _fixed;
        private static Func<object, object> _switch;
        private static Func<object, object> _couplingSession;
        private static Func<object, VehicleController> _sessionVehicle;
        private static Func<object, bool> _partGone, _keyOn, _on, _cranking;
        private static Func<object, Component> _itemText;
        private static Action<object, bool> _turn, _setCranking, _setPressed;
        private static Action<object, string> _shown;
        private static bool _ready, _dispatch, _attempted, _down, _held;
        private static int _playerFrame = -1;
        private static readonly Dictionary<object, StartRequest> Starts = new Dictionary<object, StartRequest>();
        private sealed class StartRequest { internal EngineComponent Engine; internal float Until; }

        public override string Id { get { return "EngineSimCompat"; } }
        public override string DisplayName { get { return "Engine Sim compatibility"; } }
        public override void BindConfig(ModuleConfig cfg) { }
        public override void Init()
        {
            _owner = this; _ready = _dispatch = _attempted = false; _playerFrame = -1;
            TryInstall();
            Scheduler.Interval(this, "Engine Sim discovery", 1f, TryInstall);
            if (!_attempted) Log.Always("[EngineSimCompat] Engine Sim not loaded; compatibility idle");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TryInstall()
        {
            if (_attempted) return;
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue("com.burner.apocalypter.enginesim", out info) || ReferenceEquals(info.Instance, null)) return;
            _attempted = true;
            try
            {
                if (info.Metadata.Version != new Version(1, 7, 0)) Log.Warn("[EngineSimCompat] Engine Sim " + info.Metadata.Version + " differs from the tested 1.7.0; attempting compatibility with this version");
                var plugin = info.Instance.GetType(); var asm = plugin.Assembly;
                var manager = asm.GetType("EngineSim.EngineManager", true);
                var runner = AccessTools.Field(plugin, "_runner").GetValue(null) as GameObject;
                if (runner == null) { Log.Warn("[EngineSimCompat] Engine Sim has no runner; check its native-library loading error. Standalone behavior retained."); return; }
                _manager = runner.GetComponent(manager);
                if (_manager == null) throw new InvalidOperationException("Engine Sim manager missing");
                var coupling = asm.GetType("EngineSim.DriveCoupling", true);
                var ignition = asm.GetType("EngineSim.IgnitionSwitch", true);
                _active = (IDictionary)AccessTools.Field(coupling, "Active").GetValue(null);
                _update = Call(manager, "Update"); _fixed = Call(manager, "FixedUpdate");
                _switch = Get<object>(coupling, "_switch"); _partGone = Get<bool>(coupling, "PartGone"); _keyOn = Get<bool>(coupling, "KeyOn");
                _couplingSession = Get<object>(coupling, "_session"); _sessionVehicle = Get<VehicleController>(asm.GetType("EngineSim.EngineSession", true), "Vc");
                _on = Get<bool>(ignition, "On"); _cranking = Get<bool>(ignition, "Cranking");
                _turn = Call<bool>(ignition, "Turn"); _setCranking = Set<bool>(ignition, "Cranking"); _setPressed = Set<bool>(ignition, "_pressed");
                _itemText = Get<Component>(ignition, "_itemText"); _shown = Set<string>(ignition, "_shown");
                EngineDiscovery.Init();
                Patch(manager, "Update", "ScheduledOnly"); Patch(manager, "FixedUpdate", "ScheduledOnly", transpiler: "OwnerTorque");
                var session = asm.GetType("EngineSim.EngineSession", true);
                _sessionControls = MultiplayerAccess.Call<Action<object, float, float, bool, float>>(session, "SetControls", typeof(float), typeof(float), typeof(bool), typeof(float));
                _condition = MultiplayerAccess.Call<Func<object, float>>(manager, "ReadCondition", session);
                _sessions = MultiplayerAccess.Get<IDictionary>(manager, "_sessions");
                Patch(manager, "Feed", "FeedAuthority");
                Patch(manager, "Scan", transpiler: "CachedScan"); Patch(manager, "FindListener", "FindListener");
                Patch(asm.GetType("EngineSim.Listener", true), "get_Current", "FindListener");
                Patch(ignition, "Update", "BeforeSwitch", "AfterSwitch", "SwitchInput");
                Patch(ignition, "Pointed", transpiler: "CachedCamera"); Patch(ignition, "Pointed", postfix: "KeyboardPointed");
                Patch(ignition, "SetText", "SetText"); Patch(coupling, "Release", postfix: "Released");
                Patch(coupling, "Update", "CouplingAuthority");
                Patch(asm.GetType("EngineSim.WheelCoupling", true), "Step", "WheelAuthority");
                Patch(typeof(VehicleController), "OnEnable", postfix: "VehicleEnabled");
                Patch(typeof(RealisticEngineSound), "OnEnable", postfix: "SoundEnabled");
                Scheduler.EveryFrame(_owner, "Engine Sim frame", Frame);
                Scheduler.EveryFrame(_owner, "Engine Sim physics", PhysicsFrame, Phase.FixedUpdate);
                Scheduler.Sliced(_owner, "Engine Sim world cache", 500, CacheStep);
                Services.VehicleIgnition = _owner; _ready = true;
                Log.Always("[EngineSimCompat] Engine Sim " + info.Metadata.Version + " scheduled; 500 us world cache, player/crew ignition bridge ready; native simulation and audio threads retained");
            }
            catch
            {
                _ready = false;
                ClearStarts();
                Core.Patches.Unpatch(_owner);
                Scheduler.RemoveAll(_owner);
                EngineDiscovery.Clear();
                throw;
            }
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string transpiler = null)
        {
            var own = typeof(EngineSimCompatModule);
            if (!Core.Patches.Patch(_owner, AccessTools.Method(type, name),
                prefix: prefix == null ? null : AccessTools.Method(own, prefix),
                postfix: postfix == null ? null : AccessTools.Method(own, postfix),
                transpiler: transpiler == null ? null : AccessTools.Method(own, transpiler)))
                throw new InvalidOperationException("Engine Sim hook failed: " + type.Name + "." + name);
        }
        private static bool ScheduledOnly(object __instance) { return !_ready || !ReferenceEquals(__instance, _manager) || _dispatch; }
        private static bool CouplingAuthority(object __instance)
        {
            if (!_ready || !Multiplayer.Active) return true;
            var vc = _sessionVehicle(_couplingSession(__instance));
            return !MultiplayerSaving.Frozen && vc != null && !Multiplayer.OwnedElsewhere(vc.gameObject);
        }
        private static bool WheelAuthority(Component __instance)
        { return !Multiplayer.Active || !MultiplayerSaving.Frozen && __instance != null && !Multiplayer.OwnedElsewhere(__instance.gameObject); }
        private static Func<object, IDictionary> _sessions;
        private static Func<object, float> _condition;
        private static Action<object, float, float, bool, float> _sessionControls;
        private static bool FeedAuthority(object __0)
        {
            if (!_ready || !Multiplayer.Active) return true;
            var vc = _sessionVehicle(__0); if (vc == null) return false;
            if (!MultiplayerSaving.Frozen && !Multiplayer.OwnedElsewhere(vc.gameObject)) return true;
            RealisticEngineSound sound = null;
            foreach (DictionaryEntry entry in _sessions(_manager)) if (ReferenceEquals(entry.Value, __0)) { sound = entry.Key as RealisticEngineSound; break; }
            float rpm = sound != null ? sound.engineCurrentRPM : 0;
            _sessionControls(__0, rpm, MultiplayerSaving.Frozen ? 0f : vc.input.Throttle,
                !MultiplayerSaving.Frozen && sound != null && sound.gameObject.activeSelf && rpm > 0f, _condition(__0));
            return false;
        }
        private static IEnumerable<CodeInstruction> OwnerTorque(IEnumerable<CodeInstruction> instructions)
        {
            var target = AccessTools.Method(typeof(Rigidbody), "AddTorque", new[] { typeof(Vector3), typeof(ForceMode) });
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(target)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(EngineSimCompatModule), "ApplyTorque"); }
                yield return instruction;
            }
        }
        private static void ApplyTorque(Rigidbody body, Vector3 force, ForceMode mode)
        { if (body != null && (!Multiplayer.Active || !MultiplayerSaving.Frozen && !Multiplayer.OwnedElsewhere(body.gameObject))) body.AddTorque(force, mode); }
        private static void Run(Action<object> tick)
        {
            if (!_ready || (_manager as Component) == null) return;
            _dispatch = true;
            try { tick(_manager); }
            catch
            {
                _ready = false;
                ClearStarts();
                if (ReferenceEquals(Services.VehicleIgnition, _owner)) Services.VehicleIgnition = null;
                Log.Warn("[EngineSimCompat] Manager callback failed; original Engine Sim callbacks restored");
                throw;
            }
            finally { _dispatch = false; }
        }
        private static void Frame() { Run(_update); }
        private static void PhysicsFrame() { Run(_fixed); }
        private static bool CacheStep()
        {
            if (!_ready) return false;
            try { return EngineDiscovery.Step(); }
            catch
            {
                _ready = false; ClearStarts();
                if (ReferenceEquals(Services.VehicleIgnition, _owner)) Services.VehicleIgnition = null;
                Log.Warn("[EngineSimCompat] World cache failed; original Engine Sim callbacks and discovery restored");
                throw;
            }
        }
        private static RealisticEngineSound[] Sounds() { return _ready ? EngineDiscovery.Snapshot : Resources.FindObjectsOfTypeAll<RealisticEngineSound>(); }
        private static bool FindListener(ref AudioListener __result) { if (!_ready) return true; __result = EngineDiscovery.Listener(); return false; }
        private static Camera CachedMainCamera() { return GameRefs.MainCamera; }
        private static IEnumerable<CodeInstruction> CachedScan(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var i in instructions)
            {
                var m = i.operand as MethodInfo;
                if (m != null && m.DeclaringType == typeof(Resources) && m.Name == "FindObjectsOfTypeAll" && m.ReturnType == typeof(RealisticEngineSound[]))
                { i.opcode = OpCodes.Call; i.operand = AccessTools.Method(typeof(EngineSimCompatModule), "Sounds"); count++; }
                yield return i;
            }
            if (count != 1) throw new NotSupportedException("Engine Sim scan interface changed");
        }
        private static IEnumerable<CodeInstruction> CachedCamera(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var i in instructions)
            {
                var m = i.operand as MethodInfo;
                if (m != null && m.DeclaringType == typeof(UnityEngine.Camera) && m.Name == "get_main")
                { i.opcode = OpCodes.Call; i.operand = AccessTools.Method(typeof(EngineSimCompatModule), "CachedMainCamera"); count++; }
                yield return i;
            }
            if (count != 1) throw new NotSupportedException("Engine Sim camera interface changed");
        }
        private static bool InputAllowed { get { return GameRefs.InGame && !ApocasetterBridge.InputBlocked && Application.isFocused; } }
        private static bool PlayerKey(object sw) { return _ready && _playerFrame == Time.frameCount && ReferenceEquals(sw, _playerSwitch); }
        private static bool MouseDown(int button, object sw) { return !_ready ? Input.GetMouseButtonDown(button) : InputAllowed && (Input.GetMouseButtonDown(button) || (PlayerKey(sw) && _down)); }
        private static bool MouseHeld(int button, object sw) { return !_ready ? Input.GetMouseButton(button) : InputAllowed && (Input.GetMouseButton(button) || (PlayerKey(sw) && _held)); }
        private static IEnumerable<CodeInstruction> SwitchInput(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var i in instructions)
            {
                var m = i.operand as MethodInfo;
                if (m != null && m.DeclaringType == typeof(Input) && (m.Name == "GetMouseButtonDown" || m.Name == "GetMouseButton"))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(i.labels); i.labels.Clear();
                    load.blocks.AddRange(i.blocks); i.blocks.Clear(); yield return load;
                    i.opcode = OpCodes.Call; i.operand = AccessTools.Method(typeof(EngineSimCompatModule), m.Name == "GetMouseButtonDown" ? "MouseDown" : "MouseHeld"); count++;
                }
                yield return i;
            }
            if (count != 2) throw new NotSupportedException("Engine Sim switch input interface changed");
        }
        private static void KeyboardPointed(object __instance, ref bool __result)
        { if (PlayerKey(__instance) && InputAllowed && (_down || _held)) __result = true; }
        private static bool SetText(object __instance, string __0)
        {
            if (!_ready) return true;
            _shown(__instance, __0);
            var text = _itemText(__instance) as Text;
            if (text != null) text.text = __0;
            return false;
        }
        private static bool BeforeSwitch(object __instance)
        {
            if (!_ready) return true;
            if (PlayerKey(__instance)) Starts.Remove(__instance);
            return true;
        }
        private static void AfterSwitch(object __instance)
        {
            if (!_ready) return;
            StartRequest request;
            if (!Starts.TryGetValue(__instance, out request)) return;
            bool crank = request.Engine != null && _on(__instance) && !Turning(request.Engine) && Time.time < request.Until;
            _setCranking(__instance, crank);
            if (!crank) Starts.Remove(__instance);
        }
        private static void Released(object __instance)
        {
            if (!_ready) return;
            var sw = _switch(__instance); if (sw == null) return;
            Starts.Remove(sw);
            if (ReferenceEquals(sw, _playerSwitch)) { _playerSwitch = null; _playerFrame = -1; }
        }
        private static void VehicleEnabled(VehicleController __instance) { if (_ready) EngineDiscovery.Queue(__instance.transform); }
        private static void SoundEnabled(RealisticEngineSound __instance) { if (_ready) EngineDiscovery.Observe(__instance); }
        private static bool Turning(EngineComponent engine) { return engine.OutputRPM > .5f * engine.idleRPM; }
        private static object Coupling(GameObject car, out EngineComponent engine)
        {
            engine = null; if (!_ready || car == null) return null;
            var vc = car.GetComponent<VehicleController>();
            if (vc == null || vc.powertrain == null || vc.powertrain.engine == null) return null;
            engine = vc.powertrain.engine;
            var result = _active[engine];
            return result != null && !_partGone(result) ? result : null;
        }
        public bool TryPlayer(GameObject car, bool down, bool held, out bool keyOn, out bool running, out bool cranking)
        {
            keyOn = running = cranking = false;
            EngineComponent engine; var coupling = Coupling(car, out engine); if (coupling == null) return false;
            var sw = _switch(coupling); if (sw == null) return false;
            _playerSwitch = sw; _playerFrame = Time.frameCount; _down = down; _held = held;
            keyOn = _keyOn(coupling); running = keyOn && Turning(engine); cranking = _cranking(sw); return true;
        }
        public bool TryRunning(GameObject car, out bool running)
        {
            running = false; EngineComponent engine;
            var coupling = Coupling(car, out engine); if (coupling == null) return false;
            running = _keyOn(coupling) && Turning(engine); return true;
        }
        public bool TryStart(GameObject car)
        {
            if (Multiplayer.OwnedElsewhere(car)) return false;
            EngineComponent engine; var coupling = Coupling(car, out engine); if (coupling == null) return false;
            var sw = _switch(coupling); if (sw == null) return false;
            if (!_on(sw)) _turn(sw, true);
            if (!Turning(engine) && !Starts.ContainsKey(sw)) Starts.Add(sw, new StartRequest { Engine = engine, Until = Time.time + 8f });
            return true;
        }
        public bool TryStop(GameObject car)
        {
            if (Multiplayer.OwnedElsewhere(car)) return false;
            EngineComponent engine; var coupling = Coupling(car, out engine); if (coupling == null) return false;
            var sw = _switch(coupling); if (sw == null) return false;
            Starts.Remove(sw); _setPressed(sw, false); _setCranking(sw, false);
            if (_on(sw)) _turn(sw, false);
            return true;
        }
        private static void ClearStarts()
        {
            foreach (var request in Starts) { _setCranking(request.Key, false); _setPressed(request.Key, false); }
            Starts.Clear();
        }
        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode) { if (_ready) EngineDiscovery.Init(); ClearStarts(); _playerSwitch = null; _playerFrame = -1; }
        public override void Shutdown()
        {
            _ready = false;
            if (ReferenceEquals(Services.VehicleIgnition, this)) Services.VehicleIgnition = null;
            ClearStarts(); EngineDiscovery.Clear(); _manager = _playerSwitch = null; _active = null; _owner = null; _dispatch = false; _playerFrame = -1;
        }

        private static Func<object, T> Get<T>(Type type, string name)
        {
            var p = Expression.Parameter(typeof(object), "p"); var field = AccessTools.Field(type, name);
            Expression value = field != null ? (Expression)Expression.Field(field.IsStatic ? null : Expression.Convert(p, type), field)
                : Expression.Property(Expression.Convert(p, type), AccessTools.Property(type, name));
            return Expression.Lambda<Func<object, T>>(Expression.Convert(value, typeof(T)), p).Compile();
        }
        private static Action<object, T> Set<T>(Type type, string name)
        {
            var p = Expression.Parameter(typeof(object), "p"); var v = Expression.Parameter(typeof(T), "v"); var field = AccessTools.Field(type, name);
            var target = Expression.Convert(p, type);
            Expression assign = field != null ? (Expression)Expression.Assign(Expression.Field(target, field), v)
                : Expression.Call(target, AccessTools.Property(type, name).GetSetMethod(true), v);
            return Expression.Lambda<Action<object, T>>(assign, p, v).Compile();
        }
        private static Action<object> Call(Type type, string name)
        {
            var p = Expression.Parameter(typeof(object), "p");
            return Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(p, type), AccessTools.Method(type, name)), p).Compile();
        }
        private static Action<object, T> Call<T>(Type type, string name)
        {
            var p = Expression.Parameter(typeof(object), "p"); var v = Expression.Parameter(typeof(T), "v");
            return Expression.Lambda<Action<object, T>>(Expression.Call(Expression.Convert(p, type), AccessTools.Method(type, name), v), p, v).Compile();
        }
    }
}
