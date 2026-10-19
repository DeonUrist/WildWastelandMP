using System.IO;
using UnityEngine;
using CleanHud = WildWasteland.Modules.CleanHUD;

namespace WildWasteland.Core
{
    internal static class HostSaveWarning
    {
        const byte Packet = 168;
        static byte _phase;
        static int _seconds;
        static float _expires, _repeat;
        static string _text;
        internal static string Text
        {
            get { return (Multiplayer.WorldReady || HostSaveSystem.LocalWorld) && (!Multiplayer.Client || Time.unscaledTime <= _expires) ? _text : null; }
        }
        internal static void Install()
        {
            Multiplayer.Message += Receive;
            Multiplayer.SessionChanged += Clear;
            Scheduler.Gui(null, "Autosave log fallback", Draw, Wants);
        }
        static void Clear() { _phase = 0; _text = null; _repeat = 0; }
        // 0 hidden, 1 ticking, 2 deferred, 3 final warning. Repeat includes joining players.
        internal static void Show(byte phase, int seconds = 0)
        {
            bool changed = phase != _phase || seconds != _seconds;
            if (changed) Set(phase, seconds);
            if (Multiplayer.Hosting && (changed || phase != 0 && Time.unscaledTime >= _repeat))
            { _repeat = Time.unscaledTime + 1f; Multiplayer.Send(Packet, Write); }
        }
        static void Set(byte phase, int seconds)
        {
            _phase = phase; _seconds = seconds;
            _text = phase == 1 ? "AUTOSAVE IN " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00") : phase == 2 ? "AUTOSAVE DELAYED - WAITING FOR SAFETY" : phase == 3 ? "The game will save now" : null;
        }
        static void Write(BinaryWriter w) { w.Write(MultiplayerParts.Epoch); w.Write(_phase); w.Write((short)_seconds); }
        static void Receive(ulong peer, byte kind, BinaryReader r)
        {
            if (kind != Packet || !Multiplayer.Client || peer != 0 || r.ReadUInt64() != MultiplayerParts.Epoch) return;
            byte phase = r.ReadByte(); int seconds = r.ReadInt16();
            if (phase > 3 || seconds < 0 || seconds > 120 || r.BaseStream.Position != r.BaseStream.Length) return;
            if (phase != _phase || seconds != _seconds) Set(phase, seconds);
            _expires = Time.unscaledTime + 4f;
        }
        static bool Wants() { return Text != null && !CleanHud.MessageHook.Active; }
        static void Draw()
        {
            if (LoadingProgress.MenusBlocked || !GameRefs.InGame || UI.EditorSession.Busy || Event.current.type != EventType.Repaint) return;
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            var rect = new Rect(40 * scale, 24 * scale, 640 * scale, 32 * scale);
            int depth = GUI.depth; GUI.depth = -100;
            var color = _phase == 3 || _phase == 1 && _seconds <= 15 ? new Color(1f, .78f, .35f) : new Color(.96f, .91f, .79f);
            CleanHud.GlyphFont.DrawGame(rect, _text, 22 * scale, color, TextAnchor.MiddleLeft);
            GUI.depth = depth;
        }
    }
}
