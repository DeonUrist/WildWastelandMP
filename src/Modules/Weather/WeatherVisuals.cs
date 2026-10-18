using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using WildWasteland.Core;

namespace WildWasteland.Modules.Weather
{
    internal sealed class WeatherVisuals : IDisposable
    {
        private AssetBundle _bundle;
        private readonly Mesh[] _funnel = new Mesh[3], _lightning = new Mesh[3];
        private readonly Mesh[] _clouds = new Mesh[3];
        private readonly Mesh[] _canopies = new Mesh[3];
        private readonly Mesh[] _nearMeshes = new Mesh[3];
        private readonly MaterialPropertyBlock[] _properties = new MaterialPropertyBlock[4];
        private Material _cloud, _core, _near, _discharge;
        private Material _smokeMaterial;
        private WeatherSmoke _smoke;
        private WeatherVortexSmoke _vortexSmoke;
        private readonly WeatherPerimeter _perimeter = new WeatherPerimeter();
        private WeatherAtmosphere _atmosphere;
        private WeatherDust _localDust;
        private WeatherDebris _debris;
        private readonly WeatherAudio _audio;
        private readonly int[] _strikes = new int[8];
        private readonly int[] _strikeColumns = new int[8];
        private float _flash;
        private int _drawing;
        private float _nextColor, _clock;
        private Color _dustColor;
        private Color _hurricaneColor;
        private int _paletteRevision = -1;
        private readonly Plane[] _frustum = new Plane[6];
        private readonly float[,] _groundOffset = new float[WeatherModule.MaxRegions,80];
        private readonly bool[,] _hasGround = new bool[WeatherModule.MaxRegions,80];
        private float _nextGroundProbe;
        private int _groundProbe;
        private int _nearDetail = -1;
        private static readonly int Clock = Shader.PropertyToID("_WWWeatherClock");
        private static readonly int Tint = Shader.PropertyToID("_Tint");
        private static readonly int Life = Shader.PropertyToID("_Life"), Air = Shader.PropertyToID("_WWWeatherAir"), Sun = Shader.PropertyToID("_WWWeatherSun"), AzureSun = Shader.PropertyToID("_Azure_SunDirection");
        private static readonly int Hurricane = Shader.PropertyToID("_Hurricane"), Pulse = Shader.PropertyToID("_Pulse"), BackA = Shader.PropertyToID("_BackLightA"), BackB = Shader.PropertyToID("_BackLightB");
        private static readonly int Vortex = Shader.PropertyToID("_Vortex");
        private static readonly int Canopy = Shader.PropertyToID("_Canopy"), FrameBlend = Shader.PropertyToID("_FrameBlend"), RegionCenter = Shader.PropertyToID("_RegionCenter"), AxisX = Shader.PropertyToID("_RegionAxisX"), AxisZ = Shader.PropertyToID("_RegionAxisZ"), RegionSize = Shader.PropertyToID("_RegionSize");
        private static readonly int Steps = Shader.PropertyToID("_Steps");
        private static readonly int Critical = Shader.PropertyToID("_Critical");
        private static readonly int DiagnosticOwner = Shader.PropertyToID("_WWDiagOwner");
        internal bool Ready { get; private set; }

