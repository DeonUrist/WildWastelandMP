using System;
using ES3Internal;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    // Visual-only native stones: bounded pools, no rigidbodies, collisions, damage or managed particle loop.
    internal sealed class WeatherDebris : IDisposable
    {
        private readonly ParticleSystem[] _systems = new ParticleSystem[3];
        private readonly ParticleSystemRenderer[] _renderers = new ParticleSystemRenderer[3];
        private readonly bool[] _used = new bool[3];
        private readonly int[] _keys = { -1,-1,-1 };
        private readonly Material[] _materials = new Material[3];
        private readonly bool[] _reported = new bool[3];
        private readonly float[] _checkAt = new float[3];
        private Transform _source;
        private Camera _camera;
        private int _limit;
        private float _retry;
        internal WeatherDebris() { Camera.onPreCull += Before; }
        internal void Begin(Camera camera, WeatherVisualDetail detail)
        {
            _camera = camera; _limit = detail == WeatherVisualDetail.Low ? 1 : detail == WeatherVisualDetail.Max ? 3 : 2;
            Array.Clear(_used,0,_used.Length);
        }
        internal void Draw(int key, Vector3 position, Quaternion rotation, WeatherVisualDetail detail)
        {
            if (!WeatherDiagnostics.Full || (WeatherDiagnostics.Enabled && WeatherDiagnostics.Output != 0)) return;
            int slot = -1;
            for (int i=0;i<_limit;i++) if (_keys[i] == key) { slot=i; break; }
            if (slot < 0) for (int i=0;i<_limit;i++) if (!_used[i]) { slot=i; break; }
            if (slot < 0) return;
            if (_systems[slot] == null)
            {
                if (_source == null)
                {
                    if (Time.unscaledTime < _retry) return;
                    _retry = Time.unscaledTime+1;
                    var manager = ES3ReferenceMgrBase.Current;
                    var prefab = manager != null ? manager.GetPrefab(5697600806714206537L,true) : null;
                    _source = prefab != null ? prefab.transform.Find("Sand tornado loop/Stones") : null;
                    if (_source == null) return;
                }
                var copy = UnityEngine.Object.Instantiate(_source.gameObject,Runner.Host.transform,false);
                copy.name = "WW.Weather.Debris."+slot; copy.hideFlags = HideFlags.HideAndDontSave;
                copy.transform.localScale = Vector3.one*100; copy.SetActive(false);
                _systems[slot] = copy.GetComponent<ParticleSystem>(); _renderers[slot] = copy.GetComponent<ParticleSystemRenderer>();
                _materials[slot] = new Material(_renderers[slot].sharedMaterial);
                _materials[slot].SetColor("_Color",new Color(.045f,.035f,.025f,1));
                _materials[slot].SetFloat("_Emission",1);
                _renderers[slot].sharedMaterial = _materials[slot];
                _systems[slot].Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _systems[slot].main; main.playOnAwake = false; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.startSize = new ParticleSystem.MinMaxCurve(.03f,.09f); // Native parent scale 100: 3-9 metre debris.
                main.startLifetime = new ParticleSystem.MinMaxCurve(2,4);
                var velocity = _systems[slot].velocityOverLifetime;
                velocity.orbitalY = new ParticleSystem.MinMaxCurve(.25f,.55f);
                velocity.y = new ParticleSystem.MinMaxCurve(.4f,1);
                var shape = _systems[slot].shape; shape.radius = 5.8f;
                var collision = _systems[slot].collision; collision.enabled = false;
                var triggers = _systems[slot].trigger; triggers.enabled = false;
                copy.SetActive(true);
                Log.Always("[Weather] Visual stone pool ready: slot="+slot+"; native Stones sprite/orbit, no damage or physics.");
            }
            var system = _systems[slot];
            if (_keys[slot] != key) { system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); _keys[slot] = key; }
            _used[slot] = true; system.transform.SetPositionAndRotation(position,rotation);
            var settings = system.main; settings.maxParticles = detail == WeatherVisualDetail.Low ? 32 : detail == WeatherVisualDetail.Max ? 96 : 64;
            settings.startColor = Color.white; // Independent dark material, not the bright hurricane palette.
            var emission = system.emission; emission.rateOverTimeMultiplier = settings.maxParticles*.5f; emission.enabled = true;
            if (!GameRefs.WorldRunning || WeatherDiagnostics.Frozen) { if (system.isPlaying) system.Pause(); }
            else if (!system.isPlaying) system.Play();
            if (!_reported[slot] && GameRefs.WorldRunning && !WeatherDiagnostics.Frozen)
            {
                if (_checkAt[slot] == 0) _checkAt[slot] = Time.time+3;
                if (system.particleCount > 0 || Time.time >= _checkAt[slot])
                {
                    _reported[slot] = true;
                    Log.Always("[Weather] Dark stones live: slot="+slot+"; particles="+system.particleCount+"; size=3-9m; lifetime=2-4s; orbital=0.25-0.55rad/s; playing="+system.isPlaying+"; time="+system.time);
                }
            }
        }
        internal void End()
        {
            for (int i=0;i<3;i++) if (!_used[i] && _systems[i] != null && !_systems[i].isStopped)
                _systems[i].Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private void Before(Camera camera)
        {
            for (int i=0;i<3;i++) if (_renderers[i] != null) _renderers[i].enabled = _used[i] && camera == _camera;
        }
        internal void Suspend() { _camera = null; Array.Clear(_used,0,3); End(); }
        public void Dispose()
        {
            Camera.onPreCull -= Before; Suspend();
            for (int i=0;i<3;i++) if (_systems[i] != null) UnityEngine.Object.Destroy(_systems[i].gameObject);
            for (int i=0;i<3;i++) if (_materials[i] != null) UnityEngine.Object.Destroy(_materials[i]);
        }
    }
}
