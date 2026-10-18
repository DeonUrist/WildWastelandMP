using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Micosmo.SensorToolkit;
using Micosmo.SensorToolkit.PlayMaker;
using UnityEngine;

namespace WildWasteland.Core
{
    /// <summary>
    /// Shared Harmony dispatchers (AGENTS.md 5.6): ONE patch per target per kind for the whole mod. Modules subscribe
    /// handlers instead of patching these targets themselves. Semantics match HarmonyX: every prefix handler runs
    /// (in module init order) and the original is skipped when any returned false; postfix/finalizer handlers always run.
    /// Handlers must start with a cheap filter (static bool, reference compare, cached set) - they run for every FSM in the game.
    /// A module's handlers are removed with <see cref="RemoveAll"/> when it shuts down.
    /// </summary>
    public static class Hooks
    {
        public delegate bool PrefixHandler<T>(T action);
        public delegate void PostfixHandler<T>(T action);
        public delegate Exception FinalizerHandler<T>(T action, Exception exception);

        public sealed class Hook<T> where T : class
        {
            public readonly string Target;
            // owners are kept in arrays parallel to the handler arrays and swapped together, so a dispatch that is running while a
            // handler's module gets removed (Fail -> Stop -> RemoveAll from inside the catch) still reports to the right owner
            IModule[] _po = new IModule[0], _pre = new IModule[0], _fo = new IModule[0];
            PrefixHandler<T>[] _prefix = new PrefixHandler<T>[0];
            PostfixHandler<T>[] _postfix = new PostfixHandler<T>[0];
            FinalizerHandler<T>[] _final = new FinalizerHandler<T>[0];
            public bool Applied { get; internal set; }
            public long Calls;

            internal Hook(string target) { Target = target; }
            public int PrefixCount { get { return _prefix.Length; } }
            public int PostfixCount { get { return _postfix.Length; } }
            public int FinalizerCount { get { return _final.Length; } }
            public IEnumerable<IModule> Subscribers
            {
                get { var s = new HashSet<IModule>(); foreach (var m in _pre) s.Add(m); foreach (var m in _po) s.Add(m); foreach (var m in _fo) s.Add(m); return s; }
            }

            public void Prefix(IModule owner, PrefixHandler<T> h)
            {
                var a = new PrefixHandler<T>[_prefix.Length + 1]; _prefix.CopyTo(a, 0); a[a.Length - 1] = h;
                var o = Owners.Append(_pre, owner); _pre = o; _prefix = a;
            }
            public void Postfix(IModule owner, PostfixHandler<T> h)
            {
                var a = new PostfixHandler<T>[_postfix.Length + 1]; _postfix.CopyTo(a, 0); a[a.Length - 1] = h;
                var o = Owners.Append(_po, owner); _po = o; _postfix = a;
            }
            public void Finalizer(IModule owner, FinalizerHandler<T> h)
            {
                var a = new FinalizerHandler<T>[_final.Length + 1]; _final.CopyTo(a, 0); a[a.Length - 1] = h;
                var o = Owners.Append(_fo, owner); _fo = o; _final = a;
            }
            internal void Remove(IModule owner)
            {
                IModule[] o;
                _prefix = Filter(_prefix, _pre, owner, out o); _pre = o;
                _postfix = Filter(_postfix, _po, owner, out o); _po = o;
                _final = Filter(_final, _fo, owner, out o); _fo = o;
            }
            static H[] Filter<H>(H[] arr, IModule[] owners, IModule owner, out IModule[] keptOwners)
            {
                var keep = new List<H>(); var ko = new List<IModule>();
                for (int i = 0; i < arr.Length; i++) if (owners[i] != owner) { keep.Add(arr[i]); ko.Add(owners[i]); }
                keptOwners = ko.ToArray();
                return keep.ToArray();
            }

