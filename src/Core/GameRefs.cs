using UnityEngine;
using UnityEngine.SceneManagement;

namespace WildWasteland.Core
{
    /// <summary>
    /// Cached references to the game's well-known objects (AGENTS.md 5.5). Looked up at most once per second while
    /// missing, cleared on scene load. Never call GameObject.Find per frame in modules; use these.
    /// </summary>
    public static class GameRefs
    {
        static GameObject _player, _camera, _gm, _slg, _arrays, _itemId;
        static PlayMakerFSM _menu, _saveLoad;
        static float _next;
        static int _inGameFrame = -1;
        static bool _inGame;

        public static GameObject Player { get { Refresh(); return _player; } }
        /// <summary>"PlayerCameraHolder" (the first-person camera rig).</summary>
        public static GameObject PlayerCameraHolder { get { Refresh(); return _camera; } }
        public static GameObject GameManager { get { Refresh(); return _gm; } }
        public static GameObject NewGOArrayList { get { Refresh(); return _arrays; } }
        public static GameObject ItemNameId { get { Refresh(); return _itemId; } }
        public static PlayMakerFSM Menu { get { Refresh(); return _menu; } }
        public static PlayMakerFSM SaveLoadGame { get { Refresh(); return _saveLoad; } }

        static Camera _cam; static int _camFrame = -1;
        /// <summary>Camera.main cached per frame.</summary>
        public static Camera MainCamera
        {
            get { if (_camFrame != Time.frameCount || _cam == null) { _camFrame = Time.frameCount; _cam = Camera.main; } return _cam; }
        }

        /// <summary>Menu "play" && SaveLoadGame "isPlay" && timeScale > 0. Evaluated once per frame.</summary>
        public static bool InGame
        {
            get
            {
                if (_inGameFrame == Time.frameCount) return _inGame;
                _inGameFrame = Time.frameCount;
                Refresh();
                _inGame = Time.timeScale > 0f && _menu != null && _menu.Fsm.Initialized && _menu.ActiveStateName == "play"
                          && (_saveLoad == null || !_saveLoad.Fsm.Initialized || _saveLoad.ActiveStateName == "isPlay");
                return _inGame;
            }
        }

        static int _inPlayFrame = -1; static bool _inPlay;
        // World simulation continues behind MP menus; InGame remains the local UI/input gate.
        public static bool WorldRunning { get { return Multiplayer.Active ? !MultiplayerSaving.Frozen && Time.timeScale > 0f && SaveLoadState == "isPlay" : InGame; } }
        /// <summary>Like InGame but ignores timeScale (true while the game is paused by an editor / the ESC menu). For tools and editors.</summary>
        public static bool InPlay
        {
            get
            {
                if (_inPlayFrame == Time.frameCount) return _inPlay;
                _inPlayFrame = Time.frameCount;
                Refresh();
                _inPlay = _menu != null && _menu.Fsm.Initialized && _menu.ActiveStateName == "play"
                          && (_saveLoad == null || !_saveLoad.Fsm.Initialized || _saveLoad.ActiveStateName == "isPlay");
                return _inPlay;
            }
        }

        public static string SaveLoadState { get { var f = SaveLoadGame; return f != null ? f.ActiveStateName : null; } }

        static void Refresh()
        {
            if (_player != null && _gm != null && _menu != null && _saveLoad != null && _camera != null && _arrays != null && _itemId != null) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            if (_player == null) _player = GameObject.Find("Player");
            if (_camera == null) _camera = GameObject.Find("PlayerCameraHolder");
            if (_gm == null) _gm = GameObject.Find("__GameManager__");
            if (_menu == null && _gm != null) _menu = FsmUtil.Find(_gm, "Menu");
            if (_slg == null) _slg = GameObject.Find("SaveLoadGame");
            if (_saveLoad == null && _slg != null) _saveLoad = FsmUtil.Find(_slg, "SaveLoadGame");
            if (_arrays == null) _arrays = GameObject.Find("NewGO_ArrayList");
            if (_itemId == null) _itemId = GameObject.Find("itemNameID");
        }

        internal static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            _player = _camera = _gm = _slg = _arrays = _itemId = null; _menu = _saveLoad = null; _next = 0; _inGameFrame = -1; _inPlayFrame = -1; _cam = null;
        }
    }

    /// <summary>FSM lookup helpers (no FindFsmOnGameObject: it logs per miss).</summary>
    public static class FsmUtil
    {
        static readonly System.Collections.Generic.List<PlayMakerFSM> _buf = new System.Collections.Generic.List<PlayMakerFSM>(8);
        public static PlayMakerFSM Find(GameObject go, string fsmName)
        {
            if (go == null) return null;
            go.GetComponents(_buf);
            for (int i = 0; i < _buf.Count; i++) if (_buf[i] != null && _buf[i].FsmName == fsmName) { var f = _buf[i]; _buf.Clear(); return f; }
            _buf.Clear();
            return null;
        }
    }
}
