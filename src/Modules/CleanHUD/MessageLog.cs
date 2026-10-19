using System;
using System.Collections.Generic;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using UnityEngine.UI;

namespace WildWasteland.Modules.CleanHUD
{
    // Timed on-screen messages ("FULL", "Ammo Full", "Armor Full", "Bodyarmor Needed", ...) are FSM states that
    // UiTextSetText a text on the HUD canvas, Wait, and reset it on exit. While CleanHUD is on, such a message is
    // taken off the game's text (restored to what it showed before) and sent to the log at the top left instead.
    internal static class MessageHook
    {
        internal static readonly List<string> Pending = new List<string>();
        internal static bool Active;   // set by the HUD while it is attached and visible

        private static readonly Dictionary<FsmState, bool> Timed = new Dictionary<FsmState, bool>();
        internal static void Clear() { Active = false; Pending.Clear(); Timed.Clear(); }
        internal static bool MultiplayerNotice(string message)
        {
            if (!CleanHudModule.Running || !CleanHudModule.Enabled.Value) return false;
            Push(message); return true;
        }

        internal static void Postfix(UiTextSetText __instance, Text ___uiText, string ___originalString)
        {
            try
            {
                if (!Active || ___uiText == null || __instance.everyFrame) return;
                var msg = __instance.text != null ? __instance.text.Value : null;
                if (string.IsNullOrEmpty(msg) || msg.Trim().Length == 0) return;
                if (!IsTimed(__instance.State)) return;
                var fsm = __instance.Fsm;
                var fn = fsm != null ? fsm.Name : "";
                if (fn == "DayTime" || fn == "DistanceTravelled" || fn.StartsWith("FactionRelation", StringComparison.Ordinal)) return; // periodic counters, not messages
                var canvas = ___uiText.canvas != null ? ___uiText.canvas.rootCanvas : null;
                if (canvas == null || canvas.name != "Canvas") return;   // HUD canvas only, never menus
                ___uiText.text = ___originalString ?? "";
                Push(msg);
            }
            catch { }
        }

        private static bool IsTimed(FsmState state)
        {
            if (state == null) return false;
            bool result;
            if (Timed.TryGetValue(state, out result)) return result;
            result = CheckTimed(state); Timed[state] = result; return result;
        }

        private static bool CheckTimed(FsmState state)
        {
            // a timed message state: has a Wait and no input / pick polling (those are interaction prompts such as "Sleep: (Hold F)")
            if (state == null || state.Actions == null) return false;
            bool wait = false;
            foreach (var a in state.Actions)
            {
                if (a == null) continue;
                if (a is Wait || a is RandomWait) wait = true;
                var n = a.GetType().Name;
                if (n.StartsWith("GetButton", StringComparison.Ordinal) || n.StartsWith("GetKey", StringComparison.Ordinal) || n.StartsWith("GetMouseButton", StringComparison.Ordinal) || n.StartsWith("MousePick", StringComparison.Ordinal)) return false;
            }
            return wait;
        }

        internal static void Push(string msg)
        {
            msg = msg.Replace("\r", " ").Replace("\n", " ").Trim();
            if (msg.Length == 0) return;
            if (Pending.Count > 32) Pending.RemoveAt(0);
            Pending.Add(msg);
        }
    }

    // Top-left message log: newest line at the bottom; every 3 s the oldest line fades out and the rest slide up.
    internal sealed class MessageLog
    {
        private const float LineH = 32f, Life = 3f, Width = 640f;
        private const int MaxLines = 8;
        private sealed class Line { internal RectTransform Rt; internal Text Txt; internal CanvasGroup G; internal string Msg; internal int Count = 1; internal float Y, A; internal bool Dying; }

        private readonly RectTransform _root;
        private readonly Font _font;
        private readonly Color _gold, _white;
        private readonly List<Line> _lines = new List<Line>();
        private readonly Stack<Line> _pool = new Stack<Line>();
        private Line _autosave;
        private float _nextDrop;

        internal static Vector2 Size { get { return new Vector2(Width, LineH * (MaxLines + 1)); } }

        internal MessageLog(RectTransform root, Font font, Color gold, Color white) { _root = root; _font = font; _gold = gold; _white = white; }