            public bool RunPrefix(T a)
            {
                Calls++;
                var hs = _prefix; var os = _pre; bool run = true;
                for (int i = 0; i < hs.Length; i++)
                {
                    try { if (!hs[i](a)) run = false; }
                    catch (Exception e) { ModuleHost.ReportHookException(i < os.Length ? os[i] : null, Target, e); }
                }
                return run;
            }
            public void RunPostfix(T a)
            {
                var hs = _postfix; var os = _po;
                for (int i = 0; i < hs.Length; i++)
                {
                    try { hs[i](a); }
                    catch (Exception e) { ModuleHost.ReportHookException(i < os.Length ? os[i] : null, Target, e); }
                }
            }
            public Exception RunFinalizer(T a, Exception ex)
            {
                var hs = _final; var os = _fo;
                for (int i = 0; i < hs.Length; i++)
                {
                    try { ex = hs[i](a, ex); }
                    catch (Exception e) { ModuleHost.ReportHookException(i < os.Length ? os[i] : null, Target, e); }
                }
                return ex;
            }
        }

        // ---- PlayMaker actions (instance = the action) ----
        public static readonly Hook<CreateObject> CreateObjectEnter = new Hook<CreateObject>("CreateObject.OnEnter");
        public static readonly Hook<Explosion> ExplosionDo = new Hook<Explosion>("Explosion.DoExplosion");
        public static readonly Hook<SendEvent> SendEventEnter = new Hook<SendEvent>("SendEvent.OnEnter");
        public static readonly Hook<SendEvent> SendEventUpdate = new Hook<SendEvent>("SendEvent.OnUpdate");
        public static readonly Hook<SetFsmFloat> SetFsmFloatEnter = new Hook<SetFsmFloat>("SetFsmFloat.OnEnter");
        public static readonly Hook<SetFsmFloat> SetFsmFloatDo = new Hook<SetFsmFloat>("SetFsmFloat.DoSetFsmFloat");
        public static readonly Hook<SetVelocity> SetVelocityDo = new Hook<SetVelocity>("SetVelocity.DoSetVelocity");
        public static readonly Hook<GetButtonDown> GetButtonDownUpdate = new Hook<GetButtonDown>("GetButtonDown.OnUpdate");
        public static readonly Hook<GetButton> GetButtonDo = new Hook<GetButton>("GetButton.DoGetButton");
        public static readonly Hook<GetButtonUp> GetButtonUpUpdate = new Hook<GetButtonUp>("GetButtonUp.OnUpdate");
        public static readonly Hook<GetMouseButtonDown> GetMouseButtonDownUpdate = new Hook<GetMouseButtonDown>("GetMouseButtonDown.OnUpdate");
        public static readonly Hook<Raycast> RaycastEnter = new Hook<Raycast>("Raycast.OnEnter");
        public static readonly Hook<Raycast> RaycastDo = new Hook<Raycast>("Raycast.DoRaycast");
        public static readonly Hook<SmoothLookAt> SmoothLookAtDo = new Hook<SmoothLookAt>("SmoothLookAt.DoSmoothLookAt");
        public static readonly Hook<ActivateGameObject> ActivateGameObjectEnter = new Hook<ActivateGameObject>("ActivateGameObject.OnEnter");
        public static readonly Hook<ActivateGameObject> ActivateGameObjectDo = new Hook<ActivateGameObject>("ActivateGameObject.DoActivateGameObject");
        public static readonly Hook<SetPosition> SetPositionDo = new Hook<SetPosition>("SetPosition.DoSetPosition");
        public static readonly Hook<SensorGetDetectionRayHit> SensorRayHitEnter = new Hook<SensorGetDetectionRayHit>("SensorGetDetectionRayHit.OnEnter");
        public static readonly Hook<SensorGetLineOfSightResult> LosEnter3D = new Hook<SensorGetLineOfSightResult>("SensorGetLineOfSightResult.OnEnter3D");
        public static readonly Hook<SensorGetLineOfSightResult> LosUpdate3D = new Hook<SensorGetLineOfSightResult>("SensorGetLineOfSightResult.OnUpdate3D");
        public static readonly Hook<SensorGetDetections> SensorDetectionsDo = new Hook<SensorGetDetections>("SensorGetDetections.DoAction");

