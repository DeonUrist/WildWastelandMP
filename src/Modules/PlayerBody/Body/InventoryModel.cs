using UnityEngine;
using UnityEngine.UI;

namespace WildWasteland.Modules.PlayerBody
{
    // Settings and TAB share editable RGBA PNGs. Native DXT1 has no alpha, and a GPU-only backup loses its pixels when
    // Low's masterTextureLimit changes. Bind the RawImages instead; CleanHUD mirrors their texture references.
    internal static class InventoryModel
    {
        private const string GameTexture = "player_character_2_UI";
        private static Texture2D _game;
        private static readonly System.Collections.Generic.Dictionary<RawImage, Rect> _switched = new System.Collections.Generic.Dictionary<RawImage, Rect>();
        private static bool _applied;
        private static float _nextTry;
        private static int _tries;

        internal static void PrepareSettingsPictures()
        {
            CharacterPortraits.Prepare();
        }

        // Runner calls this every frame, but it only works until the swap is done for the loaded scene (a cheap bool check after that)
        public static void LateTick()
        {
            // (WW) module on/off is handled by PlayerBodyModule (Init resets, Shutdown calls Off; ReplaceDriver changes call Off / Reset)
            if (_applied || !PlayerBodyModule.Running || !PlayerBodyModule.ReplaceDriver.Value) return;
            if (Time.unscaledTime < _nextTry || _tries >= 15) return;   // the texture may load a moment after the scene: retry for ~30 s
            _nextTry = Time.unscaledTime + 2f; _tries++;
            Apply();
        }

        private static void Apply()
        {
            if (_game == null)
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                    if (t != null && t.name == GameTexture) { _game = t; break; }
            if (_game == null) return;
            PrepareSettingsPictures();
            var mine = CharacterPortraits.Picture((int)PlayerBodyModule.Character.Value);
            if (mine == null) return;
            int n = 0;
            foreach (var r in Resources.FindObjectsOfTypeAll<RawImage>())
                if (r != null && r.gameObject.scene.IsValid() && r.texture == _game) { _switched.Add(r, r.uvRect); r.texture = mine; r.uvRect = new Rect(0, 0, 1, 1); n++; }
            _applied = n > 0;
            if (_applied) PlayerBodyModule.Info("TAB screen picture: " + n + " UI element(s) switched to " + PlayerBodyModule.Character.Value + " PNG");
        }
        internal static void RefreshPortrait()
        {
            var picture = CharacterPortraits.Picture((int)PlayerBodyModule.Character.Value);
            if (picture != null) foreach (var image in _switched.Keys) if (image != null) image.texture = picture;
        }

        public static void Off()
        {
            foreach (var pair in _switched) if (pair.Key != null && _game != null) { pair.Key.texture = _game; pair.Key.uvRect = pair.Value; }
            _switched.Clear();
            _applied = false; _tries = 0; _nextTry = 0f;
        }

        // Character switched: restore native references, then bind the other PNG on the next tick.
        public static void CharacterChanged()
        {
            Off();
        }

        // New scene: the native texture/UI may be new instances; the shared PNGs remain cached.
        public static void Reset()
        {
            Off(); _game = null;
        }
    }
}