        private Line Make()
        {
            if (_pool.Count > 0) { var p = _pool.Pop(); p.Rt.gameObject.SetActive(true); return p; }
            var go = new GameObject("line", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.sizeDelta = new Vector2(Width, LineH);
            var l = new Line { Rt = rt, G = go.AddComponent<CanvasGroup>() };
            l.G.blocksRaycasts = false; l.G.interactable = false;
            var bar = new GameObject("bar", typeof(RectTransform)).AddAccent<Image>();
            bar.transform.SetParent(rt, false);
            var br = bar.rectTransform; br.anchorMin = br.anchorMax = new Vector2(0, 1); br.pivot = new Vector2(0, 1);
            br.anchoredPosition = new Vector2(0, -6); br.sizeDelta = new Vector2(6, 20); bar.color = _gold; bar.raycastTarget = false;
            var t = new GameObject("text", typeof(RectTransform)).AddAccent<Text>();
            t.transform.SetParent(rt, false);
            var tr = t.rectTransform; tr.anchorMin = tr.anchorMax = new Vector2(0, 1); tr.pivot = new Vector2(0, 1);
            tr.anchoredPosition = new Vector2(16, 0); tr.sizeDelta = new Vector2(Width - 16, LineH);
            t.font = _font; t.fontSize = 22; t.alignment = TextAnchor.MiddleLeft; t.color = _white; t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, .95f); s.effectDistance = new Vector2(0, -1.5f);
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, .45f); o.effectDistance = new Vector2(1, 1);
            l.Txt = t;
            return l;
        }

        internal void Add(string msg)
        {
            float now = Time.unscaledTime;
            var last = _lines.Count > 0 ? _lines[_lines.Count - 1] : null;
            if (last != null && !last.Dying && last.Msg == msg)
            {
                last.Count++; last.Txt.text = msg + "  x" + last.Count; last.A = .35f;   // brief flash on repeat
                return;
            }
            int alive = 0; foreach (var x in _lines) if (!x.Dying) alive++;
            if (alive == 0) _nextDrop = now + Life;
            var l = Make();
            l.Msg = msg; l.Count = 1; l.Dying = false; l.Txt.text = msg; l.A = 0f;
            l.Y = _lines.Count * LineH;
            l.Rt.anchoredPosition = new Vector2(0, -l.Y); l.G.alpha = 0f;
            _lines.Add(l);
            if (alive + 1 > MaxLines) foreach (var x in _lines) if (!x.Dying) { x.Dying = true; break; }
        }

        internal void Tick()
        {
            string warning = Core.HostSaveWarning.Text;
            while (MessageHook.Pending.Count > 0) { var m = MessageHook.Pending[0]; MessageHook.Pending.RemoveAt(0); Add(m); }
            float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (now >= _nextDrop)
            {
                foreach (var x in _lines) if (!x.Dying) { x.Dying = true; break; }
                _nextDrop = now + Life;
            }
            for (int i = 0; i < _lines.Count; i++)
            {
                var l = _lines[i];
                if (l.Dying)
                {
                    l.A -= dt * 4f;
                    if (l.A <= 0f) { l.Rt.gameObject.SetActive(false); _pool.Push(l); _lines.RemoveAt(i); i--; continue; }
                }
                else l.A = Mathf.Min(1f, l.A + dt * 6f);
                float alpha = Mathf.Clamp01(l.A); if (l.G.alpha != alpha) l.G.alpha = alpha;
            }
            for (int i = 0; i < _lines.Count; i++)
            {
                var l = _lines[i];
                float target = (i + (warning == null ? 0 : 1)) * LineH;
                l.Y = Mathf.Abs(l.Y - target) < .5f ? target : Mathf.Lerp(l.Y, target, 1f - Mathf.Exp(-dt * 14f));
                var position = new Vector2(0, -l.Y); if (l.Rt.anchoredPosition != position) l.Rt.anchoredPosition = position;
            }
            // Keep the server countdown in one transparent log row instead of adding a line every second.
            if (warning == null)
            {
                if (_autosave != null) { _autosave.Rt.gameObject.SetActive(false); _pool.Push(_autosave); _autosave = null; }
            }
            else
            {
                if (_autosave == null) { _autosave = Make(); _autosave.Msg = null; _autosave.G.alpha = 1f; }
                if (_autosave.Msg != warning) { _autosave.Msg = warning; _autosave.Txt.text = warning; }
                var position = Vector2.zero;
                if (_autosave.Rt.anchoredPosition != position) _autosave.Rt.anchoredPosition = position;
            }
        }
    }
}
