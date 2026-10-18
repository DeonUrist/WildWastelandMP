using System;
using System.Collections.Generic;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Guns
{
    /// Bridges item identity to the reusable arm model before its activation actions run.
    /// Save data: ES3 key "Apocaunloader.Magazines" inside the game's own save file, injected into the ES3 cache right after the
    /// game's SaveAll("global") and again right before the cache is flushed (StoreCachedFile), read before LoadAll("global").
    /// Key, format and timing are verbatim from Gunplay 1.3.0 / Apocaunloader 1.3.0 (save compatibility both ways).
    internal static partial class PerGunAmmo
    {
        internal const string SaveKey = "Apocaunloader.Magazines";
        private static MagazineLedger _ledger = new MagazineLedger();
        private static PlayMakerFSM _reload, _handler, _saveFsm;
        private static GameObject _item;
        private static string _identity, _type, _snapshot, _saveFile;
        private static bool _loading = true, _saving, _faulted;
        private static float _scanAt;
        private static readonly List<PlayMakerFSM> _buf = new List<PlayMakerFSM>(64);

        internal static bool Suspended { get { return _loading || _saving || _faulted; } }
        private static bool On { get { return GunsModule.PerGunMagazines.Value; } }

        // ---- F7 live info ----
        internal static int TrackedItems { get { return _ledger.Count; } }
        internal static int ClaimedTypes { get { return _ledger.ClaimedCount; } }
        internal static string Current { get { return _identity == null ? "-" : _identity + " (" + _type + ")"; } }
        internal static string State { get { return _faulted ? "FAULTED (suspended to protect save data)" : _loading ? "loading" : _saving ? "saving" : "active"; } }

        internal static void ResetAll()
        {
            _ledger = new MagazineLedger();
            _handler = null; _saveFsm = null; _scanAt = 0f;
            Release();
            _loading = true; _saving = false; _faulted = false; _snapshot = null; _saveFile = null;
        }

        /// (WW) [Guns] PerGunMagazines changed at runtime: start from a fresh ledger (as a fresh install would).
        internal static void ToggleChanged()
        {
            Release();
            _ledger = new MagazineLedger();
            _snapshot = null;
            _scanAt = 0f;
        }

        internal static PlayMakerFSM Find(GameObject go, string name) { return FsmUtil.Find(go, name); }

        // Vanilla's Folk 17 weaponType is "folk17", but its arm model is "folk_17".
        private static bool MatchesModel(string type, string model)
        { return type == model || (type == "folk17" && model == "folk_17"); }

        internal static bool ArmModel(GameObject go)
        {
            for (var p = go == null ? null : go.transform.parent; p != null; p = p.parent)
                if (p.name == "WeaponsArm") return true;
            return false;
        }

        internal static void SceneChanged()
        {
            _handler = null; _saveFsm = null; _scanAt = 0;
            // LoadAll may precede sceneLoaded. Keep the ledger from that exact load.
            Release();
        }

        private static Magazine Read(PlayMakerFSM reload)
        {
            var rounds = reload.FsmVariables.GetFsmInt("ammo_in_gun");
            var attack = Find(reload.gameObject, "Attack");
            var hammer = attack == null ? null : attack.FsmVariables.GetFsmBool("hammer");
            return new Magazine(rounds == null ? 0 : rounds.Value, hammer != null && hammer.Value);
        }

        private static void Apply(PlayMakerFSM reload, Magazine magazine)
        {
            reload.FsmVariables.GetFsmInt("ammo_in_gun").Value = magazine.Rounds;
            var attack = Find(reload.gameObject, "Attack");
            var hammer = attack == null ? null : attack.FsmVariables.GetFsmBool("hammer");
            if (hammer != null) hammer.Value = magazine.Hammer;
        }

        internal static void Capture()
        {
            if (_reload == null || _identity == null || _faulted || _loading) return;
            _ledger.Capture(_identity, _type, Read(_reload));
            CaptureNetworkMagazine(_item, Read(_reload));
        }

        private static void Release()
        { _reload = null; _item = null; _identity = null; _type = null; }

        internal static void Holster(Fsm fsm)
        {
            if (fsm.Name != "WeaponInHand" || Suspended) return;
            AmmoRunner.Unloader.CompleteForSwitch();
            if (!On) return;
            Capture(); Release();
        }

        internal static void Draw(Fsm handler, GameObject model)
        {
            if (Suspended || !On || handler == null || handler.Name != "WeaponInHand" || !ArmModel(model)) return;
            var reload = Find(model, "Reload");
            if (reload == null || reload.FsmVariables.GetFsmInt("ammo_in_gun") == null) return; // melee
            var itemVar = handler.Variables.GetFsmGameObject("Item");
            var item = itemVar == null ? null : itemVar.Value;
            var itemTypeFsm = Find(item, "weaponType");
            var itemType = itemTypeFsm == null ? null : itemTypeFsm.FsmVariables.GetFsmString("weaponType");
            // Only the physical item selected by vanilla owns this model. Save-time activation of all models is ignored.
            if (item == null || itemType == null || !MatchesModel(itemType.Value, model.name) || string.IsNullOrEmpty(item.name)) return;
            if (_item == item && _reload == reload) return;
            AmmoRunner.Unloader.CompleteForSwitch();
            Capture();
            ReadNetworkMagazine(item, itemType.Value);
            var magazine = _ledger.Draw(item.name, itemType.Value, Read(reload));
            _item = item; _identity = item.name; _type = itemType.Value; _reload = reload; _handler = handler.FsmComponent;
            Apply(reload, magazine);
            if (Core.Log.Verbose) AmmoSettings.V("Magazine " + _identity + ": " + magazine.Rounds + " rounds");
        }

        // Fsm.EnterState prefix: every state entry of every FSM. Both FSM names we watch are 12 characters long, so one int compare
        // rejects almost every call before any string compare.
        internal static void BeforeState(Fsm fsm, FsmState st)
        {
            string n = fsm.Name;
            if (n == null || n.Length != 12) return;
            string state = st.Name;
            if (n == "WeaponInHand") { if (state == "Deactivate") Holster(fsm); return; }
            if (n != "SaveLoadGame") return;
            _saveFsm = fsm.FsmComponent;
            if (state == "Start" || state == "setSeed" || state == "LoadGame")
            {
                ArmorOverflow.Reset();
                AmmoRunner.Unloader.CancelForLoad();
                _ledger = new MagazineLedger(); Release();
                _loading = true; _saving = false; _faulted = false; _snapshot = null; _saveFile = null;
            }
            else if (state == "wait" || state == "SaveGame") BeginSave();
        }

        internal static void AfterState(Fsm fsm, FsmState st)
        {
            string n = fsm.Name;
            if (n == null || n.Length != 12 || n != "SaveLoadGame" || st.Name != "isPlay") return;
            _saveFsm = fsm.FsmComponent;
            _loading = false; _saving = false;
            // Native load restored only the per-type counts. Replace the selected one from our item ledger.
            Release();
            AdoptDrawn();
        }

        private static void AdoptDrawn()
        {
            if (Suspended || !On) return;
            if (_handler == null) _handler = FindHandler();
            if (_handler == null || _handler.Fsm == null || !_handler.Fsm.Initialized) return;
            // A holstered handler retains its Item variable; require a currently activated gun model.
            string state = _handler.ActiveStateName;
            if (state == "off" || state == "Deactivate" || state == "hands" || state == "Activate" || state.StartsWith("Slot ", StringComparison.Ordinal)) return;
            var type = _handler.FsmVariables.GetFsmString("weaponType");
            if (type == null) return;
            // (WW) the drawn model is an active Reload FSM under the player's WeaponsArm: search that subtree instead of the whole
            // scene every second (whole-scene scan only while the rig is not found)
            var arm = PlayerRig.WeaponsArm;
            if (arm != null) arm.GetComponentsInChildren(false, _buf);
            else _buf.AddRange(UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>());
            try
            {
                for (int i = 0; i < _buf.Count; i++)
                {
                    var f = _buf[i];
                    if (f != null && f.FsmName == "Reload" && MatchesModel(type.Value, f.gameObject.name) && f.gameObject.activeInHierarchy && ArmModel(f.gameObject))
                    { Draw(_handler.Fsm, f.gameObject); break; }
                }
            }
            finally { _buf.Clear(); }
        }

        // the "Weapons" object's WeaponInHand FSM: looked for under the player and the camera rig first, then (as before) in the whole
        // scene; cached until the next scene load
        private static PlayMakerFSM FindHandler()
        {
            var r = HandlerIn(GameRefs.Player);
            if (r == null) r = HandlerIn(GameRefs.PlayerCameraHolder);
            if (r != null) return r;
            foreach (var f in UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>())
                if (f.FsmName == "WeaponInHand" && f.gameObject.name == "Weapons") return f;
            return null;
        }

        private static PlayMakerFSM HandlerIn(GameObject root)
        {
            if (root == null) return null;
            root.GetComponentsInChildren(false, _buf);
            try
            {
                for (int i = 0; i < _buf.Count; i++)
                {
                    var f = _buf[i];
                    if (f != null && f.FsmName == "WeaponInHand" && f.gameObject.name == "Weapons") return f;
                }
                return null;
            }
            finally { _buf.Clear(); }
        }

        internal static void Tick()
        {
            if (Multiplayer.Active && On) Capture();
            if (_saveFsm == null && Time.unscaledTime >= _scanAt)
            {
                // (WW) Core.GameRefs / SaveBus instead of a FindObjectsOfType scan per second
                _scanAt = Time.unscaledTime + 1f;
                _saveFsm = GameRefs.SaveLoadGame;
                if (_saveFsm != null && SaveBus.State == "isPlay") _loading = false;
            }
            if (Suspended || !On) return;
            if (_reload != null && (_item == null || !_reload.gameObject.activeInHierarchy)) { Capture(); Release(); }
            if (_reload == null && Time.unscaledTime >= _scanAt) { _scanAt = Time.unscaledTime + 1f; AdoptDrawn(); }
        }

        internal static void BeginSave()
        {
            if (_loading || _faulted || _saving) return;
            AmmoRunner.Unloader.CompleteForSwitch();
            if (On) { Capture(); ImportNetworkMagazines(); _snapshot = _ledger.Encode(); }
            _saving = true;
        }

        internal static void SaveGlobal(ES3PlayMaker.SaveAll action)
        {
            if (action.Fsm == null || action.Fsm.Name != "SaveLoadGame" || action.key.Value != "global" || _faulted || _loading) return;
            BeginSave();
            var settings = action.GetSettings();
            _saveFile = settings.path;
            CacheSnapshot(settings);
        }

        private static void CacheSnapshot(ES3Settings settings)
        {
            if (_snapshot == null || string.IsNullOrEmpty(_saveFile)) return;
            var cache = new ES3Settings(settings.path, settings) { location = ES3.Location.Cache };
            ES3.Save<string>(SaveKey, _snapshot, cache);
        }

        internal static void BeforeStore(ES3Settings settings)
        {
            // Also inject into Apocasaver's final rebuilt cache, immediately before its transactional flush.
            if (settings != null && settings.path == _saveFile && !_faulted && !_loading) CacheSnapshot(settings);
        }

        internal static void LoadGlobal(ES3PlayMaker.LoadAll action)
        {
            if (action.Fsm == null || action.Fsm.Name != "SaveLoadGame" || action.key.Value != "global") return;
            AmmoRunner.Unloader.CancelForLoad(); Release();
            _loading = true; _saving = false; _snapshot = null; _saveFile = null; _faulted = false;
            var settings = action.GetSettings();
            _ledger = On && ES3.KeyExists(SaveKey, settings) ? MagazineLedger.Decode(ES3.Load<string>(SaveKey, settings)) : new MagazineLedger();
        }

        internal static void Fail(Exception error)
        {
            AmmoRunner.Unloader.CancelForLoad();
            _faulted = true;
            Core.Log.Error("[Guns] Per-gun ammo suspended to preserve save data", error);
        }
    }

    /// (WW) Handler of Core.Hooks.ActivateGameObjectEnter (prefix, never skips).
    internal static class MagazineDrawPatch
    {
        internal static bool Prefix(ActivateGameObject __instance)
        {
            var fsm = __instance.Fsm;
            if (fsm == null) return true;
            string n = fsm.Name;
            if (n == null || n.Length != 12 || n != "WeaponInHand" || !__instance.activate.Value) return true;
            try { PerGunAmmo.Draw(fsm, fsm.GetOwnerDefaultTarget(__instance.gameObject)); }
            catch (Exception e) { PerGunAmmo.Fail(e); }
            return true;
        }
    }

    /// (WW) Handlers of Core.Hooks.EnterState (prefix + postfix, never skip).
    internal static class MagazineStatePatch
    {
        internal static bool Prefix(Fsm fsm, FsmState state)
        {
            if (state != null && fsm != null) try { PerGunAmmo.BeforeState(fsm, state); } catch (Exception e) { PerGunAmmo.Fail(e); }
            return true;
        }
        internal static void Postfix(Fsm fsm, FsmState state)
        {
            if (state != null && fsm != null) try { PerGunAmmo.AfterState(fsm, state); } catch (Exception e) { PerGunAmmo.Fail(e); }
        }
    }

    [HarmonyPatch(typeof(ES3PlayMaker.SaveAll), "Enter")]
    internal static class MagazineSavePatch
    {
        static void Postfix(ES3PlayMaker.SaveAll __instance)
        { try { PerGunAmmo.SaveGlobal(__instance); } catch (Exception e) { PerGunAmmo.Fail(e); } }
    }

    [HarmonyPatch(typeof(ES3PlayMaker.LoadAll), "Enter")]
    internal static class MagazineLoadPatch
    {
        static void Prefix(ES3PlayMaker.LoadAll __instance)
        { try { PerGunAmmo.LoadGlobal(__instance); } catch (Exception e) { PerGunAmmo.Fail(e); } }
    }

    [HarmonyPatch(typeof(ES3), "StoreCachedFile", new[] { typeof(ES3Settings) })]
    internal static class MagazineStorePatch
    {
        static void Prefix(ES3Settings settings)
        { try { PerGunAmmo.BeforeStore(settings); } catch (Exception e) { PerGunAmmo.Fail(e); } }
    }
}