        // ---- Fsm core ----
        /// <summary>Fsm.ProcessEvent(FsmEvent, FsmEventData): handlers get the Fsm and the event.</summary>
        public static readonly FsmEventHook ProcessEvent = new FsmEventHook();
        /// <summary>Fsm.EnterState(FsmState): prefix/postfix with (Fsm, FsmState).</summary>
        public static readonly FsmStateHook EnterState = new FsmStateHook();
        /// <summary>ES3ReferenceMgrBase.GetPrefab(long,bool): the first handler that returns a prefab wins.</summary>
        public static readonly PrefabHook GetPrefab = new PrefabHook();
        /// <summary>NWH FloatingOrigin.Jump: Core raises IModule.OnOriginShift; modules needing the raw before/after use these.</summary>
        /// <summary>PlayMakerFSM.OnEnable postfix (after vanilla appended the FSM to FsmList). Postfix handlers only.</summary>
        public static readonly Hook<PlayMakerFSM> FsmEnable = new Hook<PlayMakerFSM>("PlayMakerFSM.OnEnable");
        public static readonly Hook<NWH.Common.FloatingOrigin.FloatingOrigin> OriginJump = new Hook<NWH.Common.FloatingOrigin.FloatingOrigin>("FloatingOrigin.Jump");
        public static readonly Hook<object> SpawnerCatalogEntries = new Hook<object>("Apocaspawner.Catalog.Entries");

        public delegate bool FsmEventPrefix(Fsm fsm, FsmEvent ev, FsmEventData data);
        public sealed class FsmEventHook
        {
            IModule[] _o = new IModule[0];
            FsmEventPrefix[] _h = new FsmEventPrefix[0];
            public long Calls; public bool Applied { get; internal set; }
            public int PrefixCount { get { return _h.Length; } }
            public void Prefix(IModule owner, FsmEventPrefix h) { var a = new FsmEventPrefix[_h.Length + 1]; _h.CopyTo(a, 0); a[a.Length - 1] = h; _o = Owners.Append(_o, owner); _h = a; }
            internal void Remove(IModule owner)
            {
                var keep = new List<FsmEventPrefix>(); var ko = new List<IModule>();
                for (int i = 0; i < _h.Length; i++) if (_o[i] != owner) { keep.Add(_h[i]); ko.Add(_o[i]); }
                _o = ko.ToArray(); _h = keep.ToArray();
            }
            public bool Run(Fsm fsm, FsmEvent ev, FsmEventData data)
            {
                Calls++; var hs = _h; var os = _o; bool run = true;
                for (int i = 0; i < hs.Length; i++) { try { if (!hs[i](fsm, ev, data)) run = false; } catch (Exception e) { ModuleHost.ReportHookException(i < os.Length ? os[i] : null, "Fsm.ProcessEvent", e); } }
                return run;
            }
        }

        public delegate bool FsmStatePrefix(Fsm fsm, FsmState state);
        public delegate void FsmStatePostfix(Fsm fsm, FsmState state);
        public sealed class FsmStateHook
        {
            IModule[] _po = new IModule[0], _pre = new IModule[0];
            FsmStatePrefix[] _p = new FsmStatePrefix[0]; FsmStatePostfix[] _q = new FsmStatePostfix[0];
            public long Calls; public bool Applied { get; internal set; }
            public int PrefixCount { get { return _p.Length; } }
            public int PostfixCount { get { return _q.Length; } }
            public void Prefix(IModule owner, FsmStatePrefix h) { var a = new FsmStatePrefix[_p.Length + 1]; _p.CopyTo(a, 0); a[a.Length - 1] = h; _pre = Owners.Append(_pre, owner); _p = a; }
            public void Postfix(IModule owner, FsmStatePostfix h) { var a = new FsmStatePostfix[_q.Length + 1]; _q.CopyTo(a, 0); a[a.Length - 1] = h; _po = Owners.Append(_po, owner); _q = a; }
            internal void Remove(IModule owner)
            {
                var kp = new List<FsmStatePrefix>(); var kpo = new List<IModule>();
                for (int i = 0; i < _p.Length; i++) if (_pre[i] != owner) { kp.Add(_p[i]); kpo.Add(_pre[i]); }
                var kq = new List<FsmStatePostfix>(); var kqo = new List<IModule>();
                for (int i = 0; i < _q.Length; i++) if (_po[i] != owner) { kq.Add(_q[i]); kqo.Add(_po[i]); }
                _pre = kpo.ToArray(); _po = kqo.ToArray();
                _p = kp.ToArray(); _q = kq.ToArray();
            }
            public bool RunPrefix(Fsm fsm, FsmState st)
            {
                Calls++; var hs = _p; var os = _pre; bool run = true;
                for (int i = 0; i < hs.Length; i++) { try { if (!hs[i](fsm, st)) run = false; } catch (Exception e) { ModuleHost.ReportHookException(i < os.Length ? os[i] : null, "Fsm.EnterState", e); } }
                return run;
            }
            public void RunPostfix(Fsm fsm, FsmState st)
            {
                var hs = _q; var os = _po;
                for (int i = 0; i < hs.Length; i++) { try { hs[i](fsm, st); } catch (Exception e) { ModuleHost.ReportHookException(i < os.Length ? os[i] : null, "Fsm.EnterState", e); } }
            }
        }

