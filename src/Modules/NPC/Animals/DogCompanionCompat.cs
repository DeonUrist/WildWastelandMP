using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using WildWasteland.Core;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WildWasteland.Modules.NPC
{
    // Dog Companion strips the hound's Rigidbody and FSMs. Only this adapter knows
    // its private API; both native hounds and the companion use DogAnimations.
    internal static class DogCompanionCompat
    {
        private static readonly Dictionary<int, Driver> Drivers = new Dictionary<int, Driver>();
        private static FieldInfo target, mode, lunge, lastFight, nextBite, targetGo, pawDepth;
        private static PropertyInfo feet, alive;
        private static MethodInfo step, setMoving, unstick, stuck, giveUp, hurt, snarl;
        private static ConfigEntry<float> runSpeed, biteDamage, leash;
        private static bool warnedBundle;
        private static PropertyInfo knownPlayer;
        // (WW perf, N12) the private API as compiled delegates (built once in Initialize; a member that cannot be compiled falls back to its
        // reflection call - the same result): Riding / SyncTarget / Feet ... run several times per frame per companion dog
        private static Func<object, object> _target, _targetGo, _knownPlayer;
        private static Func<object, int> _mode;
        private static Func<object, float> _nextBite, _pawDepth;
        private static Action<object, float> _setNextBite, _setLunge, _setLastFight, _giveUp, _hurt;
        private static Func<object, Vector3> _feet;
        private static Func<object, bool> _alive;
        private static Action<object, Vector3, float, float> _step;
        private static Action<object, bool, float> _setMoving;
        private static Action<object> _unstick, _snarl;
        private static Func<object, float, bool> _stuck;

        // (WW) patched through Core (module patches, removed by Core on shutdown). The whole contract is resolved before any patch; a failed
        // patch turns the bridge off (handlers return at once) instead of UnpatchSelf. [NPC] DogCompanionCompat switches it live.
        private static bool _ok;
        internal static bool Present { get { return _ok; } }
        private static bool On { get { return _ok && NpcModule.Running && NpcModule.DogCompanionCompatEnabled.Value; } }

        internal static void Initialize(NpcModule module)
        {
            _ok = false;
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue("apocalypter.dogcompanion", out info) || ReferenceEquals(info.Instance, null)) return;
            try
            {
                var assembly = info.Instance.GetType().Assembly;
                var dog = assembly.GetType("DogCompanion.Dog", true);
                var npc = assembly.GetType("DogCompanion.Npc", true);
                var config = assembly.GetType("DogCompanion.Plugin", true);
                knownPlayer = Property(assembly.GetType("DogCompanion.Game", true), "KnownPlayer");
                target = Field(dog, "_target"); mode = Field(dog, "_mode"); lunge = Field(dog, "_lunge");
                lastFight = Field(dog, "_lastFight"); nextBite = Field(dog, "_nextBite"); targetGo = Field(npc, "Go");
                pawDepth = Field(dog, "_pawDepth");
                feet = Property(dog, "Feet"); alive = Property(npc, "Alive");
                step = Method(dog, "Step"); setMoving = Method(dog, "SetMoving"); unstick = Method(dog, "Unstick");
                stuck = Method(dog, "Stuck"); giveUp = Method(dog, "GiveUp"); hurt = Method(npc, "Hurt"); snarl = Method(dog, "Snarl");
                runSpeed = (ConfigEntry<float>)Field(config, "RunSpeed").GetValue(null);
                biteDamage = (ConfigEntry<float>)Field(config, "BiteDamage").GetValue(null);
                leash = (ConfigEntry<float>)Field(config, "LeashDistance").GetValue(null);
                // Resolve the complete contract before installing any hooks.
                var update = Method(dog, "Update"); var chase = Method(dog, "ChaseTick");
                var late = Method(dog, "LateUpdate"); var disable = Method(dog, "OnDisable"); var steer = Method(dog, "Steer");
                var ride = Method(dog, "RideTick");
                var distraction = Method(assembly.GetType("DogCompanion.Distraction", true), "Nearest");
                bool all = true;
                all &= Core.Patches.Patch(module, update, prefix: Hook(nameof(BeforeUpdate)), postfix: Hook(nameof(AfterUpdate)));
                all &= Core.Patches.Patch(module, chase, prefix: Hook(nameof(BeforeChase)));
                all &= Core.Patches.Patch(module, late, postfix: Hook(nameof(AfterLateUpdate)));
                all &= Core.Patches.Patch(module, disable, prefix: Hook(nameof(BeforeDisable)));
                all &= Core.Patches.Patch(module, steer, prefix: Hook(nameof(BeforeSteer)));
                all &= Core.Patches.Patch(module, ride, postfix: Hook(nameof(AfterRideTick)));
                all &= Core.Patches.Patch(module, step, prefix: Hook(nameof(BeforeStep)));
                all &= Core.Patches.Patch(module, distraction, prefix: Hook(nameof(BeforeDistraction)));
                if (!all) throw new InvalidOperationException("a Harmony patch was refused");
                Compile();
                _ok = true;
                NpcModule.DogCompanionCompatEnabled.SettingChanged += OnToggle;
                NpcModule.Log.LogInfo("Dog Companion compatibility enabled (" + info.Metadata.Version + "): shared canine locomotion/combat and sitting in vehicles.");
            }
            catch (Exception e)
            {
                _ok = false;
                NpcModule.Log.LogWarning("Dog Companion compatibility unavailable; keeping its original behavior: " + e.Message);
            }
        }

        // switched off live: every companion dog is released (Dog Companion's own behaviour); switched on: they are taken again on their next Update
        private static void OnToggle(object s, EventArgs a) { if (!NpcModule.DogCompanionCompatEnabled.Value) ReleaseAll(); }
        private static void ReleaseAll()
        {
            foreach (var d in Drivers.Values) { try { if (d.Dog != null) DogAnimations.RemoveCompanion(d.Dog.gameObject); } catch (Exception) { } }
            Drivers.Clear();
        }
        internal static void Shutdown()
        {
            ReleaseAll();
            if (_ok) { try { NpcModule.DogCompanionCompatEnabled.SettingChanged -= OnToggle; } catch (Exception) { } }
            _ok = false;
        }
        internal static int Count { get { return Drivers.Count; } }

        private static FieldInfo Field(Type type, string name)
        { return AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name); }
        private static PropertyInfo Property(Type type, string name)
        { return AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name); }
        private static MethodInfo Method(Type type, string name)
        { return AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name); }
        private static MethodInfo Hook(string name) { return AccessTools.Method(typeof(DogCompanionCompat), name); }

        // ---------- (WW perf) compiled accessors ----------
        private static Expression Inst(ParameterExpression o, MemberInfo m, bool isStatic) { return isStatic ? null : Expression.Convert(o, m.DeclaringType); }
        private static Func<object, T> FieldGet<T>(FieldInfo f)
        {
            try
            {
                var o = Expression.Parameter(typeof(object), "o");
                Expression e = Expression.Field(Inst(o, f, f.IsStatic), f);
                if (e.Type != typeof(T)) e = Expression.Convert(e, typeof(T));
                return Expression.Lambda<Func<object, T>>(e, o).Compile();
            }
            catch (Exception) { return null; }
        }
        private static Action<object, float> FieldSetFloat(FieldInfo f)
        {
            try
            {
                if (f.FieldType != typeof(float) || f.IsInitOnly) return null;
                var o = Expression.Parameter(typeof(object), "o"); var v = Expression.Parameter(typeof(float), "v");
                return Expression.Lambda<Action<object, float>>(Expression.Assign(Expression.Field(Inst(o, f, f.IsStatic), f), v), o, v).Compile();
            }
            catch (Exception) { return null; }
        }
        private static Func<object, T> PropGet<T>(PropertyInfo pi)
        {
            try
            {
                var o = Expression.Parameter(typeof(object), "o");
                var g = pi.GetGetMethod(true);
                Expression e = Expression.Property(Inst(o, pi, g.IsStatic), pi);
                if (e.Type != typeof(T)) e = Expression.Convert(e, typeof(T));
                return Expression.Lambda<Func<object, T>>(e, o).Compile();
            }
            catch (Exception) { return null; }
        }
        private static TDelegate Call<TDelegate>(MethodInfo m, params Type[] args) where TDelegate : class
        {
            try
            {
                var o = Expression.Parameter(typeof(object), "o");
                var ps = new ParameterExpression[args.Length + 1]; ps[0] = o;
                var call = new Expression[args.Length];
                var mp = m.GetParameters();
                if (mp.Length != args.Length) return null;
                for (int i = 0; i < args.Length; i++) { ps[i + 1] = Expression.Parameter(args[i], "a" + i); if (mp[i].ParameterType != args[i]) return null; call[i] = ps[i + 1]; }
                return Expression.Lambda<TDelegate>(Expression.Call(Inst(o, m, m.IsStatic), m, call), ps).Compile();
            }
            catch (Exception) { return null; }
        }
        private static void Compile()
        {
            var ft = target; var fm = mode; var fl = lunge; var flf = lastFight; var fnb = nextBite; var fg = targetGo; var fpd = pawDepth;
            var pf = feet; var pa = alive; var pk = knownPlayer;
            var ms = step; var msm = setMoving; var mu = unstick; var mst = stuck; var mg = giveUp; var mh = hurt; var msn = snarl;
            _target = FieldGet<object>(ft) ?? (o => ft.GetValue(o));
            _targetGo = FieldGet<object>(fg) ?? (o => fg.GetValue(o));
            _mode = FieldGet<int>(fm) ?? (o => Convert.ToInt32(fm.GetValue(o)));
            _nextBite = FieldGet<float>(fnb) ?? (o => (float)fnb.GetValue(o));
            _pawDepth = FieldGet<float>(fpd) ?? (o => (float)fpd.GetValue(o));
            _setNextBite = FieldSetFloat(fnb) ?? ((o, v) => fnb.SetValue(o, v));
            _setLunge = FieldSetFloat(fl) ?? ((o, v) => fl.SetValue(o, v));
            _setLastFight = FieldSetFloat(flf) ?? ((o, v) => flf.SetValue(o, v));
            _feet = PropGet<Vector3>(pf) ?? (o => (Vector3)pf.GetValue(o, null));
            _alive = PropGet<bool>(pa) ?? (o => (bool)pa.GetValue(o, null));
            _knownPlayer = PropGet<object>(pk) ?? (o => pk.GetValue(null, null));
            _step = Call<Action<object, Vector3, float, float>>(ms, typeof(Vector3), typeof(float), typeof(float)) ?? ((o, a, b, c) => ms.Invoke(o, new object[] { a, b, c }));
            _setMoving = Call<Action<object, bool, float>>(msm, typeof(bool), typeof(float)) ?? ((o, a, b) => msm.Invoke(o, new object[] { a, b }));
            _unstick = Call<Action<object>>(mu) ?? (o => mu.Invoke(o, null));
            _snarl = Call<Action<object>>(msn) ?? (o => msn.Invoke(o, null));
            _stuck = Call<Func<object, float, bool>>(mst, typeof(float)) ?? ((o, a) => (bool)mst.Invoke(o, new object[] { a }));
            _giveUp = Call<Action<object, float>>(mg, typeof(float)) ?? ((o, a) => mg.Invoke(o, new object[] { a }));
            _hurt = Call<Action<object, float>>(mh, typeof(float)) ?? ((o, a) => mh.Invoke(o, new object[] { a }));
        }

        internal sealed class Driver
        {
            internal readonly MonoBehaviour Dog;
            internal readonly Senses.Agent Agent;
            internal bool Held, Failed, FixedDash;
            internal float Speed;
            internal Vector3 Before;
            internal GameObject DashTarget;
            internal bool WantsSneak;
            private GameObject movementPlayer;
            private PlayMakerFSM playerMovement;
            private FsmFloat standingHeight;
            internal Driver(MonoBehaviour dog)
            { Dog = dog; Agent = new Senses.Agent { Owner = dog.gameObject, T = dog.transform, Col = dog.GetComponent<Collider>() }; }
            internal bool Active { get { return !Failed && Dog != null && Dog.isActiveAndEnabled; } }
            internal bool Riding { get { return _mode(Dog) == 1; } }
            internal bool Sneaking
            {
                get
                {
                    object npc = _target(Dog);
                    return WantsSneak && !Riding && Agent.Target == null && (npc == null || !_alive(npc));
                }
            }
            internal void RefreshSneaking()
            {
                var player = Senses.PlayerObj ?? (GameObject)_knownPlayer(null);
                if (player != movementPlayer || (player != null && playerMovement == null))
                {
                    movementPlayer = player; playerMovement = null; standingHeight = null;
                    if (player != null) foreach (var fsm in player.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Movement") { playerMovement = fsm; standingHeight = fsm.FsmVariables.FindFsmFloat("standingHeight"); break; }
                }
                string state = playerMovement != null && playerMovement.enabled ? playerMovement.ActiveStateName ?? "" : "";
                WantsSneak = state.StartsWith("Crouch", StringComparison.OrdinalIgnoreCase) || state.StartsWith("Crawl", StringComparison.OrdinalIgnoreCase)
                    || (playerMovement != null && playerMovement.enabled && standingHeight != null && standingHeight.Value > .1f && standingHeight.Value < 1.4f);
            }
            internal Vector3 Feet { get { return _feet(Dog); } }
            internal float PawHeight { get { return -_pawDepth(Dog); } }
            internal float RunSpeed { get { return runSpeed.Value * 1.1f; } }
            internal float NativeNextBite { get { return _nextBite(Dog); } }
            internal void SetCooldown(float until) { _setNextBite(Dog, until); }
            internal void SyncTarget()
            {
                object npc = Riding ? null : _target(Dog);
                Agent.Target = npc != null && _alive(npc) ? (GameObject)_targetGo(npc) : null;
                Agent.State = Agent.Target != null ? Senses.State.Combat : Senses.State.Idle;
            }
            internal bool TargetAlive(GameObject victim)
            {
                object npc = _target(Dog);
                return victim != null && npc != null && (GameObject)_targetGo(npc) == victim && _alive(npc);
            }
            internal void Halt(bool hold)
            { Held = hold; if (hold && Active) _setMoving(Dog, false, 0f); }
            internal void Contact(GameObject victim)
            {
                if (!Active || Riding || !TargetAlive(victim)) return;
                if (!Core.VehicleMeleeProtection.CanHit(victim)) return;
                _hurt(_target(Dog), biteDamage.Value);
                if (Senses.PlayerObj != null) Senses.Hurt(victim, Senses.PlayerObj);
                _snarl(Dog);
            }
            internal void Dash(Vector3 direction, float speed, float dt, GameObject victim)
            {
                if (!Active || Riding) return;
                FixedDash = true; DashTarget = victim;
                try { _step(Dog, Feet + direction * 5f, speed, dt); }
                finally { FixedDash = false; DashTarget = null; }
                Dog.transform.rotation = Quaternion.LookRotation(direction);
            }
            internal bool Chase(Transform player, float dt)
            {
                SyncTarget(); if (Agent.Target == null) return false;
                _setLastFight(Dog, Time.time);
                Vector3 distance = Agent.Target.transform.position - player.position; distance.y = 0f;
                if (distance.magnitude > leash.Value) { _giveUp(Dog, 5f); SyncTarget(); return false; }
                // The shared animation state owns holding and committed dashes. All
                // other chasing keeps Dog Companion's obstacle/ground navigation.
                if (!Held && !DogAnimations.CompanionDashing(Dog.gameObject))
                {
                    _step(Dog, Agent.Target.transform.position, RunSpeed, dt);
                    if (_stuck(Dog, 3f)) { _giveUp(Dog, 6f); SyncTarget(); }
                }
                else _unstick(Dog);
                return false;
            }
        }
        private static Driver Get(MonoBehaviour dog)
        {
            if (dog == null) return null;
            Driver driver; int id = dog.GetInstanceID();
            if (!Drivers.TryGetValue(id, out driver))
            {
                driver = new Driver(dog);
                if (!DogAnimations.RegisterCompanion(driver.Agent, driver))
                {
                    if (!warnedBundle) { warnedBundle = true; NpcModule.Log.LogWarning("Dog Companion needs the updated npcai_dogs.bundle (including sit clips); keeping its original behavior."); }
                    driver.Failed = true;
                }
                Drivers[id] = driver;
            }
            return driver;
        }
        private static bool Owns(Driver d) { return d != null && !d.Failed && DogAnimations.Owns(d.Dog.gameObject); }
        private static void Fail(Driver d, Exception e)
        {
            if (d == null) return;
            d.Failed = true; DogAnimations.RemoveCompanion(d.Dog.gameObject);
            NpcModule.Log.LogWarning("Dog Companion compatibility released this dog after an error: " + e.GetBaseException().Message);
        }
        public static void BeforeUpdate(MonoBehaviour __instance)
        {
            if (!On) return;
            Driver d = null;
            try { d = Get(__instance); if (d == null) return; d.SyncTarget(); d.RefreshSneaking(); DogAnimations.RestoreCompanion(__instance.gameObject); d.Before = __instance.transform.position; }
            catch (Exception e) { Fail(d, e); }
        }
        public static void AfterUpdate(MonoBehaviour __instance)
        {
            if (!On) return;
            Driver d;
            if (!Drivers.TryGetValue(__instance.GetInstanceID(), out d) || d.Failed) return;
            try
            {
                d.SyncTarget(); Vector3 moved = __instance.transform.position - d.Before; moved.y = 0f;
                d.Speed = d.Riding || moved.magnitude > 2f ? 0f : moved.magnitude / Mathf.Max(.001f, Time.deltaTime);
                if (Owns(d)) _setLunge(__instance, 1f); // suppress the old procedural jolt
                DogAnimations.TickCompanion(__instance.gameObject);
            }
            catch (Exception e) { Fail(d, e); }
        }
        public static bool BeforeChase(MonoBehaviour __instance, Transform __0, float __1)
        {
            if (!On) return true;
            Driver d = null;
            try { d = Get(__instance); return !Owns(d) || d.Chase(__0, __1); }
            catch (Exception e) { Fail(d, e); return true; }
        }
        public static bool BeforeSteer(MonoBehaviour __instance, Vector3 __0, Vector3 __1, ref Vector3 __result)
        {
            if (!On) return true;
            Driver d;
            if (!Drivers.TryGetValue(__instance.GetInstanceID(), out d) || !d.FixedDash) return true;
            RaycastHit hit;
            bool blocked = Physics.SphereCast(__0 + Vector3.up * .5f, .22f, __1, out hit, .4f,
                (1 << 0) | (1 << 8) | (1 << 9) | (1 << 11) | (1 << 14) | (1 << 16), QueryTriggerInteraction.Ignore);
            if (blocked && (hit.normal.y > .6f || (hit.collider != null && d.DashTarget != null && (hit.collider.gameObject == d.DashTarget || hit.collider.transform.IsChildOf(d.DashTarget.transform))))) blocked = false;
            __result = blocked ? Vector3.zero : __1;
            return false;
        }
        public static void BeforeStep(MonoBehaviour __instance, ref float __1)
        {
            if (!On) return;
            Driver d;
            if (Drivers.TryGetValue(__instance.GetInstanceID(), out d) && Owns(d) && d.Sneaking) __1 = Mathf.Min(__1, 1.6f);
        }
        public static bool BeforeDistraction(FsmStateAction __0)
        {
            if (!On) return true;
            var fsm = __0 != null ? __0.Fsm : null;
            return fsm == null || !Senses.On || !NpcModule.BrainEnabled.Value || Senses.AgentFor(fsm.GameObject) == null;
        }
        internal static GameObject CloseThreat(GameObject enemy, float range)
        {
            if (enemy == null) return null;
            foreach (var d in Drivers.Values)
                if (Owns(d) && !d.Riding && (d.Dog.transform.position - enemy.transform.position).sqrMagnitude <= range * range) return d.Dog.gameObject;
            return null;
        }
        public static void AfterLateUpdate(MonoBehaviour __instance)
        {
            if (!On) return;
            Driver d;
            if (!Drivers.TryGetValue(__instance.GetInstanceID(), out d) || d.Failed) return;
            try { DogAnimations.LateCompanion(__instance.gameObject); }
            catch (Exception e) { Fail(d, e); }
        }
        public static void AfterRideTick(MonoBehaviour __instance)
        {
            if (!On) return;
            Driver d = null;
            try
            {
                d = Get(__instance);
                if (Owns(d) && d.Riding) __instance.transform.localPosition += new Vector3(0f, DogCompanionSeating.UpOffset, DogCompanionSeating.ForwardOffset);
            }
            catch (Exception e) { Fail(d, e); }
        }
        public static void BeforeDisable(MonoBehaviour __instance)
        {
            if (!_ok) return;
            DogAnimations.RemoveCompanion(__instance.gameObject);
            Drivers.Remove(__instance.GetInstanceID());
        }
        internal static void OnSceneLoaded() { Drivers.Clear(); }
    }
}
