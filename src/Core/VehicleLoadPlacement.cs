using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using NWH.VehiclePhysics2;
using UnityEngine;

namespace WildWasteland.Core
{
    // Native ES3 restores a seat parent, not the live InCar/Drive lifecycle. Hand control back on clear ground.
    internal static class VehicleLoadPlacement
    {
        static bool _loading, _seated, _collisions;
        static VehicleController _car;
        static Rigidbody _playerBody;
        static float _retry;
        static readonly List<Rigidbody> Bodies = new List<Rigidbody>();
        static readonly List<Collider> Colliders = new List<Collider>();
        internal static void Install()
        {
            SaveBus.StateChanged += StateChanged;
            Hooks.WaitUpdate.Prefix(null, BeforeHandoff);
        }
        static void StateChanged(string previous, string current)
        {
            if (current != "Start" && current != "setSeed" && current != "LoadGame") return;
            Release(); _loading = current == "LoadGame"; _seated = false; _car = null; _retry = 0;
        }
        static void Release()
        { if (_playerBody != null) { _playerBody.detectCollisions = _collisions; _playerBody.isKinematic = false; } _playerBody = null; }
        internal static void Loaded()
        {
            if (!_loading || Multiplayer.Client || GameRefs.Player == null) return;
            var player = GameRefs.Player; var car = player.GetComponentInParent<VehicleController>();
            if (car == null) return;
            _car = car; _seated = true;
            var body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                if (_playerBody != body) { Release(); _playerBody = body; _collisions = body.detectCollisions; }
                body.detectCollisions = false; body.isKinematic = true;
            }
            player.transform.SetParent(null, true);
            player.transform.SetPositionAndRotation(car.transform.position + Vector3.up * 4f, Quaternion.Euler(0, car.transform.eulerAngles.y, 0));
        }
        internal static void LoadedStance(ES3PlayMaker.Load action)
        {
            if (!_loading || Multiplayer.Client || action.key.Value != "PlayerInCar" || action.Fsm.GameObject != GameRefs.Player) return;
            if (action.value.intValue > 0) { _seated = true; action.value.SetValue(0); }
            Loaded();
        }
        static bool BeforeHandoff(Wait action)
        {
            if (!_loading || Multiplayer.Client || !_seated || action.State.Name != "Wait for Terrain Gen" || action.Fsm.Name != "SaveLoadGame") return true;
            if (Time.unscaledTime < _retry) return false;
            _retry = Time.unscaledTime + .25f; Loaded();
            var player = GameRefs.Player;
            if (player == null || _car == null) return false;
            Park();
            Colliders.Clear(); _car.GetComponentsInChildren(true, Colliders);
            var bounds = new Bounds(_car.transform.position, Vector3.one);
            foreach (var collider in Colliders) if (collider != null && collider.enabled && !collider.isTrigger) bounds.Encapsulate(collider.bounds);
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 2f;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f; Vector3 place;
                var target = bounds.center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                if (!MultiplayerRespawn.Surface(target, player.transform, out place)) continue;
                if (place.x > bounds.min.x - .5f && place.x < bounds.max.x + .5f && place.z > bounds.min.z - .5f && place.z < bounds.max.z + .5f) continue;
                player.transform.SetParent(null, true); player.transform.SetPositionAndRotation(place, Quaternion.Euler(0, _car.transform.eulerAngles.y, 0));
                Stand(player); Release(); var body = player.GetComponent<Rigidbody>();
                if (body != null) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                Physics.SyncTransforms(); _loading = _seated = false;
                Log.Always("[System] In-vehicle save loaded: player standing beside parked " + _car.name); _car = null;
                return true;
            }
            return false; // Keep native loading/input freeze until main terrain and standing clearance exist.
        }
        static void Park()
        {
            _car.input.Throttle = 0; _car.input.Brakes = 1; _car.input.Handbrake = 1;
            if (Services.VehicleIgnition == null || !Services.VehicleIgnition.TryStop(_car.gameObject)) _car.powertrain.engine.StopEngine();
            var brake = _car.transform.Find("handbrake"); var fsm = brake != null ? FsmUtil.Find(brake.gameObject, "Handbrake") : null;
            if (fsm != null && fsm.Fsm.Initialized && fsm.ActiveStateName != "HandbrakeOn") fsm.Fsm.SetState("HandbrakeOn");
            Bodies.Clear(); _car.GetComponentsInChildren(true, Bodies);
            foreach (var body in Bodies) if (body != null && !body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            var root = _car.GetComponent<Rigidbody>(); if (root != null) { root.isKinematic = false; root.velocity = Vector3.zero; root.angularVelocity = Vector3.zero; }
        }
        internal static void Stand(GameObject player)
        {
            if (player == null || !Multiplayer.LocalTargetable) return;
            var inCar = FsmUtil.Find(player, "InCar");
            if (inCar != null && inCar.Fsm.Initialized && (inCar.ActiveStateName != "OnFoot" || player.GetComponent<Rigidbody>() == null)) inCar.Fsm.SetState("OnFoot");
            var movement = FsmUtil.Find(player, "Movement");
            if (movement != null && movement.Fsm.Initialized)
            {
                var height = movement.FsmVariables.FindFsmFloat("standingHeight"); if (height != null) height.Value = 1.7f;
                var friction = movement.FsmVariables.FindFsmFloat("ZeroFricitonHeight"); if (friction != null) friction.Value = 1.55f;
                var camera = movement.FsmVariables.FindFsmFloat("camHeight"); if (camera != null) camera.Value = .75f;
                movement.enabled = true; movement.Fsm.SetState("Standing");
            }
            // ES3 restores the camera's seat parent even when InCar already starts in OnFoot.
            // Look rotates the player toward the holder's WORLD yaw, so a child holder feeds
            // that rotation back into itself every frame. Restore the missing OnFoot camera lifecycle.
            var holder = GameRefs.PlayerCameraHolder;
            if (holder != null)
            {
                var parent = holder.transform.parent;
                holder.transform.SetParent(null, true);
                holder.transform.rotation = Quaternion.Euler(0, player.transform.eulerAngles.y, 0);
                var anchor = player.transform.Find("Pivot/PCameraPos");
                if (anchor != null) holder.transform.position = anchor.position;
                var look = FsmUtil.Find(player, "Look");
                if (look != null && look.Fsm.Initialized) look.Fsm.SetState("Reset"); // MouseLook.OnEnter recaptures yaw/pitch.
                Log.Always("[System] On-foot camera restored: previous parent=" + (parent != null ? parent.name : "none") + ", parent=none, look=" + (look != null ? look.ActiveStateName : "unavailable"));
            }
            var body = player.GetComponent<Rigidbody>(); if (body != null && !body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
    }
}