        public delegate ES3Internal.ES3Prefab PrefabResolver(long id);
        public sealed class PrefabHook
        {
            IModule[] _o = new IModule[0];
            PrefabResolver[] _h = new PrefabResolver[0];
            public long Calls; public bool Applied { get; internal set; }
            public int PrefixCount { get { return _h.Length; } }
            public void Resolver(IModule owner, PrefabResolver h) { var a = new PrefabResolver[_h.Length + 1]; _h.CopyTo(a, 0); a[a.Length - 1] = h; _o = Owners.Append(_o, owner); _h = a; }
            internal void Remove(IModule owner)
            {
                var keep = new List<PrefabResolver>(); var ko = new List<IModule>();
                for (int i = 0; i < _h.Length; i++) if (_o[i] != owner) { keep.Add(_h[i]); ko.Add(_o[i]); }
                _o = ko.ToArray(); _h = keep.ToArray();
            }
            public ES3Internal.ES3Prefab Run(long id)
            {
                Calls++; var hs = _h; var os = _o;
                for (int i = 0; i < hs.Length; i++)
                {
                    try { var p = hs[i](id); if (p != null) return p; }
                    catch (Exception e) { ModuleHost.ReportHookException(i < os.Length ? os[i] : null, "ES3ReferenceMgrBase.GetPrefab", e); }
                }
                return null;
            }
        }

        static class Owners
        {
            internal static IModule[] Append(IModule[] owners, IModule owner)
            {
                var o = new IModule[owners.Length + 1]; owners.CopyTo(o, 0); o[o.Length - 1] = owner; return o;
            }
        }

        public static void RemoveAll(IModule owner)
        {
            CreateObjectEnter.Remove(owner); ExplosionDo.Remove(owner); SendEventEnter.Remove(owner); SendEventUpdate.Remove(owner); SetFsmFloatEnter.Remove(owner); SetFsmFloatDo.Remove(owner);
            SetVelocityDo.Remove(owner); GetButtonDownUpdate.Remove(owner); GetButtonDo.Remove(owner); GetButtonUpUpdate.Remove(owner); GetMouseButtonDownUpdate.Remove(owner);
            RaycastEnter.Remove(owner); RaycastDo.Remove(owner); SmoothLookAtDo.Remove(owner); ActivateGameObjectEnter.Remove(owner); ActivateGameObjectDo.Remove(owner); SetPositionDo.Remove(owner);
            SensorRayHitEnter.Remove(owner); LosEnter3D.Remove(owner); LosUpdate3D.Remove(owner); SensorDetectionsDo.Remove(owner);
            ProcessEvent.Remove(owner); EnterState.Remove(owner); GetPrefab.Remove(owner); OriginJump.Remove(owner);
            SpawnerCatalogEntries.Remove(owner);
        }

