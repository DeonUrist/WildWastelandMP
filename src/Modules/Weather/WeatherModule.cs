using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.Weather
{
    // Native encounters keep their save anchors; Weather owns presentation and annular hazards.
    public sealed partial class WeatherModule : ModuleBase, IWeatherRegions, IMultiplayerPresentation
    {
        private readonly WeatherRegion[] _regions = new WeatherRegion[4];
        private readonly float[] _ages = new float[4];
        private readonly bool[] _criticalLogged = new bool[4];
        private int _count;
        private float _clock;
        private ConfigEntry<bool> _mapAreas;
        private ConfigEntry<float> _density, _width, _depth;
        private readonly Vector3[] _columns = new Vector3[80];
        private int _columnCount;
        private float _lastDensity = -1, _lastWidth, _lastDepth;
        private WeatherVisuals _visuals;
        private WeatherAudio _audio;
        private WeatherNative _native;
        private WeatherHazards _hazards;
        private readonly WeatherNative.Storm[] _anchors = new WeatherNative.Storm[4];
        private Job _draw;
        private bool _running;
        public override string Id { get { return "Weather"; } }
        public override string DisplayName { get { return "Weather"; } }
        public int Count { get { return _count; } }
        public bool MapAreas { get { return _mapAreas.Value; } set { _mapAreas.Value = value; } }
        public float HurricaneDensity { get { return _density.Value; } set { _density.Value = value; } }
        public float FrontWidthKm { get { return _width.Value; } set { _width.Value = value; } }
        public float DepthKm { get { return _depth.Value; } set { _depth.Value = value; } }

        public override void BindConfig(ModuleConfig cfg)
        {
            WeatherPalette.Bind(cfg);
            _mapAreas = cfg.Player("Map", "Storm areas", true,
                "Show moving Weather storm areas on the map. Critical storms lift vehicles and damage actors in hurricane sand walls; eyes are safe.");
            _density = cfg.Player("Storms", "Hurricanes per km²", .8f, "Columns per square kilometre of critical storm area, rounded to the nearest whole column. Zero disables columns. Applies live and does not change with Performance presets.", new AcceptableValueRange<float>(0,1));
            _width = cfg.Player("Storms", "Front width (km)", 20f, "Width of the broad leading sand front. Applies live to preview regions and their map areas.", new AcceptableValueRange<float>(6,20));
            _depth = cfg.Player("Storms", "Depth (km)", 4f, "Depth behind the front; sand fades and the footprint narrows toward the rear. Applies live.", new AcceptableValueRange<float>(1,4));
        }

        public override void Init()
        {
            WeatherDiagnostics.Reset();
            _running = true; _count = 0; _clock = 0;
            if (!Core.Patches.Patch(this,Core.Patches.Method(typeof(UnityEngine.AzureSky.AzureFogScattering),"OnRenderImage"),
                prefix: Core.Patches.Method(typeof(WeatherSky),"NativeFogBefore"), finalizer: Core.Patches.Method(typeof(WeatherSky),"NativeFogAfter")))
                throw new System.InvalidOperationException("Weather requires the native Azure fog scope.");
            _audio = new WeatherAudio(System.IO.Path.Combine(Dir,"sound","sandstorm.wav"));
            Services.Weather = this;
            _draw = Scheduler.EveryFrame(this, "Moving storm visuals", Tick, Phase.LateUpdateLast);
            _draw.Enabled = false;
            _native = new WeatherNative(this); _hazards = new WeatherHazards();
            StartNetwork();
            Scheduler.EveryFrame(this,"Weather.HurricaneHazards",TickHazards,Phase.FixedUpdate);
            Scheduler.Gui(this,"Weather diagnostic label",WeatherDiagnostics.DrawOverlay,WeatherDiagnostics.WantsOverlay);
        }

        public bool TryGet(int index, out WeatherRegion region)
        {
            region = index >= 0 && index < _count ? _regions[index] : default(WeatherRegion);
            return index >= 0 && index < _count;
        }

        private void Spawn(bool hurricane, bool here)
        {
            if (!_running || !GameRefs.InPlay || GameRefs.Player == null || GameRefs.MainCamera == null) return;
            if (_count == _regions.Length) { Log.Warn("[Weather] Four previews are already active; clear them before spawning more."); return; }
            if (_visuals == null) _visuals = new WeatherVisuals(_audio);
            if (!_visuals.Ready) { Log.Warn("[Weather] Preview assets unavailable. See the earlier Weather asset error."); return; }
            _audio.Load();
            Vector3 forward = GameRefs.MainCamera.transform.forward; forward.y = 0;
            if (forward.sqrMagnitude < .01f) forward = GameRefs.Player.transform.forward;
            forward.y = 0; forward.Normalize();
            Vector3 center = GameRefs.Player.transform.position; center.y -= 1.5f;
            UpdateLayout();
            if (!here) center += forward * (_depth.Value * 500 + (hurricane ? 2000f : 700f));
            var anchor = _native.Preview(center,Quaternion.LookRotation(-forward));
            if (anchor == null) return;
            _regions[_count] = new WeatherRegion
            {
                WorldCenter = Origin.World(center), Radii = new Vector2(_width.Value*500, _depth.Value*500), Velocity = -forward * 20f,
                Heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, Hurricane = hurricane
            };
            _anchors[_count] = anchor; anchor.Selected = true;
            _criticalLogged[_count] = false; _ages[_count++] = 0; _draw.Enabled = true;
            Log.Always("[Weather] Spawned " + (hurricane ? "critical" : "ordinary") + " rounded-front storm; movement 20 m/s; critical hurricane walls use native lifting/damage with safe eyes. Manual storms are not saved.");
        }

        internal void ReconcileNative()
        {
            if (!_running || _native == null || GameRefs.Player == null) return;
            for (int i=_count-1;i>=0;i--)
                if (_anchors[i]!=null && _anchors[i].Root==null) RemoveRegion(i);
            Vector3 player=GameRefs.Player.transform.position;
            for (int n=0;n<_native.Storms.Length;n++)
            {
                var storm=_native.Storms[n];
                if (storm.Root==null || !storm.Ready || storm.Owned || storm.Selected || !storm.Root.activeInHierarchy) continue;
                float distance=(storm.Root.transform.position-player).sqrMagnitude;
                if (distance>Mathf.Pow(_width.Value*500+8000,2)) continue;
                if (_count==_regions.Length)
                {
                    int farthest=-1; float far=distance;
                    for (int i=0;i<_count;i++)
                    {
                        if (_anchors[i]==null || _anchors[i].Owned) continue;
                        float d=(Origin.Scene(_regions[i].WorldCenter)-player).sqrMagnitude;
                        if (d>far) { far=d; farthest=i; }
                    }
                    if (farthest<0) continue;
                    RemoveRegion(farthest);
                }
                if (_visuals==null) _visuals=new WeatherVisuals(_audio);
                if (!_visuals.Ready) return;
                _audio.Load(); UpdateLayout();
                Vector3 forward=-storm.Root.transform.forward; forward.y=0; forward.Normalize();
                _regions[_count]=new WeatherRegion { WorldCenter=Origin.World(storm.Root.transform.position),
                    Radii=new Vector2(_width.Value*500,_depth.Value*500), Heading=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg,
                    Velocity=storm.Body!=null ? storm.Body.velocity : -forward*25, Hurricane=true };
                _anchors[_count]=storm; _ages[_count]=storm.Age; _criticalLogged[_count]=true;
                storm.Selected=true; _count++; _draw.Enabled=true;
                if (_visuals!=null) _visuals.ClearGround();
                Log.Always("[Weather] Natural encounter now drives a WW critical storm: "+storm.Root.name+"; native movement/save list retained.");
            }
        }
        private void RemoveRegion(int index)
        {
            if (_native!=null) _native.Remove(_anchors[index]);
            _count--; _regions[index]=_regions[_count]; _ages[index]=_ages[_count];
            _criticalLogged[index]=_criticalLogged[_count]; _anchors[index]=_anchors[_count]; _anchors[_count]=null;
            if (_visuals!=null) _visuals.ClearGround();
        }
        private void TickHazards()
        {
            if (!_running || _native==null || _native.Saving || !GameRefs.InGame || WeatherDiagnostics.Frozen) return;
            for (int i=0;i<_count;i++)
            {
                var anchor=_anchors[i];
                if (anchor==null || anchor.Root==null || !anchor.Root.activeInHierarchy || _ages[i]<30 || _ages[i]>580) continue;
                _hazards.Tick(_regions[i],i,anchor,_columns,_columnCount,_visuals,_native);
            }
        }

        private static float Sequence(int index, int radix)
        {
            float value = 0, weight = 1f/radix;
            while (index > 0) { value += index % radix * weight; index /= radix; weight /= radix; }
            return value;
        }
        private void UpdateLayout()
        {
            if (_lastDensity == _density.Value && _lastWidth == _width.Value && _lastDepth == _depth.Value) return;
            _lastDensity = _density.Value; _lastWidth = _width.Value; _lastDepth = _depth.Value;
            if (_visuals != null) _visuals.ClearGround();
            var radii = new Vector2(_lastWidth*500,_lastDepth*500);
            float area = new WeatherRegion { Radii = radii }.AreaSquareKm;
            int requested = Mathf.Clamp(Mathf.RoundToInt(area*_lastDensity),0,_columns.Length);
            _columnCount = 0;
            float occupied = 0, available = radii.x*1.7f;
            for (int i = 0; i < requested; i++)
            {
                float radius = WeatherVortexSmoke.TopRadius*WeatherVortexSmoke.TopWidth(i), gap = i == 0 ? 0 : 350;
                if (occupied+gap+radius*2 > available) break;
                _columns[_columnCount++] = new Vector3(occupied+gap+radius,0,Mathf.Sin(i*2.4f)*radii.y*.16f);
                occupied += gap+radius*2;
            }
            for (int i = 0; i < _columnCount; i++) _columns[i].x -= occupied*.5f;
            for (int i = 0; i < _count; i++) { var region = _regions[i]; region.Radii = radii; _regions[i] = region; }
            Log.Always("[Weather] Front layout: " + _lastWidth.ToString("0.#",CultureInfo.InvariantCulture) + " x " + _lastDepth.ToString("0.#",CultureInfo.InvariantCulture) + " km; " + area.ToString("0.##",CultureInfo.InvariantCulture) + " km²; " + _columnCount + " hurricanes placed of " + requested + " requested at " + _lastDensity.ToString("0.##",CultureInfo.InvariantCulture) + "/km²; varied widths, minimum top gap 350 m (critical phase only).");
        }

        private void Tick()
        {
            if (!_running || _count == 0) return;
            UpdateLayout();
            float dt = GameRefs.InGame && !WeatherDiagnostics.Frozen ? Time.deltaTime : 0;
            WeatherDiagnostics.Show(GameRefs.InPlay);
            _clock += dt;
            for (int i = _count - 1; i >= 0; i--)
            {
                var anchor = _anchors[i];
                if (anchor != null && anchor.Root == null) { RemoveRegion(i); continue; }
                var region = _regions[i];
                if (anchor != null && !anchor.Owned)
                {
                    region.WorldCenter = Origin.World(anchor.Root.transform.position);
                    if (anchor.Body != null) region.Velocity = anchor.Body.velocity;
                    Vector3 motion = region.Velocity; motion.y = 0;
                    if (motion.sqrMagnitude > 1)
                    {
                        float heading = Mathf.Atan2(-motion.x,-motion.z)*Mathf.Rad2Deg;
                        if (Mathf.Abs(Mathf.DeltaAngle(region.Heading,heading)) > .1f && _visuals != null) _visuals.ClearGround();
                        region.Heading = heading;
                    }
                }
                else
                {
                    region.WorldCenter += region.Velocity * dt;
                    if (anchor != null) anchor.Root.transform.position = Origin.Scene(region.WorldCenter);
                }
                _regions[i] = region; _ages[i] += dt;
                if (anchor != null) anchor.Age = _ages[i];
                if (region.Hurricane && _ages[i] >= 20 && !_criticalLogged[i])
                {
                    _criticalLogged[i] = true;
                    Log.Always("[Weather] Preview entering critical phase: same-color columns forming inside the dust wall over 20 seconds.");
                }
                if (anchor != null && !anchor.Owned) { _ages[i] = anchor.Age = 40; continue; }
                if (_ages[i] >= 600) RemoveRegion(i);
            }
            if (_count == 0) { Clear(); return; }
            _audio.Tick(_regions,_ages,_count,GameRefs.MainCamera,_clock);
            if (_visuals == null) return;
            if (!GameRefs.InPlay) { _visuals.Suspend(); return; }
            var camera = GameRefs.MainCamera;
            if (camera == null || !camera.isActiveAndEnabled) { _visuals.Suspend(); return; }
            var policy = Services.WeatherVisualPolicy;
            var detail = policy != null ? policy.WeatherDetail : WeatherVisualDetail.Balanced;
            _visuals.Begin(_clock, camera);
            for (int i = 0; i < _count; i++)
            {
                float life = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_ages[i] / 6f)) * Mathf.SmoothStep(0, 1, Mathf.Clamp01((600 - _ages[i]) / 20f));
                float critical = _regions[i].Hurricane ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((_ages[i] - 20) / 20f)) : 0;
                _visuals.Draw(_regions[i], camera, detail, life, critical, _columns, _columnCount, i);
            }
            _visuals.End(camera, detail);
        }

        private void DrawTools(ToolLayout l)
        {
            WeatherPalette.Draw(l);
            l.Text("Native encounters are replaced by moving WW storms. Critical storms lift vehicles and apply vanilla damage in hurricane sand walls, with safe eyes and War Rig shelter protection. Manual critical storms form over 20-40 gameplay seconds and last ten minutes; manual storms are not saved. Natural storms retain native encounter/save bookkeeping. Up to four nearest storms are presented at once; manual previews reserve their slots.");
            l.Value("Active storms", _count.ToString(CultureInfo.InvariantCulture) + " / 4");
            l.Toggle(_mapAreas, "Storm areas on the map");
            l.Entry(_density); l.Entry(_width); l.Entry(_depth);
            UpdateLayout(); l.Value("Hurricanes per critical storm", _columnCount.ToString(CultureInfo.InvariantCulture));
            bool available = _running && GameRefs.InPlay;
            if (l.Button("Dust storm ahead", available)) ToolWindow.CloseThen(SpawnDust);
            if (l.Button("Critical dust storm preview ahead", available)) ToolWindow.CloseThen(SpawnHurricane);
            if (l.Button("Dust storm around player", available)) ToolWindow.CloseThen(SpawnHere);
            if (l.Button("Clear weather previews", _running && _count > 0)) Clear();
            WeatherDiagnostics.Controls(l);
            if (WeatherDiagnostics.Enabled)
            {
                if (l.Button("Capture snapshot and freeze",_count > 0)) CaptureDiagnostic();
                if (l.Button("Restore diagnostic snapshot",_running && GameRefs.InPlay)) RestoreDiagnostic();
                if (l.Button("Select hurricane again (nearest in first region)",_count > 0))
                { WeatherDiagnostics.SelectedRegion = WeatherDiagnostics.SelectedColumn = -1; WeatherDiagnostics.Changed(); }
            }
        }
        private void CaptureDiagnostic()
        {
            try
            {
                WeatherDiagnostics.Freeze = true; WeatherDiagnostics.Changed();
                var camera = GameRefs.MainCamera;
                var data = new WeatherDiagnostics.Snapshot { Clock = _clock, Width = _width.Value, Depth = _depth.Value, Density = _density.Value,
                    Slot = SaveBus.Slot, WorldSeed = WeatherDiagnostics.WorldSeed(), CapturedUtc = System.DateTime.UtcNow.ToString("o"),
                    SelectedRegion = WeatherDiagnostics.SelectedRegion, SelectedColumn = WeatherDiagnostics.SelectedColumn,
                    ResolutionX = Screen.width, ResolutionY = Screen.height, CameraName = camera.name, Fov = camera.fieldOfView,
                    CameraWorld = Origin.World(camera.worldToCameraMatrix.inverse.MultiplyPoint3x4(Vector3.zero)), CameraForward = camera.transform.forward,
                    Projection = camera.projectionMatrix, View = camera.worldToCameraMatrix,
                    Policy = Services.WeatherVisualPolicy != null ? Services.WeatherVisualPolicy.WeatherDetail.ToString() : "Balanced",
                    Regions = new WeatherRegion[_count], Ages = new float[_count] };
                System.Array.Copy(_regions,data.Regions,_count); System.Array.Copy(_ages,data.Ages,_count);
                _visuals.CaptureDiagnostic(data); WeatherDiagnostics.Write(data);
            }
            catch (System.Exception error) { Log.Error("[WeatherDiag] Snapshot capture failed",error); }
        }
        private void RestoreDiagnostic()
        {
            try
            {
                var data = WeatherDiagnostics.Read();
                if (data == null || data.Version != 1 || data.Slot != SaveBus.Slot || data.WorldSeed != WeatherDiagnostics.WorldSeed() ||
                    data.Width != _width.Value || data.Depth != _depth.Value || data.Density != _density.Value ||
                    data.Regions == null || data.Ages == null || data.Regions.Length == 0 || data.Regions.Length > 4 || data.Ages.Length != data.Regions.Length ||
                    data.SelectedRegion < -1 || data.SelectedRegion >= data.Regions.Length || data.SelectedColumn < -1 || data.SelectedColumn >= 80)
                { Log.Warn("[WeatherDiag] Snapshot rejected: use the same save, world seed and Weather width/depth/density."); return; }
                string policy = Services.WeatherVisualPolicy != null ? Services.WeatherVisualPolicy.WeatherDetail.ToString() : "Balanced";
                if (data.Policy != policy || data.ResolutionX != Screen.width || data.ResolutionY != Screen.height)
                    Log.Warn("[WeatherDiag] Snapshot graphics settings differ: captured "+data.Policy+" at "+data.ResolutionX+"x"+data.ResolutionY+"; restore those manually before comparing.");
                if (_visuals == null) _visuals = new WeatherVisuals(_audio);
                if (!_visuals.Ready) return;
                Clear(); _audio.Stop(); UpdateLayout(); _clock = data.Clock; _count = data.Regions.Length;
                System.Array.Copy(data.Regions,_regions,_count); System.Array.Copy(data.Ages,_ages,_count);
                for (int i = 0; i < _count; i++) _criticalLogged[i] = _ages[i] >= 20;
                WeatherDiagnostics.Freeze = true; WeatherDiagnostics.SelectedRegion = data.SelectedRegion; WeatherDiagnostics.SelectedColumn = data.SelectedColumn;
                _visuals.RestoreDiagnostic(data); _draw.Enabled = true; WeatherDiagnostics.Changed();
                Log.Always("[WeatherDiag] Restored storm/particle snapshot; recorded camera world="+data.CameraWorld.ToString("F3")+"; camera/player were not moved. Native sky and shader time were not restored.");
            }
            catch (System.Exception error) { Log.Error("[WeatherDiag] Snapshot restore failed",error); }
        }
        private void SpawnDust() { Spawn(false, false); }
        private void SpawnHurricane() { Spawn(true, false); }
        private void SpawnHere() { Spawn(false, true); }
        private void Clear()
        {
            bool hadPreviews = _count > 0;
            for (int i=0;i<_count;i++) { if (_native!=null) _native.Remove(_anchors[i]); _anchors[i]=null; }
            if (_hazards!=null) _hazards.Reset();
            _count = 0; _clock = 0;
            WeatherDiagnostics.Reset();
            if (_audio != null) _audio.Stop();
            if (_visuals != null) { _visuals.Suspend(); _visuals.ClearGround(); }
            if (_draw != null) _draw.Enabled = false;
            if (hadPreviews) Log.Always("[Weather] Cleared visual previews.");
        }
        public override IEnumerable<ToolPage> ToolPages()
        {
            yield return new ActionPage(this, ToolGroups.Debug, "Weather visual preview", DrawTools) { IsTool = true };
        }
        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Clear(); if (_native!=null) _native.Reset(); }
        public override void OnLoadBegin(string slot) { Clear(); if (_native!=null) _native.Reset(); }
        public override void OnWorldReady() { if (_native!=null) { _native.Scan(); _native.Save(false); } }
        public override void OnSaveBegin(string slot) { if (_native!=null) _native.Save(true); }
        public override void OnSaveEnd(string slot) { if (_native!=null) _native.Save(false); }
        public override void Shutdown()
        {
            if (_network != null) _network.Dispose(); _network = null;
            _running = false; Clear();
            if (_native!=null) _native.Dispose(); _native=null; _hazards=null;
            if (ReferenceEquals(Services.Weather, this)) Services.Weather = null;
            if (_visuals != null) _visuals.Dispose(); _visuals = null; _draw = null;
            if (_audio != null) _audio.Dispose(); _audio = null;
        }
    }
}
