using UnityEngine;

namespace WildWasteland.Core.UI
{
    /// <summary>
    /// One modal editor at a time for the whole mod (F7 tool window, the Patrols ledger, the template recorder, ...).
    /// Pauses the game (timeScale 0), frees the cursor and blocks game input through Apocasetter's InputBlocker when present.
    /// Release happens one frame after Close so the click that closed the window does not reach the game.
    /// </summary>
    public sealed class EditorSession
    {
        static EditorSession _owner;
        int _releaseFrame = -1;
        float _time;
        CursorLockMode _lock;
        bool _visible, _blocked;
        bool _pause;
        public bool Open { get; private set; }
        public static bool Busy { get { return _owner != null; } }
        public static EditorSession Owner { get { return _owner; } }
        public static int Generation { get; private set; }
        public static void CancelAll() { Generation++; if (_owner != null) _owner.Close(true); }

        public bool Acquire()
        { return Acquire(true); }
        public bool Acquire(bool pause)
        { return Acquire(pause, false); }
        internal bool Acquire(bool pause, bool allowPaused)
        {
            if (Busy || (!allowPaused && Time.timeScale <= 0) || ApocasetterBridge.InputBlocked) return false;
            _owner = this; Open = true; _time = Time.timeScale; _lock = Cursor.lockState; _visible = Cursor.visible;
            _pause = pause && !Multiplayer.Active;
            _blocked = ApocasetterBridge.SetInputBlock(true);
            if (_pause) Time.timeScale = 0; Maintain(); return true;
        }
        public void Maintain()
        {
            if (!Open) return;
            if (_pause) Time.timeScale = 0; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        public void Close(bool immediate = false)
        {
            if (!Open && _releaseFrame < 0) return;
            Open = false;
            if (immediate) Release(); else _releaseFrame = Time.frameCount + 1;
        }
        /// <summary>Called every frame by Core (EarlyUpdate).</summary>
        public static void Tick() { if (_owner != null && _owner._releaseFrame >= 0 && Time.frameCount >= _owner._releaseFrame) _owner.Release(); }
        void Release()
        {
            if (_blocked) ApocasetterBridge.SetInputBlock(false);
            _blocked = false; _releaseFrame = -1; if (!Multiplayer.Active) Time.timeScale = _time; Cursor.lockState = _lock; Cursor.visible = _visible; _owner = null;
        }
    }
}
