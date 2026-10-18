using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    internal sealed class WeatherThunder : IDisposable
    {
        private struct Pending { internal bool Used; internal float Due, Strength; internal Vector3 World; }
        private readonly Pending[] _pending = new Pending[32];
        private readonly AudioSource[] _voices = new AudioSource[16];
        private readonly string _path;
        private AudioClip _clip;
        private GameObject _host;
        private Coroutine _load;
        private UnityWebRequest _request;
        private bool _attempted, _paused;
        private float _lastClock;
        private int _nextVoice;
        internal WeatherThunder(string path) { _path=path; }
        internal void Load()
        {
            if (_attempted) return; _attempted=true;
            if (!File.Exists(_path)) { Log.Warn("[Weather] Thunder sound missing: "+_path); return; }
            _load=Scheduler.StartCoroutine(LoadClip());
        }
        private IEnumerator LoadClip()
        {
            using (var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(_path).AbsoluteUri,AudioType.MPEG))
            {
                var handler=(DownloadHandlerAudioClip)request.downloadHandler; handler.compressed=false; handler.streamAudio=false;
                _request=request; yield return request.SendWebRequest(); _request=null;
                if (request.result!=UnityWebRequest.Result.Success) { Log.Warn("[Weather] Thunder load failed: "+request.error); yield break; }
                _clip=DownloadHandlerAudioClip.GetContent(request);
                if (_clip==null) { Log.Warn("[Weather] Thunder could not be decoded."); yield break; }
                _clip.hideFlags=HideFlags.HideAndDontSave;
                _host=new GameObject("WW.Weather.Thunder") { hideFlags=HideFlags.HideAndDontSave };
                _host.transform.SetParent(Runner.Host.transform,false);
                for (int i=0;i<_voices.Length;i++)
                {
                    var source=_host.AddComponent<AudioSource>(); source.playOnAwake=false; source.loop=false;
                    source.spatialBlend=0; source.dopplerLevel=0; _voices[i]=source;
                }
                Log.Always("[Weather] Thunder loaded: sound/thunder.mp3; distance / 343 m/s delay, 16 bounded voices.");
            }
            _load=null;
        }
        internal void Strike(float clock, Vector3 position, Vector3 eye, float strength)
        {
            float distance=Vector3.Distance(position,eye), delay=distance/343f;
            for (int i=0;i<_pending.Length;i++) if (!_pending[i].Used)
            {
                _pending[i]=new Pending { Used=true,Due=clock+delay,World=Origin.World(position),Strength=strength };
                if (Log.Verbose) Log.Info("[Weather] Lightning: distance="+distance.ToString("0")+" m; thunder delay="+delay.ToString("0.00")+" s");
                return;
            }
        }
        internal void Tick(float clock, Camera camera)
        {
            if (clock<_lastClock) Stop(); _lastClock=clock;
            bool paused=!GameRefs.WorldRunning || WeatherDiagnostics.Frozen || AudioListener.pause || camera==null;
            if (paused!=_paused)
            {
                for (int i=0;i<_voices.Length;i++) if (_voices[i]!=null) { if (paused) _voices[i].Pause(); else _voices[i].UnPause(); }
                _paused=paused;
            }
            if (paused || _clip==null) return;
            Matrix4x4 view=camera.worldToCameraMatrix.inverse; Vector3 eye=view.MultiplyPoint3x4(Vector3.zero);
            for (int i=0;i<_pending.Length;i++)
            {
                var pending=_pending[i]; if (!pending.Used || pending.Due>clock) continue;
                _pending[i].Used=false;
                int voice=-1; for (int j=0;j<_voices.Length;j++) if (!_voices[j].isPlaying) { voice=j; break; }
                if (voice<0) voice=_nextVoice++%_voices.Length;
                var source=_voices[voice]; source.Stop();
                Vector3 delta=Origin.Scene(pending.World)-eye; float distance=delta.magnitude;
                source.panStereo=Vector3.Dot(delta/Mathf.Max(1,distance),view.MultiplyVector(Vector3.right))*.7f;
                source.PlayOneShot(_clip,pending.Strength/(1+distance*distance/(1800f*1800f)));
                if (Log.Verbose) Log.Info("[Weather] Thunder played at weather time "+clock.ToString("0.00")+" s");
            }
        }
        internal void Stop()
        {
            Array.Clear(_pending,0,_pending.Length); _lastClock=0; _paused=false;
            for (int i=0;i<_voices.Length;i++) if (_voices[i]!=null) _voices[i].Stop();
        }
        public void Dispose()
        {
            Stop(); if (_load!=null) Scheduler.StopCoroutine(_load); _load=null;
            if (_request!=null) { try { _request.Dispose(); } catch { } _request=null; }
            if (_host!=null) UnityEngine.Object.Destroy(_host); _host=null;
            if (_clip!=null) UnityEngine.Object.Destroy(_clip); _clip=null;
        }
    }
}