        /// <summary>
        /// Applies the dispatcher patch of every hook that has at least one subscriber and is not patched yet (each separately,
        /// in try/catch). Unused targets are never patched, so a disabled feature costs nothing. Called by ModuleHost after Init.
        /// </summary>
        public static void ApplyUsed()
        {
            Use(CreateObjectEnter.PrefixCount + CreateObjectEnter.PostfixCount, CreateObjectEnter.Applied, typeof(P_CreateObjectEnter), () => CreateObjectEnter.Applied = true);
            Use(ExplosionDo.PrefixCount, ExplosionDo.Applied, typeof(P_ExplosionDo), () => ExplosionDo.Applied = true);
            Use(SendEventEnter.PrefixCount + SendEventEnter.PostfixCount, SendEventEnter.Applied, typeof(P_SendEventEnter), () => SendEventEnter.Applied = true);
            Use(SendEventUpdate.PrefixCount, SendEventUpdate.Applied, typeof(P_SendEventUpdate), () => SendEventUpdate.Applied = true);
            Use(SetFsmFloatEnter.PrefixCount + SetFsmFloatEnter.PostfixCount, SetFsmFloatEnter.Applied, typeof(P_SetFsmFloatEnter), () => SetFsmFloatEnter.Applied = true);
            Use(SetFsmFloatDo.PrefixCount, SetFsmFloatDo.Applied, typeof(P_SetFsmFloatDo), () => SetFsmFloatDo.Applied = true);
            Use(SetVelocityDo.PrefixCount, SetVelocityDo.Applied, typeof(P_SetVelocityDo), () => SetVelocityDo.Applied = true);
            Use(GetButtonDownUpdate.PrefixCount, GetButtonDownUpdate.Applied, typeof(P_GetButtonDownUpdate), () => GetButtonDownUpdate.Applied = true);
            Use(GetButtonDo.PrefixCount, GetButtonDo.Applied, typeof(P_GetButtonDo), () => GetButtonDo.Applied = true);
            Use(GetButtonUpUpdate.PrefixCount, GetButtonUpUpdate.Applied, typeof(P_GetButtonUpUpdate), () => GetButtonUpUpdate.Applied = true);
            Use(GetMouseButtonDownUpdate.PrefixCount, GetMouseButtonDownUpdate.Applied, typeof(P_GetMouseButtonDownUpdate), () => GetMouseButtonDownUpdate.Applied = true);
            Use(RaycastEnter.PrefixCount + RaycastEnter.PostfixCount, RaycastEnter.Applied, typeof(P_RaycastEnter), () => RaycastEnter.Applied = true);
            Use(RaycastDo.PrefixCount, RaycastDo.Applied, typeof(P_RaycastDo), () => RaycastDo.Applied = true);
            Use(SmoothLookAtDo.PrefixCount, SmoothLookAtDo.Applied, typeof(P_SmoothLookAtDo), () => SmoothLookAtDo.Applied = true);
            Use(ActivateGameObjectEnter.PrefixCount + ActivateGameObjectEnter.PostfixCount, ActivateGameObjectEnter.Applied, typeof(P_ActivateGameObjectEnter), () => ActivateGameObjectEnter.Applied = true);
            Use(ActivateGameObjectDo.PrefixCount, ActivateGameObjectDo.Applied, typeof(P_ActivateGameObjectDo), () => ActivateGameObjectDo.Applied = true);
            Use(SetPositionDo.PrefixCount + SetPositionDo.PostfixCount, SetPositionDo.Applied, typeof(P_SetPositionDo), () => SetPositionDo.Applied = true);
            Use(SensorRayHitEnter.PrefixCount + SensorRayHitEnter.FinalizerCount, SensorRayHitEnter.Applied, typeof(P_SensorRayHitEnter), () => SensorRayHitEnter.Applied = true);
            Use(LosEnter3D.PrefixCount, LosEnter3D.Applied, typeof(P_LosEnter3D), () => LosEnter3D.Applied = true);
            Use(LosUpdate3D.PrefixCount, LosUpdate3D.Applied, typeof(P_LosUpdate3D), () => LosUpdate3D.Applied = true);
            Use(SensorDetectionsDo.PrefixCount, SensorDetectionsDo.Applied, typeof(P_SensorDetectionsDo), () => SensorDetectionsDo.Applied = true);
            Use(ProcessEvent.PrefixCount, ProcessEvent.Applied, typeof(P_ProcessEvent), () => ProcessEvent.Applied = true);
            Use(EnterState.PrefixCount + EnterState.PostfixCount, EnterState.Applied, typeof(P_EnterState), () => EnterState.Applied = true);
            Use(GetPrefab.PrefixCount, GetPrefab.Applied, typeof(P_GetPrefab), () => GetPrefab.Applied = true);
            Use(FsmEnable.PostfixCount, FsmEnable.Applied, typeof(P_FsmEnable), () => FsmEnable.Applied = true);
            Use(SpawnerCatalogEntries.PostfixCount, SpawnerCatalogEntries.Applied, typeof(P_SpawnerCatalogEntries), () => SpawnerCatalogEntries.Applied = true);
            // FloatingOrigin.Jump is always patched: Core raises IModule.OnOriginShift from it.
            Use(1, OriginJump.Applied, typeof(P_OriginJump), () => OriginJump.Applied = true);
        }

