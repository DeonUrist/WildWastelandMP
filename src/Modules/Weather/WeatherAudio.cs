using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    internal sealed class WeatherAudio : IDisposable
    {
        private readonly string _path;
        private readonly WeatherThunder _thunder;
        private readonly Vector2[] _edge = new Vector2[64];
        private GameObject _host;
        private AudioSource _source;
        private AudioClip _clip;
        private Coroutine _load;
        private UnityWebRequest _request;
        private bool _attempted;
        private float _nextSample, _target, _volume;
        internal WeatherAudio(string path)
        {
            _path = path;
            _thunder = new WeatherThunder(Path.Combine(Path.GetDirectoryName(path),"thunder.mp3"));
            var unit = new WeatherRegion { Radii = Vector2.one };
            for (int i = 0; i < _edge.Length; i++) _edge[i] = unit.Boundary(i);
        }
        internal void Load()
        {
            _thunder.Load();
            if (_attempted) return; _attempted = true;
            if (!File.Exists(_path)) { Log.Warn("[Weather] Storm sound missing: "+_path); return; }
            _load = Scheduler.StartCoroutine(LoadClip());
        }
        private IEnumerator LoadClip()
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(_path).AbsoluteUri,AudioType.WAV))
            {
                _request = request;
                yield return request.SendWebRequest(); _request = null;
                if (request.result != UnityWebRequest.Result.Success) { Log.Warn("[Weather] Storm sound load failed: "+request.error); yield break; }
                _clip = DownloadHandlerAudioClip.GetContent(request);
                if (_clip == null) { Log.Warn("[Weather] Storm sound could not be decoded."); yield break; }
                _clip.hideFlags = HideFlags.HideAndDontSave;
                _host = new GameObject("WW.Weather.Sound") { hideFlags = HideFlags.HideAndDontSave };
                _host.transform.SetParent(Runner.Host.transform,false);
                _source = _host.AddComponent<AudioSource>(); _source.playOnAwake = false; _source.loop = true;
                _source.spatialBlend = 0; _source.dopplerLevel = 0; _source.volume = 0; _source.clip = _clip;
                Log.Always("[Weather] Storm sound loaded: sound/sandstorm.wav; silent beyond 1200 m, full volume within 150 m and inside; one shared loop.");
            }
            _load = null;
        }
        internal void Strike(float clock, Vector3 position, Vector3 eye, float strength) { _thunder.Strike(clock,position,eye,strength); }
        internal void Tick(WeatherRegion[] regions, float[] ages, int count, Camera camera, float clock)
        {
            _thunder.Tick(clock,camera);
            if (_source == null) return;
            if (!GameRefs.WorldRunning || camera == null || AudioListener.pause) { if (_source.isPlaying) _source.Pause(); return; }
            if (Time.unscaledTime >= _nextSample)
            {
                _nextSample = Time.unscaledTime+.2f; _target = 0;
                Vector3 eye = camera.worldToCameraMatrix.inverse.MultiplyPoint3x4(Vector3.zero);
                for (int i = 0; i < count; i++)
                {
                    Vector3 local = Quaternion.Euler(0,-regions[i].Heading,0)*(eye-Origin.Scene(regions[i].WorldCenter));
                    Vector2 p = new Vector2(local.x,local.z), radii = regions[i].Radii;
                    float distance = 0;
                    if (WeatherRegion.EdgeDistance(p.x/radii.x,p.y/radii.y)<0)
                    {
                        float closest = float.MaxValue; Vector2 a = Vector2.Scale(_edge[63],radii);
                        for (int point = 0; point < _edge.Length; point++)
                        {
                            Vector2 b = Vector2.Scale(_edge[point],radii), line = b-a;
                            float t = Mathf.Clamp01(Vector2.Dot(p-a,line)/Mathf.Max(.001f,line.sqrMagnitude));
                            closest = Mathf.Min(closest,(p-a-line*t).sqrMagnitude); a = b;
                        }
                        distance = Mathf.Sqrt(closest);
                    }
                    float life = Mathf.SmoothStep(0,1,Mathf.Clamp01(ages[i]/6))*Mathf.SmoothStep(0,1,Mathf.Clamp01((600-ages[i])/20));
                    float level = Mathf.SmoothStep(0,1,Mathf.Clamp01((1200-distance)/1050))*life;
                    _target = Mathf.Max(_target,level);
                }
            }
            float volume = Mathf.Lerp(_volume,_target,1-Mathf.Exp(-Time.unscaledDeltaTime*2));
            if (volume<.001f && _target==0) volume = 0;
            if (Mathf.Abs(volume-_volume)>.0001f || volume==0 && _volume!=0) { _source.volume = volume; _volume = volume; }
            if (volume==0 && _target==0) { if (_source.isPlaying) _source.Pause(); }
            else if (!_source.isPlaying) { if (_source.time>0) _source.UnPause(); else _source.Play(); }
        }
        internal void Stop()
        {
            _thunder.Stop();
            _target = 0; _volume = 0; _nextSample = 0;
            if (_source != null) { _source.Stop(); _source.volume = 0; }
        }
        public void Dispose()
        {
            Stop();
            _thunder.Dispose();
            if (_load != null) Scheduler.StopCoroutine(_load); _load = null;
            if (_request != null) { try { _request.Dispose(); } catch { } _request = null; }
            if (_host != null) UnityEngine.Object.Destroy(_host); _host = null; _source = null;
            if (_clip != null) UnityEngine.Object.Destroy(_clip); _clip = null;
        }
    }
}
