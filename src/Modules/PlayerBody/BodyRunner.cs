using System;
using UnityEngine;

namespace WildWasteland.Modules.PlayerBody
{
    // The old Apocaplayer Runner MonoBehaviour, as plain static job methods registered by PlayerBodyModule.Init (Update -> Tick,
    // LateUpdate -> LateTick, OnGUI -> Gui, ModApiRunner.LateUpdate -> ModApiTick, Start's end-of-frame
    // coroutine -> EndOfFrameLoop). The old "Enabled off" branch is Shutdown -> TearDown (the jobs are removed by Core).
    internal static class BodyRunner
    {
        private static Body _body;
        private static Vector3 _jumpHeadPlayerPosition;
        private static bool _jumpHeadReady;
        internal static bool TryPlayerJumpHead(GameObject player, out Vector3 position)
        {
            position = Vector3.zero;
            if (!_jumpHeadReady || player != Game.Player || Game.Dead || _body == null || !_body.Alive || !_body.TryJumpHead(out position)) return false;
            position += player.transform.position - _jumpHeadPlayerPosition;
            return true;
        }
        internal static Body PlayerBody { get { return _body; } }
        private static float _nextBuild;
        private static string _lastMode = "";
        private static Vector3 _lastVelocity;
        private static Transform _lastCar;
        internal static bool RagdollVisible { get { return _body != null && _body.IsRagdoll; } }
        internal static Vector3 DeathFocus { get { return _body.DeathFocus; } }
        internal static bool TryOcclusionPoints(out Vector3 head, out Vector3 feet)
        {
            if (_body != null && _body.Alive) return _body.TryOcclusionPoints(out head, out feet);
            head = feet = Vector3.zero; return false;
        }

        public static void OnSceneLoaded()
        {
            Crawling.Reset();
            WheelArms.Clear();
            ClimbingController.ResetInstance();
            DestroyBody();
            CarSeat.Detach();
            ThirdPerson.Off();
            Game.Reset();
            Arms.OnSceneLoaded();
            Car.Reset();
            Cave.Reset();
            InventoryModel.Reset();
            Props.OnSceneLoaded();
        }

        // Init: start from a clean state (also after a runtime off -> on)
        internal static void ResetAll()
        {
            OnSceneLoaded();
            _nextBuild = 0f; _lastMode = "";
        }

        // Shutdown: everything the old "Enabled = false" branch restored, plus the camera / cutaway components
        internal static void TearDown()
        {
            WheelArms.Clear();
            DestroyBody();
            CarSeat.Detach();
            ThirdPerson.Off();
            Arms.Restore();
            InventoryModel.Off();
            OcclusionCutaway.Stop();
            ThirdPerson.EndOfFrame();
            Car.Reset();
            PlayerBodyModule.Info("Disabled: the game's own player is back");
        }

        // Male / Female: rebuild the body from the other model; her arms texture and TAB picture only for her
        public static void CharacterChanged()
        {
            ClimbingController.StopInstance();
            try
            {
                DestroyBody();
                Model.Reset();
                Arms.Restore();   // the next character's arms (or the game's for Male)
                InventoryModel.CharacterChanged();
                PlayerBodyModule.Info("Character: " + PlayerBodyModule.Character.Value);
            }
            catch (Exception e) { PlayerBodyModule.Error("Character switch: " + e); }
        }

        private static void DestroyBody()
        {
            _jumpHeadReady = false;
            if (_body != null) _body.Destroy();
            _body = null;
            _lastVelocity = Vector3.zero;
            _lastCar = null;
        }

        internal static void Tick()
        {
            long t0 = Perf.Now;
            try
            {
                WheelArms.Tick();
                try { Perf.Frame(); } catch (Exception e) { PlayerBodyModule.Warn("Perf: " + e.Message); }
                if (!Game.Ready) { OcclusionCutaway.Stop(); if (_body != null) DestroyBody(); return; }   // (2.2.6) the cutaway taken down in menus / loading too
                if (!Game.Dead) { _lastVelocity = Game.Velocity; _lastCar = Game.InCar ? Game.CarRoot : null; }
                if (PlayerBodyModule.FemaleArms.Value && PlayerBodyModule.Female) Arms.Tick(); else Arms.Restore();
                ThirdPerson.Tick();
                Car.Tick();
                GunPose.ClearHint();                       // set again in LateUpdate while a weapon is being adjusted
                if (!ThirdPerson.On || Game.InCar) GunPose.Flush();
            }
            catch (Exception e) { PlayerBodyModule.Error("Update: " + e); }
            finally { Perf.Add(Perf.Update, t0); }
        }