        static void Use(int subscribers, bool applied, Type patchClass, Action ok)
        {
            if (subscribers == 0 || applied) return;
            if (Patches.ApplyClass(null, patchClass) > 0) ok();
        }

        public static string Summary()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("hooks: ");
            Add(sb, CreateObjectEnter.Target, CreateObjectEnter.Applied, CreateObjectEnter.PrefixCount, CreateObjectEnter.PostfixCount);
            Add(sb, SendEventEnter.Target, SendEventEnter.Applied, SendEventEnter.PrefixCount, SendEventEnter.PostfixCount);
            Add(sb, ExplosionDo.Target, ExplosionDo.Applied, ExplosionDo.PrefixCount, 0);
            Add(sb, SendEventUpdate.Target, SendEventUpdate.Applied, SendEventUpdate.PrefixCount, SendEventUpdate.PostfixCount);
            Add(sb, SetFsmFloatEnter.Target, SetFsmFloatEnter.Applied, SetFsmFloatEnter.PrefixCount, SetFsmFloatEnter.PostfixCount);
            Add(sb, SetFsmFloatDo.Target, SetFsmFloatDo.Applied, SetFsmFloatDo.PrefixCount, SetFsmFloatDo.PostfixCount);
            Add(sb, SetVelocityDo.Target, SetVelocityDo.Applied, SetVelocityDo.PrefixCount, SetVelocityDo.PostfixCount);
            Add(sb, GetButtonDownUpdate.Target, GetButtonDownUpdate.Applied, GetButtonDownUpdate.PrefixCount, GetButtonDownUpdate.PostfixCount);
            Add(sb, GetButtonDo.Target, GetButtonDo.Applied, GetButtonDo.PrefixCount, GetButtonDo.PostfixCount);
            Add(sb, GetButtonUpUpdate.Target, GetButtonUpUpdate.Applied, GetButtonUpUpdate.PrefixCount, GetButtonUpUpdate.PostfixCount);
            Add(sb, GetMouseButtonDownUpdate.Target, GetMouseButtonDownUpdate.Applied, GetMouseButtonDownUpdate.PrefixCount, GetMouseButtonDownUpdate.PostfixCount);
            Add(sb, RaycastEnter.Target, RaycastEnter.Applied, RaycastEnter.PrefixCount, RaycastEnter.PostfixCount);
            Add(sb, RaycastDo.Target, RaycastDo.Applied, RaycastDo.PrefixCount, RaycastDo.PostfixCount);
            Add(sb, SmoothLookAtDo.Target, SmoothLookAtDo.Applied, SmoothLookAtDo.PrefixCount, SmoothLookAtDo.PostfixCount);
            Add(sb, ActivateGameObjectEnter.Target, ActivateGameObjectEnter.Applied, ActivateGameObjectEnter.PrefixCount, ActivateGameObjectEnter.PostfixCount);
            Add(sb, ActivateGameObjectDo.Target, ActivateGameObjectDo.Applied, ActivateGameObjectDo.PrefixCount, ActivateGameObjectDo.PostfixCount);
            Add(sb, SetPositionDo.Target, SetPositionDo.Applied, SetPositionDo.PrefixCount, SetPositionDo.PostfixCount);
            Add(sb, SensorRayHitEnter.Target, SensorRayHitEnter.Applied, SensorRayHitEnter.PrefixCount, SensorRayHitEnter.PostfixCount + SensorRayHitEnter.FinalizerCount);
            Add(sb, LosEnter3D.Target, LosEnter3D.Applied, LosEnter3D.PrefixCount, LosEnter3D.PostfixCount);
            Add(sb, LosUpdate3D.Target, LosUpdate3D.Applied, LosUpdate3D.PrefixCount, LosUpdate3D.PostfixCount);
            Add(sb, SensorDetectionsDo.Target, SensorDetectionsDo.Applied, SensorDetectionsDo.PrefixCount, SensorDetectionsDo.PostfixCount);
            Add(sb, "Fsm.ProcessEvent", ProcessEvent.Applied, ProcessEvent.PrefixCount, 0);
            Add(sb, "Fsm.EnterState", EnterState.Applied, EnterState.PrefixCount, EnterState.PostfixCount);
            Add(sb, "ES3.GetPrefab", GetPrefab.Applied, GetPrefab.PrefixCount, 0);
            Add(sb, FsmEnable.Target, FsmEnable.Applied, FsmEnable.PrefixCount, FsmEnable.PostfixCount);
            Add(sb, OriginJump.Target, OriginJump.Applied, OriginJump.PrefixCount, OriginJump.PostfixCount);
            Add(sb, SpawnerCatalogEntries.Target, SpawnerCatalogEntries.Applied, 0, SpawnerCatalogEntries.PostfixCount);
            return sb.ToString();
        }
        static void Add(System.Text.StringBuilder sb, string name, bool applied, int pre, int post)
        {
            if (pre == 0 && post == 0) return;
            sb.Append(name).Append(applied ? "" : "(NOT PATCHED)").Append('[').Append(pre).Append('/').Append(post).Append("] ");
        }