        internal WeatherVisuals(WeatherAudio audio)
        {
            _audio=audio; for (int i=0;i<_strikes.Length;i++) _strikes[i]=int.MinValue;
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Weather.weather.bundle"))
                {
                    if (stream == null) throw new IOException("Embedded Weather.weather.bundle missing");
                    byte[] bytes = new byte[checked((int)stream.Length)];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0) throw new EndOfStreamException("Weather asset stream ended early");
                        offset += read;
                    }
                    _bundle = AssetBundle.LoadFromMemory(bytes);
                }
                if (_bundle == null) throw new IOException("Weather bundle could not be loaded");
                _funnel[0] = Load<Mesh>("vortex-low.asset"); _funnel[1] = Load<Mesh>("vortex.asset"); _funnel[2] = Load<Mesh>("vortex-max.asset");
                _canopies[0] = Load<Mesh>("canopy-low.asset"); _canopies[1] = Load<Mesh>("canopy.asset"); _canopies[2] = Load<Mesh>("canopy-max.asset");
                _lightning[0] = Load<Mesh>("lightning-low.asset"); _lightning[1] = Load<Mesh>("lightning.asset"); _lightning[2] = Load<Mesh>("lightning-max.asset");
                _clouds[0] = Load<Mesh>("cloud-ring-8.asset"); _clouds[1] = Load<Mesh>("cloud-ring-16.asset"); _clouds[2] = Load<Mesh>("cloud-ring-24.asset");
                _nearMeshes[0] = Load<Mesh>("near-dust-8.asset"); _nearMeshes[1] = Load<Mesh>("near-dust-16.asset"); _nearMeshes[2] = Load<Mesh>("near-dust-24.asset");
                _cloud = new Material(Load<Material>("cloud.mat"));
                _smokeMaterial = new Material(Load<Material>("smoke-particles.mat")); _smoke = new WeatherSmoke(_smokeMaterial);
                _vortexSmoke = new WeatherVortexSmoke(Load<Material>("hurricane-sand.mat"),_smokeMaterial);
                _localDust = new WeatherDust();
                _debris = new WeatherDebris();
                _core = new Material(Load<Material>("vortex.mat")); _near = new Material(Load<Material>("near-dust.mat"));
                _discharge = new Material(Load<Material>("lightning.mat"));
                _atmosphere = new WeatherAtmosphere(Load<Material>("atmosphere.mat"));
                for (int i = 0; i < _properties.Length; i++) _properties[i] = new MaterialPropertyBlock();
                Ready = _smokeMaterial.shader.isSupported && _core.shader.isSupported && _discharge.shader.isSupported && _atmosphere.Supported;
                if (!Ready) throw new IOException("Weather shader unsupported on this graphics device");
                Log.Always("[Weather] Cohesive front revision: solid regional dust, rolling wall/cloud bank, camera-scoped Azure sky and sunlight; 4/6/8 fog samples.");
                Log.Always("[Weather] Vanilla local sand clouds selected; native motion/material preserved, desaturated brown tint; no managed particle simulation or terrain probes.");
                Log.Always("[Weather] Continuous dust volume with spiral tops merging into wall-colored ceiling clouds; thick orbiting ground dust and open eye.");
                Log.Always("[Weather] Width-correct density filtering; visible strikes flash camera-scoped world lighting and queue distance-delayed thunder.");
            }
            catch (Exception error) { Dispose(); Log.Error("[Weather] Preview assets failed", error); }
        }

        private T Load<T>(string name) where T : UnityEngine.Object
        {
            var asset = _bundle.LoadAsset<T>("assets/weather/generated/" + name);
            if (asset == null) throw new IOException("Weather asset missing: " + name);
            return asset;
        }

        internal void Begin(float clock, Camera camera)
        {
            _drawing = 0; _clock = clock; _flash=0; _atmosphere.Begin(camera);
            _smoke.Begin(camera);
            _vortexSmoke.Begin(camera);
            var policy = Services.WeatherVisualPolicy;
            _debris.Begin(camera,policy != null ? policy.WeatherDetail : WeatherVisualDetail.Balanced);
            GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
            Shader.SetGlobalFloat(Clock, clock);
            bool paletteChanged = _paletteRevision != WeatherPalette.Revision;
            if (Time.unscaledTime < _nextColor && !paletteChanged) return;
            _nextColor = Time.unscaledTime + .5f;
            if (WeatherDiagnostics.Frozen && _dustColor.a > 0 && !paletteChanged) return;
            _paletteRevision = WeatherPalette.Revision;
            var sun = RenderSettings.sun;
            Vector3 direction = sun != null ? -sun.transform.forward : (Vector3)Shader.GetGlobalVector(AzureSun);
            if (direction.sqrMagnitude < .01f) direction = new Vector3(.3f, .7f, -.4f);
            direction.Normalize();
            float light = sun != null && sun.enabled ? Mathf.Clamp(sun.intensity, .06f, 1) : Mathf.Lerp(.07f, 1, Mathf.SmoothStep(0, 1, (direction.y + .12f) / .37f));
            Shader.SetGlobalVector(Sun, direction);
            Color air = WeatherPalette.Main*(light*.78f); air.a = 1;
            _dustColor = WeatherPalette.Wall*light; _dustColor.a = 1;
            _hurricaneColor = WeatherPalette.Hurricane*light; _hurricaneColor.a = 1;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) { air = air.linear; _dustColor = _dustColor.linear; _hurricaneColor = _hurricaneColor.linear; }
            Shader.SetGlobalColor(Air, air);
        }

        internal void Draw(WeatherRegion region, Camera camera, WeatherVisualDetail detail, float life, float critical, Vector3[] columns, int columnCount, int regionIndex)
        {
            if (!Ready) return;
            Vector3 center = Origin.Scene(region.WorldCenter);
            if ((camera.transform.position - center).sqrMagnitude > Mathf.Pow(region.Radii.x+8000,2)) return;
            var rotation = Quaternion.Euler(0, region.Heading, 0);
            Vector3 eye = camera.worldToCameraMatrix.inverse.MultiplyPoint3x4(Vector3.zero);
            int first = -1, second = -1; float nearest = float.MaxValue, next = float.MaxValue;
            if (critical > 0)
                for (int i = 0; i < columnCount; i++)
                {
                    float distance = (center+rotation*columns[i]-eye).sqrMagnitude;
                    if (distance < nearest) { next = nearest; second = first; nearest = distance; first = i; }
                    else if (distance < next) { next = distance; second = i; }
                }
            float height = Mathf.Lerp(1050f, 1500f, critical);
            float phase = region.Heading * .037f + _drawing * 2.7f;
            float pulseA = region.Hurricane ? Discharge(_clock + phase, 7.7f) : 0;
            float pulseB = region.Hurricane && detail != WeatherVisualDetail.Low ? Discharge(_clock + phase + 3.1f, 9.3f) : 0;
            Vector3 lightA = center + rotation * ((first >= 0 ? columns[first] : Vector3.zero)+new Vector3(0,500,400));
            Vector3 lightB = center + rotation * ((second >= 0 ? columns[second] : Vector3.zero)+new Vector3(0,600,400));
            Vector4 backA = first >= 0 ? new Vector4(lightA.x, lightA.y, lightA.z, (.7f + pulseA) * life * critical) : Vector4.zero;
            Vector4 backB = second >= 0 ? new Vector4(lightB.x, lightB.y, lightB.z, (.7f + pulseB) * life * critical) : Vector4.zero;
            _atmosphere.Add(region, center, height, life, critical, backA, backB);
            var properties = _properties[_drawing++]; properties.SetFloat(Life, life); properties.SetFloat(Hurricane, 0);
            properties.SetFloat(Vortex, 0); properties.SetColor(Tint, _dustColor);
            properties.SetFloat(Canopy,0); properties.SetFloat(FrameBlend,detail == WeatherVisualDetail.Low ? 0 : 1);
            properties.SetFloat(Critical,critical);
            properties.SetFloat(Steps,detail == WeatherVisualDetail.Low ? 12 : detail == WeatherVisualDetail.Max ? 24 : 18);
            properties.SetVector(RegionCenter,center); properties.SetVector(AxisX,rotation*Vector3.right); properties.SetVector(AxisZ,rotation*Vector3.forward);
            properties.SetVector(RegionSize,new Vector4(region.Radii.x,region.Radii.y,0,0));
            properties.SetVector(BackA, backA); properties.SetVector(BackB, backB); properties.SetVector(Pulse, new Vector4(pulseA, pulseB, 0, 0));
            properties.SetFloat("_Spiral",0); properties.SetFloat("_Perimeter",1);
            _perimeter.Update(region); int sections = _perimeter.Count;
            for (int section = 0; section < sections && (WeatherDiagnostics.Wall || WeatherDiagnostics.Canopy); section++)
            {
                float span = _perimeter.Spans[section];
                Vector3 position = center+rotation*_perimeter.Centers[section];
                Quaternion wallRotation = rotation*_perimeter.Rotations[section];
                var bounds = new Bounds(position+Vector3.up*900,new Vector3(span*1.3f+2600,2600,span*1.3f+2600));
                if (!GeometryUtility.TestPlanesAABB(_frustum,bounds)) continue;
                properties.SetVector(AxisX,wallRotation*Vector3.right); properties.SetVector(AxisZ,wallRotation*Vector3.forward);
                bool leading = Vector3.Dot(-(wallRotation*Vector3.forward),region.Velocity) > region.Velocity.magnitude*.45f;
                properties.SetFloat("_LeadingFront",leading ? 1 : 0);
                if (WeatherDiagnostics.Enabled) properties.SetFloat(DiagnosticOwner,regionIndex*48+section+1);
                _smoke.Draw(regionIndex*48+section,position,wallRotation,span,0,detail,leading,properties);
            }
            if (critical <= 0 || columnCount == 0 || !WeatherDiagnostics.Core) return;
            if (WeatherDiagnostics.Single && WeatherDiagnostics.SelectedColumn < 0 && first >= 0)
            {
                WeatherDiagnostics.SelectedRegion = regionIndex; WeatherDiagnostics.SelectedColumn = first;
                Log.Always("[WeatherDiag] Selected hurricane: region="+regionIndex+", column="+first+"; world="+Origin.World(center+rotation*columns[first]).ToString("F3"));
            }
            properties.SetFloat(Vortex, 1); properties.SetFloat(Life, life * critical);
            properties.SetColor(Tint,_hurricaneColor);
            properties.SetColor("_CloudTint",_dustColor);
            properties.SetVector(AxisX,rotation*Vector3.right); properties.SetVector(AxisZ,rotation*Vector3.forward);
            properties.SetFloat("_Spiral",1);
            Vector3 relativeEye = Quaternion.Inverse(rotation)*(eye-center);
            float reveal = Mathf.SmoothStep(0,1,Mathf.Clamp01((WeatherRegion.EdgeDistance(relativeEye.x/region.Radii.x,relativeEye.z/region.Radii.y)-.025f)/.195f));
            properties.SetFloat(Life,life*critical*reveal);
            int probe = -1;
            if (Time.unscaledTime >= _nextGroundProbe && Time.timeScale > 0 && !WeatherDiagnostics.Frozen)
            { _nextGroundProbe = Time.unscaledTime+.15f; probe = _groundProbe++%columnCount; }
            for (int i = 0; i < columnCount; i++)
            {
                if (WeatherDiagnostics.Single && (WeatherDiagnostics.SelectedRegion != regionIndex || WeatherDiagnostics.SelectedColumn != i)) continue;
                Vector3 position = center+rotation*columns[i];
                if (_hasGround[regionIndex,i]) position.y += _groundOffset[regionIndex,i];
                // Ground is layer 14 in Sources/data/settings.md. Bounded visual probes,
                // at most one per .15 s globally; no terrain/scene collection scan.
                if (i == probe && (position-eye).sqrMagnitude < 2500f*2500f)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(position+Vector3.up*2500,Vector3.down,out hit,5000,1<<14,QueryTriggerInteraction.Ignore))
                    {
                        float offset = hit.point.y-center.y-2;
                        _groundOffset[regionIndex,i] = _hasGround[regionIndex,i] ? Mathf.Lerp(_groundOffset[regionIndex,i],offset,.25f) : offset;
                        _hasGround[regionIndex,i] = true; position.y = center.y+_groundOffset[regionIndex,i];
                    }
                }
                if ((position-eye).sqrMagnitude > 6500f*6500f || !GeometryUtility.TestPlanesAABB(_frustum,new Bounds(position+Vector3.up*1100,new Vector3(9000,4000,9000)))) continue;
                Vector3 back = position+rotation*new Vector3(0,600,350);
                properties.SetVector(BackA,new Vector4(back.x,back.y,back.z,(.9f+pulseA)*life*critical)); properties.SetVector(BackB,Vector4.zero);
                _atmosphere.AddEye(position,300*WeatherVortexSmoke.Width(regionIndex*80+i),1600,life*critical,eye);
                if (WeatherDiagnostics.Enabled) properties.SetFloat(DiagnosticOwner,1000+regionIndex*80+i);
                if (reveal > .001f) _vortexSmoke.Draw(regionIndex*80+i,position,rotation,detail,properties);
                if (reveal > .1f && critical > .5f && (position-eye).sqrMagnitude < 1800f*1800f) _debris.Draw(regionIndex*80+i,position,rotation,detail);
            }
            if (WeatherDiagnostics.Full && (!WeatherDiagnostics.Enabled || WeatherDiagnostics.Output == 0))
                for (int channel=0;channel<2;channel++)
                {
                    int index=regionIndex*2+channel;
                    int cycle=Mathf.FloorToInt((_clock+phase+(channel==0 ? 0 : 3.1f))/(channel==0 ? 7.7f : 9.3f));
                    int column=channel==0 ? first : second; float pulse=channel==0 ? pulseA : pulseB;
                    if (_strikes[index]==cycle) column=_strikeColumns[index];
                    float envelope=life*critical*reveal;
                    if (column<0 || pulse<=0 || envelope<.001f) continue;
                    Vector3 source=center+rotation*columns[column];
                    if (_hasGround[regionIndex,column]) source.y+=_groundOffset[regionIndex,column];
                    float distance=(source-eye).magnitude; if (distance>6500) continue;
                    properties.SetVector(Pulse,channel==0 ? new Vector4(pulse,0,0,0) : new Vector4(0,pulse,0,0));
                    Draw(_lightning[(int)detail],Matrix4x4.TRS(source+rotation*new Vector3(0,0,510),rotation,Vector3.one),_discharge,camera,properties);
                    _flash=Mathf.Max(_flash,pulse*envelope/(1+distance*distance/(3000f*3000f)));
                    if (_strikes[index]!=cycle && !WeatherDiagnostics.Frozen && GameRefs.WorldRunning)
                    { _strikes[index]=cycle; _strikeColumns[index]=column; _audio.Strike(_clock,source,eye,envelope); }
                }
        }

        private static float Discharge(float clock, float period)
        {
            float t = Mathf.Repeat(clock, period);
            return Mathf.Max(Mathf.Clamp01(1 - t / .09f), Mathf.Clamp01(1 - Mathf.Abs(t - .19f) / .065f) * .65f);
        }

        internal void End(Camera camera, WeatherVisualDetail detail)
        {
            _atmosphere.SetFlash(_flash);
            if (_nearDetail != (int)detail) { _nearDetail = (int)detail; _near.SetFloat(FrameBlend,detail == WeatherVisualDetail.Low ? 0 : 1); }
            _atmosphere.End(camera, detail, _nearMeshes[(int)detail], _near);
            _debris.End();
            if (_drawing > 0) _localDust.Tick(camera,_clock,_atmosphere.DustStrength,_atmosphere.LocalWind,detail,WeatherPalette.Sand);
        }
        internal void Suspend() { if (_atmosphere != null) _atmosphere.Suspend(); if (_smoke != null) _smoke.Suspend(); if (_vortexSmoke != null) _vortexSmoke.Suspend(); if (_localDust != null) _localDust.Suspend(); if (_debris != null) _debris.Suspend(); }
        internal bool TryGround(int region,int column,out float offset) { offset=_groundOffset[region,column]; return _hasGround[region,column]; }
        internal void SetGround(int region,int column,float offset) { _groundOffset[region,column]=offset; _hasGround[region,column]=true; }
        internal void ClearGround() { Array.Clear(_hasGround,0,_hasGround.Length); _groundProbe = 0; for (int i=0;i<_strikes.Length;i++) _strikes[i]=int.MinValue; }
        internal void CaptureDiagnostic(WeatherDiagnostics.Snapshot data)
        {
            data.DustColor = _dustColor; data.AirColor = Shader.GetGlobalVector(Air); data.SunDirection = Shader.GetGlobalVector(Sun);
            data.GroundOffsets = new float[WeatherModule.MaxRegions*80]; data.GroundValid = new bool[WeatherModule.MaxRegions*80];
            for (int r = 0; r < WeatherModule.MaxRegions; r++) for (int c = 0; c < 80; c++)
            { data.GroundOffsets[r*80+c] = _groundOffset[r,c]; data.GroundValid[r*80+c] = _hasGround[r,c]; }
            _localDust.CaptureDiagnostic(data);
        }
        internal void RestoreDiagnostic(WeatherDiagnostics.Snapshot data)
        {
            _dustColor = data.DustColor; Shader.SetGlobalVector(Air,data.AirColor); Shader.SetGlobalVector(Sun,data.SunDirection);
            ClearGround();
            if (data.GroundOffsets != null && data.GroundValid != null && data.GroundOffsets.Length <= WeatherModule.MaxRegions*80 && data.GroundOffsets.Length == data.GroundValid.Length)
                for (int r = 0; r < data.GroundOffsets.Length/80; r++) for (int c = 0; c < 80; c++)
                { _groundOffset[r,c] = data.GroundOffsets[r*80+c]; _hasGround[r,c] = data.GroundValid[r*80+c]; }
            _localDust.RestoreDiagnostic(data);
        }
        private void Draw(Mesh mesh, Matrix4x4 matrix, Material material, Camera camera, MaterialPropertyBlock properties)
        {
            Graphics.DrawMesh(mesh,matrix,material,2,camera,0,properties,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
        }

        public void Dispose()
        {
            Ready = false;
            if (_localDust != null) _localDust.Dispose(); _localDust = null;
            if (_debris != null) _debris.Dispose(); _debris = null;
            if (_smoke != null) _smoke.Dispose(); _smoke = null;
            if (_vortexSmoke != null) _vortexSmoke.Dispose(); _vortexSmoke = null;
            if (_smokeMaterial != null) UnityEngine.Object.Destroy(_smokeMaterial); _smokeMaterial = null;
            if (_atmosphere != null) _atmosphere.Dispose(); _atmosphere = null;
            if (_near != null) UnityEngine.Object.Destroy(_near); _near = null;
            if (_discharge != null) UnityEngine.Object.Destroy(_discharge); _discharge = null;
            if (_cloud != null) UnityEngine.Object.Destroy(_cloud); _cloud = null;
            if (_core != null) UnityEngine.Object.Destroy(_core); _core = null;
            if (_bundle != null) _bundle.Unload(true); _bundle = null;
        }
    }
}