        internal static void LateTick()
        {
            long t0 = Perf.Now;
            try
            {
                if (!Game.Ready) return;
                InventoryModel.LateTick();
                ThirdPerson.LateCrosshair();
                if (!Game.Dead && _body != null && _body.IsRagdoll) DestroyBody();
                if (_body == null || !_body.Alive)
                {
                    _body = null;
                    if (Time.unscaledTime < _nextBuild) return;
                    _nextBuild = Time.unscaledTime + 2f;
                    _body = Body.Create();
                    if (_body == null) return;
                }

                if (!Crawling.Active) _body.EndCrawl();
                if (Game.Dead)
                {
                    _body.LateDead(_lastVelocity, _lastCar);
                    ThirdPerson.CarShift = false;
                    return;
                }
                bool inCar = Game.InCar;
                bool fpCam = Game.FirstPersonCameraOn;
                ThirdPerson.CarShift = false;
                string mode;
                bool fpArms = false;
                if (inCar)
                {
                    bool replace = PlayerBodyModule.ReplaceDriver.Value;
                    bool seated = replace && CarSeat.Attach(Game.Player.transform);   // (2.2.7) the game's driver: no seat search every frame
                    if (!seated || !replace)
                    {
                        // no seat to copy (or the player wants the game's driver): no body in the car
                        if (!replace) CarSeat.Detach();
                        _body.SetVisible(false, false);
                        mode = "car (game driver)";
                    }
                    else
                    {
                        bool firstPerson = fpCam && (!ThirdPerson.On || ThirdPerson.Peek);   // binoculars: first person
                        _body.LateCar(Game.Player.transform, firstPerson);
                        if (firstPerson) _body.ShiftFirstPerson(Game.Player.transform.rotation, PlayerBodyModule.CarViewBase + new Vector3(PlayerBodyModule.FpCarCamX.Value, PlayerBodyModule.FpCarCamY.Value, PlayerBodyModule.FpCarCamZ.Value));
                        _body.SetMesh(firstPerson, true, Game.DrawnWeapon != "");
                        _body.SetVisible(!firstPerson || PlayerBodyModule.BodyInCar, firstPerson && PlayerBodyModule.BodyInCar);
                        ThirdPerson.CarShift = firstPerson && PlayerBodyModule.BodyInCar && PlayerBodyModule.CarCameraForward.Value != 0f;
                        // no body in first person: still her arms on the wheel while nothing is drawn (the body mesh has them when it is shown)
                        fpArms = firstPerson && fpCam && !PlayerBodyModule.BodyInCar && PlayerBodyModule.ArmsInCar && Game.DrawnWeapon == "" && !Game.Binoculars && (Game.ToolInHand || !Game.GameArmsShown);
                        mode = firstPerson ? "car, first person" : "car, third person";
                    }
                }
                else
                {
                    CarSeat.Detach();
                    bool third = ThirdPerson.On && !ThirdPerson.Peek;   // binoculars raised: drawn as in first person
                    _body.LateFoot(third ? Body.View.ThirdPerson : Body.View.FirstPerson, Time.deltaTime);
                    if (!third && Game.PlayerCamera != null)
                        _body.ShiftFirstPerson(Quaternion.Euler(0f, Game.PlayerCamera.eulerAngles.y, 0f), new Vector3(PlayerBodyModule.FpCamX.Value, PlayerBodyModule.FpCamY.Value, PlayerBodyModule.FpCamZ.Value));
                    _jumpHeadPlayerPosition = Game.Player.transform.position;
                    _jumpHeadReady = true;
                    _body.SetMesh(!third, false);
                    bool show = third || PlayerBodyModule.BodyFirstPerson.Value;
                    _body.SetVisible(show && fpCam, !third && show && fpCam);
                    // first person, nothing in hand: the game draws no arms - show the body's own, animated like the rest of her
                    fpArms = !third && fpCam && (Crawling.Active || ModAPI.PlayerTraversalActive
                        || PlayerBodyModule.EmptyHandArms.Value && !Game.Binoculars && (Game.ToolInHand || !Game.GameArmsShown));
                    mode = third ? "on foot, third person" : "on foot, first person";
                }
                _body.SetFirstPersonArms(fpArms);
                _body.LateEquipment(PlayerBodyModule.EquipmentOnBody.Value);
                if (!ReferenceEquals(mode, _lastMode)) { _lastMode = mode; PlayerBodyModule.Verbose("View: " + mode); }
            }
            catch (Exception e) { PlayerBodyModule.Error("LateUpdate: " + e); _nextBuild = Time.unscaledTime + 5f; }
            finally { Perf.Add(Perf.Late, t0); }
        }

        internal static void WheelTick()
        {
            if (_body != null && _body.Alive && Game.Ready && Game.InCar && !Game.Dead && PlayerBodyModule.ReplaceDriver.Value && CarSeat.Seat != null)
                _body.LateWheelArms();
        }

        // (2.2.0) characters animated through ModAPI, after the Animators (was ModApiRunner.LateUpdate)
        internal static void ModApiTick()
        {
            long t0 = Perf.Now;
            try { ModAPI.LateTick(); } finally { Perf.Add(Perf.ModApi, t0); }
        }


        // the same conditions GunPose.OnGUI / Car.OnGUI draw under (an editor hint, or seated in a car)
        internal static bool GuiWanted() { return !string.IsNullOrEmpty(GunPose.HintText) || Car.GuiWanted || WheelArms.EditorVisible; }
        internal static void Gui() { long t0 = Perf.Now; try { GunPose.OnGUI(); Car.OnGUI(); WheelArms.DrawEditorHint(); } catch (Exception) { } Perf.Add(Perf.Gui, t0); }

        // after the frame is drawn: the camera's real matrices back (the game's mouse picks next frame cast from the eye)
        internal static System.Collections.IEnumerator EndOfFrameLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (!PlayerBodyModule.Running) yield break;
                long t0 = Perf.Now;
                try { ThirdPerson.EndOfFrame(); } catch (Exception) { }
                Perf.Add(Perf.EndFrame, t0);
            }
        }
    }
}
