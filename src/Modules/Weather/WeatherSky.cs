using System;
using UnityEngine;
using UnityEngine.AzureSky;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    // Camera-scoped native sky/lighting. No weather-controller state or GI probes are changed.
    internal sealed class WeatherSky : IDisposable
    {
        private static readonly int[] Colors = {
            Shader.PropertyToID("_Azure_RayleighColor"), Shader.PropertyToID("_Azure_MieColor"),
            Shader.PropertyToID("_Azure_ScatteringColor"), Shader.PropertyToID("_Azure_DynamicCloudColor1"),
            Shader.PropertyToID("_Azure_DynamicCloudColor2"), Shader.PropertyToID("_Azure_StaticCloudColor") };
        private readonly Vector4[] _global = new Vector4[6];
        private readonly Color[] _local = new Color[6];
        private readonly bool[] _has = new bool[6];
        private Camera _camera;
        private Material _sky;
        private Light _sun;
        private Color _ambientSky, _ambientEquator, _ambientGround;
        private float _sunIntensity, _ambientIntensity, _amount;
        private Color _sunColor;
        private float _flash;
        private bool _sunEnabled;
        private bool _applied;
        private static WeatherSky _active;
        private bool _fogLogged;

        internal void Begin(Camera camera, float amount)
        {
            _camera = camera; _amount = amount;
            _active = this;
            Camera.onPreCull += Before;
            Camera.onPostRender += After;
        }
        internal void SetAmount(float amount) { _amount = amount; }
        internal void SetFlash(float strength) { _flash=Mathf.Clamp01(strength); }
        private void Before(Camera camera)
        {
            if (camera != _camera || (_amount < .001f && _flash < .001f) || _applied) return;
            _sky = RenderSettings.skybox; _sun = RenderSettings.sun;
            _ambientSky = RenderSettings.ambientSkyColor; _ambientEquator = RenderSettings.ambientEquatorColor;
            _ambientGround = RenderSettings.ambientGroundColor; _ambientIntensity = RenderSettings.ambientIntensity;
            if (_sun != null) { _sunIntensity = _sun.intensity; _sunColor = _sun.color; _sunEnabled=_sun.enabled;
                _sun.intensity = _sunIntensity*(1-.48f*_amount)+_flash*2.5f; _sun.color = FlashColor(Dust(_sunColor,_amount*.7f));
                if (_flash>.001f) _sun.enabled=true; }
            RenderSettings.ambientIntensity = _ambientIntensity*(1-.22f*_amount)+_flash;
            RenderSettings.ambientSkyColor = FlashColor(Dust(_ambientSky, _amount*.6f));
            RenderSettings.ambientEquatorColor = FlashColor(Dust(_ambientEquator, _amount*.6f));
            RenderSettings.ambientGroundColor = FlashColor(Dust(_ambientGround, _amount*.4f));
            for (int i = 0; i < Colors.Length; i++)
            {
                _global[i] = Shader.GetGlobalVector(Colors[i]);
                Shader.SetGlobalVector(Colors[i], FlashColor(Dust(_global[i], _amount*.94f)));
                _has[i] = _sky != null && _sky.HasProperty(Colors[i]);
                if (!_has[i]) continue;
                _local[i] = _sky.GetColor(Colors[i]);
                _sky.SetColor(Colors[i], FlashColor(Dust(_local[i], _amount*.94f)));
            }
            _applied = true;
        }
        private Color FlashColor(Color color)
        {
            Color tint=new Color(.72f,.8f,1,color.a)*Mathf.Max(.35f,Mathf.Max(color.r,Mathf.Max(color.g,color.b)));
            tint.a=color.a; return Color.Lerp(color,tint,_flash*.8f);
        }
        private static Color Dust(Color source, float amount)
        {
            float value = Mathf.Max(source.r, Mathf.Max(source.g, source.b));
            Color color = WeatherPalette.Main;
            float peak = Mathf.Max(.001f,Mathf.Max(color.r,Mathf.Max(color.g,color.b)));
            Color tint = color*(value*.9f/peak); tint.a = source.a;
            return Color.Lerp(source,tint,amount);
        }
        private void After(Camera camera) { if (camera == _camera) Restore(); }
        internal static void NativeFogBefore(AzureFogScattering __instance, out Material __state)
        {
            __state = null;
            if (!WeatherDiagnostics.FogScope) return;
            var camera = GameRefs.MainCamera;
            if (camera == null || camera.gameObject != __instance.gameObject) return;
            // Use Azure's native unfiltered blit branch instead of its distance-scattering shader.
            __state = __instance.fogScatteringMaterial;
            __instance.fogScatteringMaterial = null;
            var sky = _active;
            if (sky != null && !sky._fogLogged)
            {
                sky._fogLogged = true;
                Log.Always("[Weather] Native Azure distance haze bypassed: gameplay camera uses the unfiltered image; WW regional dust retained.");
            }
        }
        internal static Exception NativeFogAfter(Exception __exception, AzureFogScattering __instance, Material __state)
        {
            if (__instance != null && __state != null) __instance.fogScatteringMaterial = __state;
            return __exception;
        }
        private void Restore()
        {
            if (!_applied) return;
            if (_sun != null) { _sun.intensity = _sunIntensity; _sun.color = _sunColor; _sun.enabled=_sunEnabled; }
            RenderSettings.ambientIntensity = _ambientIntensity;
            RenderSettings.ambientSkyColor = _ambientSky; RenderSettings.ambientEquatorColor = _ambientEquator;
            RenderSettings.ambientGroundColor = _ambientGround;
            for (int i = 0; i < Colors.Length; i++)
            {
                Shader.SetGlobalVector(Colors[i],_global[i]);
                if (_has[i] && _sky != null) _sky.SetColor(Colors[i],_local[i]);
            }
            _applied = false;
        }
        internal void Suspend()
        {
            Restore(); Camera.onPreCull -= Before; Camera.onPostRender -= After;
            if (ReferenceEquals(_active,this)) _active = null;
            _camera = null; _sky = null; _sun = null; _amount = 0; _flash=0;
        }
        public void Dispose() { Suspend(); }
    }
}
