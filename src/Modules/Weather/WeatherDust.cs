using System;
using ES3Internal;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    // Borrow the installed game's Sand Player visual, retaining its cloud motion and animated material.
    internal sealed class WeatherDust : IDisposable
    {
        private GameObject _root;
        private ParticleSystem _system;
        private ParticleSystemRenderer _renderer;
        private Material _material;
        private Camera _camera;
        private Vector3 _previous;
        private float _next, _rate, _opacity;
        private bool _hasPosition, _waiting;
        private ParticleSystem.MinMaxGradient _nativeColor;
        private int _diagnosticState = -1, _nativeLimit;
        private bool _nativeAutoSeed, _restoredSeed, _tintPending;
        private Color _lastTint;
        private float _reportAt;
        private bool _reported;

        private bool Create()
        {
            var manager = ES3ReferenceMgrBase.Current;
            var prefab = manager != null ? manager.GetPrefab(5697600806714206537L,true) : null;
            var source = prefab != null ? prefab.transform.Find("Sand Player") : null;
            if (source == null)
            {
                if (!_waiting) { _waiting = true; Log.Warn("[Weather] Waiting for native SandStorm/Sand Player prefab in the game's reference manager."); }
                return false;
            }
            _root = new GameObject("WW.Weather.GroundDust") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            _root.transform.SetParent(Runner.Host.transform,false); _root.SetActive(false);
            var copy = UnityEngine.Object.Instantiate(source.gameObject,_root.transform,false);
            copy.name = "WW.Weather.GroundDust.0"; copy.hideFlags = HideFlags.HideAndDontSave;
            copy.transform.localPosition = Vector3.zero;
            _system = copy.GetComponent<ParticleSystem>(); _renderer = copy.GetComponent<ParticleSystemRenderer>();
            _system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _system.main; main.playOnAwake = false;
            // This camera-gated renderer may be disabled at Unity's simulation/culling step.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            _nativeColor = main.startColor; _nativeLimit = main.maxParticles;
            _nativeAutoSeed = _system.useAutoRandomSeed;
            _rate = _system.emission.rateOverTimeMultiplier;
            _material = new Material(_renderer.sharedMaterial); _renderer.sharedMaterial = _material;
            _opacity = _material.HasProperty("_Opacity") ? _material.GetFloat("_Opacity") : 1;
            _renderer.enabled = false; copy.SetActive(true); _root.SetActive(true);
            Camera.onPreCull += Before;
            Log.Always("[Weather] Vanilla Sand Player clouds ready: native material, UV flow, velocity, lifetime and shape retained; brown tint, 60/100/100 particle cap.");
            return true;
        }
        internal void Tick(Camera camera, float clock, float strength, Vector3 wind, WeatherVisualDetail detail, Color tint)
        {
            _camera = camera;
            if (_system == null)
            {
                if (Time.unscaledTime < _next) return;
                if (!Create()) { _next = Time.unscaledTime+1; return; }
            }
            Vector3 position = GameRefs.Player != null ? GameRefs.Player.transform.position : camera.transform.position;
            if (_hasPosition && (position-_previous).sqrMagnitude > 10000) _system.Clear();
            _previous = position; _hasPosition = true; _root.transform.position = position;
            if (WeatherDiagnostics.Frozen && _system.isPlaying) _system.Pause();
            int diagnosticState = WeatherDiagnostics.Enabled ? WeatherDiagnostics.Preset+1 : 0;
            if (Time.unscaledTime < _next && diagnosticState == _diagnosticState) return;
            if (_tintPending || tint != _lastTint || (diagnosticState != _diagnosticState && (WeatherDiagnostics.CloudPair || _diagnosticState == 9 || _diagnosticState == 10)))
            {
                // One-shot tint comparison of the same frozen particles; startColor alone affects only new emissions.
                var particles = new ParticleSystem.Particle[_system.particleCount]; int count = _system.GetParticles(particles);
                for (int i = 0; i < count; i++)
                    particles[i].startColor = WeatherDiagnostics.Enabled && WeatherDiagnostics.Preset == 8 ?
                        _nativeColor.Evaluate(0,(particles[i].randomSeed%10000)/9999f) : tint;
                _system.SetParticles(particles,count);
                _tintPending = false;
                _lastTint = tint;
            }
            if (!WeatherDiagnostics.Enabled && _restoredSeed) { _system.useAutoRandomSeed = _nativeAutoSeed; _restoredSeed = false; }
            _diagnosticState = diagnosticState;
            _next = Time.unscaledTime+.1f;
            if (WeatherDiagnostics.CloudPair) strength = 1;
            if (wind.sqrMagnitude > .01f) _root.transform.rotation = Quaternion.LookRotation(wind);
            bool running = GameRefs.WorldRunning && strength > .005f;
            var emission = _system.emission; emission.enabled = running;
            emission.rateOverTimeMultiplier = _rate*strength*(!WeatherDiagnostics.CloudPair && detail == WeatherVisualDetail.Low ? .6f : 1);
            var main = _system.main; main.maxParticles = WeatherDiagnostics.CloudPair ? _nativeLimit : detail == WeatherVisualDetail.Low ? 60 : 100;
            main.startColor = WeatherDiagnostics.Enabled && WeatherDiagnostics.Preset == 8 ? _nativeColor : new ParticleSystem.MinMaxGradient(tint);
            if (_material.HasProperty("_Opacity")) _material.SetFloat("_Opacity",_opacity*strength);
            if (!GameRefs.WorldRunning || WeatherDiagnostics.Frozen) { if (_system.isPlaying) _system.Pause(); }
            else if (!_system.isPlaying) _system.Play();
            if (!_reported && running && !WeatherDiagnostics.Frozen)
            {
                if (_reportAt == 0) _reportAt = Time.unscaledTime+2;
                if (_system.particleCount > 0 || Time.unscaledTime >= _reportAt)
                {
                    _reported = true;
                    Log.Always("[Weather] Local sand live: particles="+_system.particleCount+"; rate="+emission.rateOverTimeMultiplier+"; time="+_system.time+"; culling="+main.cullingMode+"; strength="+strength+"; playing="+_system.isPlaying);
                }
            }
        }
        private void Before(Camera camera) { if (_renderer != null) _renderer.enabled = camera == _camera && WeatherDiagnostics.Clouds; }
        internal void CaptureDiagnostic(WeatherDiagnostics.Snapshot data)
        {
            if (_system == null) return;
            _system.Pause();
            data.ParticleSeed = _system.randomSeed; data.ParticleRoot = Origin.World(_root.transform.position); data.ParticleRotation = _root.transform.rotation;
            var particles = new ParticleSystem.Particle[_system.particleCount]; int count = _system.GetParticles(particles);
            data.Particles = new WeatherDiagnostics.Particle[count];
            for (int i = 0; i < count; i++)
            {
                var p = particles[i];
                data.Particles[i] = new WeatherDiagnostics.Particle { World = Origin.World(p.position), Velocity = p.velocity,
                    Size = p.startSize3D, Rotation = p.rotation3D, Color = p.startColor, Life = p.remainingLifetime, StartLife = p.startLifetime, Seed = p.randomSeed };
            }
            Log.Always("[WeatherDiag] Local-cloud snapshot: count="+count+"; seed="+data.ParticleSeed+"; scale="+_system.transform.lossyScale.ToString("F3")+"; simulation="+_system.main.simulationSpace+"; shader="+_material.shader.name+"; native shader UV time remains live.");
            Log.Always("[WeatherDiag] Local-cloud emitter: time="+_system.time+"; culling="+_system.main.cullingMode+"; speed="+_system.main.simulationSpeed+"; loop="+_system.main.loop+"; emission="+_system.emission.enabled+"; rate="+_system.emission.rateOverTimeMultiplier+"; sourceRate="+_rate+"; renderer="+_renderer.enabled+"; visible="+_renderer.isVisible+"; active="+_system.gameObject.activeInHierarchy+"; paused="+_system.isPaused);
        }
        internal void RestoreDiagnostic(WeatherDiagnostics.Snapshot data)
        {
            if (data.Particles == null || data.Particles.Length > 1000) return;
            if (_system == null && !Create()) return;
            _system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); _system.randomSeed = data.ParticleSeed;
            _restoredSeed = true; _diagnosticState = -1; _tintPending = true;
            _root.transform.SetPositionAndRotation(Origin.Scene(data.ParticleRoot),data.ParticleRotation);
            var particles = new ParticleSystem.Particle[data.Particles.Length];
            for (int i = 0; i < particles.Length; i++)
            {
                var p = data.Particles[i];
                particles[i] = new ParticleSystem.Particle { position = Origin.Scene(p.World), velocity = p.Velocity, startSize3D = p.Size,
                    rotation3D = p.Rotation, startColor = p.Color, remainingLifetime = p.Life, startLifetime = p.StartLife, randomSeed = p.Seed };
            }
            _system.Play(); _system.Pause(); _system.SetParticles(particles,particles.Length); _next = 0;
        }
        internal void Clear() { _hasPosition = false; if (_system != null) _system.Clear(); }
        internal void Suspend()
        {
            Clear(); _camera = null; _next = 0;
            if (_system != null) _system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_renderer != null) _renderer.enabled = false;
        }
        public void Dispose()
        {
            Camera.onPreCull -= Before; Suspend();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
