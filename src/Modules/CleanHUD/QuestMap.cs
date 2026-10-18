using System;
using System.Collections.Generic;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.UI;
using WildWasteland.Core;

namespace WildWasteland.Modules.CleanHUD
{
    internal sealed class QuestMap : IMapMarkers, IDisposable
    {
        private sealed class Entry
        {
            internal int Id; internal string Owner, Icon; internal Color Color; internal Func<Vector3> Position;
            internal RectTransform Root; internal RawImage Image; internal Texture IconTexture;
        }
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<int> _ids = new HashSet<int>();
        private int _next;
        private PlayMakerFSM _fsm;
        private Camera _camera;
        private RectTransform _map, _root;
        private bool _captured, _nativeEnabled, _cameraNative, _fsmOn;
        private Camera _owned;
        private float _nextLookup;
        private readonly List<RawImage> _consumers = new List<RawImage>();
        private Renderer _nativePlayer; private GameObject _playerFor; private bool _nativePlayerOff, _playerShown;
        private readonly Vector3[] _playerCorners = new Vector3[4];
        internal bool PlayerShown() { return _playerShown; }
        internal void DrawPlayer()
        {
            if (Event.current.type != EventType.Repaint || !_playerShown || _map == null || _camera == null || GameRefs.Player == null) return;
            bool multiplayer = Multiplayer.WorldReady;
            if (multiplayer && !CleanHudModule.ShowPlayersOnMap.Value) return;
            _map.GetWorldCorners(_playerCorners);
            var rect = new Rect(_playerCorners[0].x, Screen.height - _playerCorners[1].y, _playerCorners[2].x - _playerCorners[0].x, _playerCorners[1].y - _playerCorners[0].y);
            if (multiplayer)
                foreach (var remote in MultiplayerCompass.MapPlayers)
                {
                    if (remote.Peer == Multiplayer.LocalPeer) continue;
                    Vector3 position; float yaw;
                    if (MultiplayerSleepers.JoiningPosition(remote.Peer, out position, out yaw)) DrawArrow(rect, position, Quaternion.Euler(0, yaw, 0) * Vector3.forward, MultiplayerNames.ColorOf(remote.Peer));
                    else
                    {
                        var positionNow = Origin.Scene(remote.World); var forward = Vector3.forward;
                        foreach (var tag in MultiplayerNames.Players) if (tag.Peer == remote.Peer && tag.Tag != null && tag.Tag.transform.parent != null) { forward = tag.Tag.transform.parent.forward; break; }
                        DrawArrow(rect, positionNow, forward, MultiplayerNames.ColorOf(remote.Peer));
                    }
                }
            var player = GameRefs.Player.transform;
            Vector3 selfPosition = player.position; float selfYaw = player.eulerAngles.y;
            bool joining = multiplayer && MultiplayerSleepers.JoiningPosition(Multiplayer.LocalPeer, out selfPosition, out selfYaw);
            if (!joining) { selfPosition = player.position; selfYaw = player.eulerAngles.y; }
            DrawArrow(rect, selfPosition, Quaternion.Euler(0, selfYaw, 0) * Vector3.forward, multiplayer ? MultiplayerNames.ColorOf(Multiplayer.LocalPeer) : new Color(.2f, 1f, .25f, 1f));
        }
        private void DrawArrow(Rect rect, Vector3 position, Vector3 forward, Color tint)
        {
            var p = _camera.WorldToViewportPoint(position);
            if (p.z <= 0 || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) return;
            var ahead = _camera.WorldToViewportPoint(position + forward * 10f) - p;
            var center = new Vector2(p.x * rect.width, (1f - p.y) * rect.height);
            var matrix = GUI.matrix; var color = GUI.color; int depth = GUI.depth;
            GUI.depth = -1; GUI.BeginGroup(rect);
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(ahead.x, ahead.y) * Mathf.Rad2Deg, center);
                var icon = MapIcons.Get("player");
                GUI.color = new Color(.02f, .08f, .02f, 1f); GUI.DrawTexture(new Rect(center.x - 11, center.y - 12.5f, 22, 25), icon);
                GUI.color = tint; GUI.DrawTexture(new Rect(center.x - 9, center.y - 10.5f, 18, 21), icon);
            }
            finally { GUI.matrix = matrix; GUI.EndGroup(); GUI.color = color; GUI.depth = depth; }
        }
        public Texture2D GetIcon(string key) { return CleanHudModule.Enabled.Value ? MapIcons.Get(key) : null; }
        public MapDetail Markers { get { return CleanHudModule.MapMarkers.Value; } }
        public MapDetail Routes { get { return CleanHudModule.MapRoutes.Value; } }
        public int Add(string owner, Func<Vector3> position, string icon, Color color, string label)
        { var e = new Entry { Id = ++_next, Owner = owner, Position = position, Icon = icon, Color = color }; _entries.Add(e); _ids.Add(e.Id); return e.Id; }
        public bool Exists(int id) { return _ids.Contains(id); }
        public bool Hidden(Vector3 scene) { return MapFog.Hides(scene); }
        public bool Explored(Vector3 scene) { return MapFog.Explored(scene); }
        public bool Open { get { return _open; } }
        internal bool TryWeatherView(out Camera camera, out RectTransform map)
        { camera = _camera; map = _map; return _open && camera != null && map != null && CleanHudModule.Enabled.Value && GameRefs.InPlay && !Core.UI.ToolWindow.IsOpen; }
        public MapDetail TrafficMarkers { get { return CleanHudModule.MapTrafficMarkers.Value; } }
        public float Zoom { get { return _zoom; } }
        public bool Observe(Transform target, string name, Color color) { return Binoculars.Observe(target, name, color) || VantageDiscovery.Sees(target); }
        public void Remove(int id)
        { for (int i = _entries.Count - 1; i >= 0; i--) if (_entries[i].Id == id) { Destroy(_entries[i]); _ids.Remove(id); _entries.RemoveAt(i); } }
        public void RemoveAll(string owner)
        { for (int i = _entries.Count - 1; i >= 0; i--) if (_entries[i].Owner == owner) { Destroy(_entries[i]); _ids.Remove(_entries[i].Id); _entries.RemoveAt(i); } }
        private static void Destroy(Entry e) { if (e.Root != null) UnityEngine.Object.Destroy(e.Root.gameObject); e.Root = null; }
        internal void FindMap() { FindMap(false); }
        // GameObject.Find and the FSM action walk back off to 5 s after a miss; opening the map forces a lookup.
        private void FindMap(bool force)
        {
            if (_fsm == null) _fsm = FsmUtil.Find(GameRefs.GameManager, "MapCheat");
            if (_fsm != null && !_captured) { _captured = true; _nativeEnabled = _fsm.enabled; force = true; }
            ApplyEnabled();
            if ((_map == null || _camera == null) && _fsm != null && (force || Time.unscaledTime >= _nextLookup))
            {
                foreach (var state in _fsm.FsmStates) foreach (var action in state.Actions)
                {
                    var activate = action as ActivateGameObject; if (activate == null) continue;
                    var go = _fsm.Fsm.GetOwnerDefaultTarget(activate.gameObject); if (go == null) continue;
                    // MapCheat_Camera may sit under an inactive object while the map is closed; GameObject.Find misses it then.
                    if (_camera == null) foreach (var c in go.GetComponentsInChildren<Camera>(true)) if (c.name == "MapCheat_Camera") { _camera = c; break; }
                    if (_map == null && go.name == "MapCheatUI")
                        foreach (var t in go.GetComponentsInChildren<RectTransform>(true)) if (t.name == "RawImage_Map") { _map = t; break; }
                }
                if (_camera == null)
                {
                    var go = GameObject.Find("MapCheat_Camera"); if (go != null) _camera = go.GetComponent<Camera>();
                }
                if (_map == null || _camera == null) _nextLookup = Time.unscaledTime + 5f;
            }
            if (_map != null && _root == null)
            {
                _root = DialogueWidgets.Rect(_map, "CleanHUD.QuestMarkers", 0, 0, 0, 0);
                _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one; _root.offsetMin = _root.offsetMax = Vector2.zero;
                _root.gameObject.AddComponent<RectMask2D>();
            }
            MapFrame.Sync(_map);
        }
        // The native map camera renders a 1024x1024 target every frame even while the map is closed: it only renders while
        // the map is shown (or while another UI shows its target). Projection helpers keep working on a disabled camera.
        private void DriveCamera(bool shown)
        {
            if (!ReferenceEquals(_camera, _owned))
            {
                RestoreCamera();
                if (_camera == null) return;
                _owned = _camera; _cameraNative = _camera.enabled; FindConsumers();
            }
            if (_owned == null) return;
            bool want = shown;
            for (int i = 0; i < _consumers.Count && !want; i++) want = _consumers[i] != null && _consumers[i].isActiveAndEnabled;
            if (_owned.enabled == want) return;
            _owned.enabled = want;
            if (want && _owned.gameObject.activeInHierarchy) _owned.Render();
        }
        private void FindConsumers()
        {
            _consumers.Clear();
            var target = _owned.targetTexture; if (target == null) return;
            foreach (var image in UnityEngine.Object.FindObjectsOfType<RawImage>(true))
                if (image.texture == target && image.name != "RawImage_Map") _consumers.Add(image);
            if (_consumers.Count > 0) CleanHudModule.Log("Map camera target is also shown by " + _consumers.Count + " other image(s); the camera renders while any is visible");
        }
        // Mouse wheel zooms the native map in (z 1 = native view .. MinZoom), never out. The native size/FOV is captured per camera
        // instance; a value the game writes itself becomes the new native. Markers, fog and Patrols' ink project through the camera.
        private const float MinZoom = .667f, ZoomStep = .05f;
        private bool _open, _zoomOrtho;
        private float _zoom = 1f, _zoomTarget = 1f, _zoomNative, _zoomWritten;
        private Camera _zoomCam;
        private void DriveZoom(bool active, bool play)
        {
            if (!ReferenceEquals(_camera, _zoomCam) || (active && _zoomCam != null && _zoomCam.orthographic != _zoomOrtho)) RestoreZoom();
            if (!active || _camera == null) { if (_zoom != 1f || _zoomTarget != 1f) RestoreZoom(); return; }
            if (_zoomCam == null)
            {
                _zoomCam = _camera; _zoomOrtho = _camera.orthographic;
                _zoomNative = _zoomWritten = _zoomOrtho ? _camera.orthographicSize : _camera.fieldOfView;
            }
            float current = _zoomOrtho ? _zoomCam.orthographicSize : _zoomCam.fieldOfView;
            if (Mathf.Abs(current - _zoomWritten) > 1e-4f) _zoomNative = current;
            float wheel = 0f;
            if (play && !ApocasetterBridge.InputBlocked && !Core.UI.ToolWindow.IsOpen && Application.isFocused) try { wheel = Input.mouseScrollDelta.y; } catch (Exception) { }
            if (wheel != 0f) _zoomTarget = Mathf.Clamp(_zoomTarget - wheel * ZoomStep, MinZoom, 1f);
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
            if (Mathf.Abs(_zoom - _zoomTarget) < .0005f) _zoom = _zoomTarget;
            float value = _zoomOrtho ? _zoomNative * _zoom : 2f * Mathf.Atan(Mathf.Tan(_zoomNative * .5f * Mathf.Deg2Rad) * _zoom) * Mathf.Rad2Deg;
            if (value != current) { if (_zoomOrtho) _zoomCam.orthographicSize = value; else _zoomCam.fieldOfView = value; }
            _zoomWritten = value;
        }
        private void RestoreZoom()
        {
            if (_zoomCam != null && _zoom != 1f)
            {
                float current = _zoomOrtho ? _zoomCam.orthographicSize : _zoomCam.fieldOfView;
                if (Mathf.Abs(current - _zoomWritten) <= 1e-4f) { if (_zoomOrtho) _zoomCam.orthographicSize = _zoomNative; else _zoomCam.fieldOfView = _zoomNative; }
            }
            _zoomCam = null; _zoom = _zoomTarget = 1f;
        }
        private void RestoreCamera()
        {
            RestoreZoom(); _open = false;
            if (_owned != null && _owned.enabled != _cameraNative) _owned.enabled = _cameraNative;
            _owned = null; _consumers.Clear();
        }
        internal void Tick()
        {
            bool fsmOn = _fsm != null && _fsm.isActiveAndEnabled && _fsm.ActiveStateName == "on";
            if (fsmOn && !_fsmOn && (_map == null || _camera == null)) FindMap(true);
            _fsmOn = fsmOn;
            bool mapShown = fsmOn && (_map == null || _map.gameObject.activeInHierarchy);
            DriveCamera(mapShown);
            bool play = GameRefs.Menu != null && GameRefs.Menu.ActiveStateName == "play";
            _open = mapShown;
            DriveZoom(mapShown, play);   // the zoom survives a pause; it resets when the map closes
            _playerShown = CleanHudModule.Enabled.Value && CleanHudModule.MapEnabled.Value && play && mapShown && _map != null && _camera != null && !Core.UI.ToolWindow.IsOpen;
            if (_playerFor != GameRefs.Player)
            {
                if (_nativePlayer != null) _nativePlayer.forceRenderingOff = _nativePlayerOff;
                _playerFor = GameRefs.Player; var icon = _playerFor != null ? _playerFor.transform.Find("mapPlayerIcon") : null;
                _nativePlayer = icon != null ? icon.GetComponent<Renderer>() : null;
                _nativePlayerOff = _nativePlayer != null && _nativePlayer.forceRenderingOff;
            }
            if (_nativePlayer != null) _nativePlayer.forceRenderingOff = _nativePlayerOff || _playerShown;
            MapFog.Tick(_map, _camera, CleanHudModule.MapEnabled.Value && play && mapShown && _map != null);
            if (_root == null) return;
            bool open = CleanHudModule.Enabled.Value && CleanHudModule.MapEnabled.Value && Markers != MapDetail.Off && play
                && mapShown && _camera != null;
            if (_root.gameObject.activeSelf != open) _root.gameObject.SetActive(open);
            if (!open) return;
            foreach (var e in _entries)
            {
                if (e.Root == null)
                {
                    e.Root = DialogueWidgets.Rect(_root, "quest", 0, 0, 28, 28);
                    e.Image = e.Root.gameObject.AddComponent<RawImage>(); e.Image.raycastTarget = false;
                    e.IconTexture = MapIcons.Get(e.Icon) ?? Assets.Tex("g_" + e.Icon);
                }
                bool ambiguous = Markers == MapDetail.Ambiguous && !(e.Owner == LocationMarkers.Owner && LocationMarkers.Known(e.Position));
                e.Image.texture = ambiguous ? MapIcons.Get("unknown") : e.IconTexture;
                e.Image.color = ambiguous ? Color.white : e.Color;
                if (e.Owner == LocationMarkers.Owner && LocationMarkers.MissingBake(e.Position)) e.Image.color = new Color(.78f, .62f, 1f, 1f);
                var scene = e.Position(); var p = _camera.WorldToViewportPoint(scene);
                bool visible = p.z > 0 && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1 && !(e.Owner == LocationMarkers.Owner && MapFog.Hides(scene));
                if (e.Root.gameObject.activeSelf != visible) e.Root.gameObject.SetActive(visible);
                if (visible) e.Root.anchoredPosition = new Vector2(p.x * _map.rect.width - 14, -(1 - p.y) * _map.rect.height + 14);
            }
        }
        internal void ApplyEnabled()
        {
            if (_fsm == null || !_captured) return;
            bool enabled = CleanHudModule.MapEnabled.Value;
            if (!enabled && _fsm.enabled && _fsm.ActiveStateName == "on") _fsm.SendEvent("back");
            if (_fsm.enabled != enabled) _fsm.enabled = enabled;
        }
        internal void SceneLoaded()
        {
            if (_nativePlayer != null) _nativePlayer.forceRenderingOff = _nativePlayerOff;
            _nativePlayer = null; _playerFor = null; _playerShown = false;
            RestoreNative(); RestoreCamera(); _fsmOn = false; _nextLookup = 0; MapFrame.Restore(); MapFog.SceneLoaded();
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            foreach (var e in _entries) e.Root = null;
            _root = _map = null; _camera = null; _fsm = null;
        }
        private void RestoreNative()
        {
            if (_captured && _fsm != null)
            {
                if (!_nativeEnabled && _fsm.enabled && _fsm.ActiveStateName == "on") _fsm.SendEvent("back");
                _fsm.enabled = _nativeEnabled;
            }
            _captured = false;
        }
        public void Dispose() { SceneLoaded(); _entries.Clear(); _ids.Clear(); }
    }
}