        // ---- the actual Harmony patch classes: one per target, each a thin forwarder ----
        [HarmonyPatch]
        static class P_SpawnerCatalogEntries
        {
            static MethodBase TargetMethod() { return Patches.Method("Apocaspawner.Catalog", "Entries"); }
            static void Postfix(object __result) { SpawnerCatalogEntries.RunPostfix(__result); }
        }
        [HarmonyPatch(typeof(CreateObject), "OnEnter")] static class P_CreateObjectEnter { static bool Prefix(CreateObject __instance) { return CreateObjectEnter.RunPrefix(__instance); } static void Postfix(CreateObject __instance) { CreateObjectEnter.RunPostfix(__instance); } }
        [HarmonyPatch(typeof(Explosion), "DoExplosion")] static class P_ExplosionDo { static bool Prefix(Explosion __instance) { return ExplosionDo.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(SendEvent), "OnEnter")] static class P_SendEventEnter { static bool Prefix(SendEvent __instance) { return SendEventEnter.RunPrefix(__instance); } static void Postfix(SendEvent __instance) { SendEventEnter.RunPostfix(__instance); } }
        [HarmonyPatch(typeof(SendEvent), "OnUpdate")] static class P_SendEventUpdate { static bool Prefix(SendEvent __instance) { return SendEventUpdate.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(SetFsmFloat), "OnEnter")] static class P_SetFsmFloatEnter { static bool Prefix(SetFsmFloat __instance) { return SetFsmFloatEnter.RunPrefix(__instance); } static void Postfix(SetFsmFloat __instance) { SetFsmFloatEnter.RunPostfix(__instance); } }
        [HarmonyPatch(typeof(SetFsmFloat), "DoSetFsmFloat")] static class P_SetFsmFloatDo { static bool Prefix(SetFsmFloat __instance) { return SetFsmFloatDo.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(SetVelocity), "DoSetVelocity")] static class P_SetVelocityDo { static bool Prefix(SetVelocity __instance) { return SetVelocityDo.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(GetButtonDown), "OnUpdate")] static class P_GetButtonDownUpdate { static bool Prefix(GetButtonDown __instance) { return GetButtonDownUpdate.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(GetButton), "DoGetButton")] static class P_GetButtonDo { static bool Prefix(GetButton __instance) { return GetButtonDo.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(GetButtonUp), "OnUpdate")] static class P_GetButtonUpUpdate { static bool Prefix(GetButtonUp __instance) { return GetButtonUpUpdate.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(GetMouseButtonDown), "OnUpdate")] static class P_GetMouseButtonDownUpdate { static bool Prefix(GetMouseButtonDown __instance) { return GetMouseButtonDownUpdate.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(Raycast), "OnEnter")] static class P_RaycastEnter { static bool Prefix(Raycast __instance) { return RaycastEnter.RunPrefix(__instance); } static void Postfix(Raycast __instance) { RaycastEnter.RunPostfix(__instance); } }
        [HarmonyPatch(typeof(Raycast), "DoRaycast")] static class P_RaycastDo { static bool Prefix(Raycast __instance) { return RaycastDo.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(SmoothLookAt), "DoSmoothLookAt")] static class P_SmoothLookAtDo { static bool Prefix(SmoothLookAt __instance) { return SmoothLookAtDo.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(ActivateGameObject), "OnEnter")] static class P_ActivateGameObjectEnter { static bool Prefix(ActivateGameObject __instance) { return ActivateGameObjectEnter.RunPrefix(__instance); } static void Postfix(ActivateGameObject __instance) { ActivateGameObjectEnter.RunPostfix(__instance); } }
        [HarmonyPatch(typeof(ActivateGameObject), "DoActivateGameObject")] static class P_ActivateGameObjectDo { static bool Prefix(ActivateGameObject __instance) { return ActivateGameObjectDo.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(SetPosition), "DoSetPosition")] static class P_SetPositionDo { static bool Prefix(SetPosition __instance) { return SetPositionDo.RunPrefix(__instance); } static void Postfix(SetPosition __instance) { SetPositionDo.RunPostfix(__instance); } }
        [HarmonyPatch(typeof(SensorGetDetectionRayHit), "OnEnter")] static class P_SensorRayHitEnter { static bool Prefix(SensorGetDetectionRayHit __instance) { return SensorRayHitEnter.RunPrefix(__instance); } static Exception Finalizer(SensorGetDetectionRayHit __instance, Exception __exception) { return SensorRayHitEnter.RunFinalizer(__instance, __exception); } }
        [HarmonyPatch(typeof(SensorGetLineOfSightResult), "OnEnter3D")] static class P_LosEnter3D { static bool Prefix(SensorGetLineOfSightResult __instance) { return LosEnter3D.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(SensorGetLineOfSightResult), "OnUpdate3D")] static class P_LosUpdate3D { static bool Prefix(SensorGetLineOfSightResult __instance) { return LosUpdate3D.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(SensorGetDetections), "DoAction")] static class P_SensorDetectionsDo { static bool Prefix(SensorGetDetections __instance) { return SensorDetectionsDo.RunPrefix(__instance); } }
        [HarmonyPatch(typeof(PlayMakerFSM), "OnEnable")] static class P_FsmEnable { static void Postfix(PlayMakerFSM __instance) { FsmEnable.RunPostfix(__instance); } }
        [HarmonyPatch(typeof(Fsm), "ProcessEvent")] static class P_ProcessEvent { static bool Prefix(Fsm __instance, FsmEvent fsmEvent, FsmEventData eventData) { return ProcessEvent.Run(__instance, fsmEvent, eventData); } }
        [HarmonyPatch(typeof(Fsm), "EnterState")] static class P_EnterState { static bool Prefix(Fsm __instance, FsmState state) { return EnterState.RunPrefix(__instance, state); } static void Postfix(Fsm __instance, FsmState state) { EnterState.RunPostfix(__instance, state); } }
        [HarmonyPatch(typeof(ES3Internal.ES3ReferenceMgrBase), "GetPrefab", typeof(long), typeof(bool))]
        static class P_GetPrefab { static bool Prefix(long id, ref ES3Internal.ES3Prefab __result) { if (GetPrefab.PrefixCount == 0) return true; var p = GetPrefab.Run(id); if (p == null) return true; __result = p; return false; } }
        [HarmonyPatch(typeof(NWH.Common.FloatingOrigin.FloatingOrigin), "Jump")]
        static class P_OriginJump { static bool Prefix(NWH.Common.FloatingOrigin.FloatingOrigin __instance) { Origin.BeforeJump(__instance); return OriginJump.RunPrefix(__instance); } static void Postfix(NWH.Common.FloatingOrigin.FloatingOrigin __instance) { OriginJump.RunPostfix(__instance); Origin.AfterJump(__instance); } }
    }
}
