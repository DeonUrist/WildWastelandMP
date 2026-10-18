using UnityEngine;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.PlayerBody
{
    internal static partial class WheelArms
    {
        private static readonly string[] Targets = { "LEFT HAND", "RIGHT HAND", "LEFT SHOULDER", "RIGHT SHOULDER", "LEFT ELBOW", "RIGHT ELBOW", "SITTING OFFSET", "TORSO ROTATION / BEND" };
        private static readonly string[] HandlebarPoses = { "STRAIGHT", "LEFT TURN", "RIGHT TURN" };
        private static bool _editor, _rotate, _dirty;
        private static int _target, _editPose, _preview = 2;
        private static float _hintNext;
        private static string _hint = "";
        private static GUIStyle _hintStyle;
        private static readonly float[] _turn = new float[2], _release = new float[2];
        private static int _releaseHand = -1, _spinDirection;
        private static float _releaseTime, _releaseDuration, _releaseStart, _releaseEnd;
        private static float[] _copiedSetup;
        private static string _copiedWheel = "";
        internal static bool EditorVisible { get { return _editor && _hint.Length != 0; } }

        private static int PoseOffset(int pose, int arm) { return 12 + pose * 24 + arm * 12; }
        private static Vector3 PoseVector(float[] v, int side, int top, float blend)
        { return Vector3.Lerp(new Vector3(v[side], v[side + 1], v[side + 2]), new Vector3(v[top], v[top + 1], v[top + 2]), blend); }
        private static Vector3 MirrorVector(Vector3 v, Quaternion frame)
        { v = Quaternion.Inverse(frame) * v; v.x = -v.x; return frame * v; }
        private static Quaternion MirrorRotation(Quaternion q, Quaternion frame)
        { q = Quaternion.Inverse(frame) * q * frame; return frame * new Quaternion(q.x, -q.y, -q.z, q.w) * Quaternion.Inverse(frame); }
        private static Quaternion MirrorOrientation(Quaternion q, Quaternion frame)
        { q = Quaternion.Inverse(frame) * q; return frame * new Quaternion(q.x, -q.y, -q.z, q.w); }
        private static int NextTarget()
        { return (_target + 1) % (_handlebars ? Targets.Length : 6); }
        private static int EditOffset
        { get { return _target == 6 ? 84 : _target == 7 ? 87 + _editPose * 3 : PoseOffset(_editPose, _target & 1) + (_target / 2 == 1 ? 6 : _target / 2 == 2 ? 9 : _rotate ? 3 : 0); } }
        private static void MirrorPose(float[] v, int pose, int sourceArm)
        {
            int source = PoseOffset(pose, sourceArm), target = PoseOffset(pose, 1 - sourceArm);
            for (int k = 0; k < 12; k++) v[target + k] = v[source + k] * (k == 0 || k == 4 || k == 5 || k == 7 || k == 8 || k == 9 ? -1f : 1f);
        }

        private static void ResetStroke(float spin)
        {
            spin = -Mathf.Abs(spin);
            _turn[0] = _turn[1] = _release[0] = _release[1] = _top[0] = _top[1] = 0f;
            _lastSpin = spin; _returning = false; _releaseHand = -1; _spinDirection = 0;
        }

        private static void TickStroke(float spin, float stroke)
        {
            spin = -Mathf.Abs(spin); // One working right-turn sequence; Solve reflects its arm roles for left steering.
            const float back = 40f; // Both hands follow small corrections before the trailing hand releases.
            if (_editor && _preview < 2) { ResetStroke(spin); return; }
            float dt = Mathf.Min(Time.deltaTime, .05f);
            if (dt <= 0f) return;
            float delta = spin - _lastSpin, distance = Mathf.Abs(spin);
            if (Mathf.Abs(_vehicle.input.Steering) < .04f || distance < Mathf.Abs(_lastSpin) - .01f) _returning = true;
            else if (distance > Mathf.Abs(_lastSpin) + .01f) _returning = false;
            _lastSpin = spin;
            if (Mathf.Abs(delta) > .001f) _spinDirection = delta > 0f ? 1 : -1;
            if (_returning)
            {
                // Let the rim unwind underneath relaxed hands; no further pulls or regrips on the return.
                _releaseHand = -1;
                float k = 1f - Mathf.Exp(-6f * dt);
                for (int i = 0; i < 2; i++)
                {
                    _turn[i] = Mathf.Lerp(_turn[i], 0f, k); _release[i] = Mathf.Lerp(_release[i], 0f, k);
                    if (Mathf.Abs(_turn[i]) < .1f) _turn[i] = _release[i] = 0f;
                    float side = i == 0 ? 180f - _setup.V[3] : _setup.V[3];
                    _top[i] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_turn[i] / ((i == 0 ? 110f : 70f) - side)));
                }
                return;
            }

            // A closed grip follows the actual wheel delta. Only one hand releases at a time;
            // its reach finishes in time even when the driver stops turning mid-stroke.
            for (int i = 0; i < 2; i++)
            {
                if (i == _releaseHand) continue;
                _turn[i] = Mathf.Clamp(_turn[i] + delta, i == 0 ? -stroke : -back, i == 0 ? back : stroke);
                _release[i] = 0f;
            }
            if (_releaseHand >= 0)
            {
                int hand = _releaseHand;
                _releaseTime = Mathf.Min(1f, _releaseTime + dt / _releaseDuration);
                _turn[hand] = Mathf.Lerp(_releaseStart, _releaseEnd, Mathf.SmoothStep(0f, 1f, _releaseTime));
                float lift = Mathf.Sin(_releaseTime * Mathf.PI);
                _release[hand] = lift * lift;
                if (_releaseTime >= 1f) { _release[hand] = 0f; _releaseHand = -1; }
            }
            if (_releaseHand < 0)
            {
                bool centered = distance < .5f;
                for (int i = 0; i < 2; i++)
                {
                    float lo = i == 0 ? -stroke : -back, hi = i == 0 ? back : stroke;
                    bool atEnd = _spinDirection > 0 ? _turn[i] >= hi - .01f : _spinDirection < 0 && _turn[i] <= lo + .01f;
                    if (centered ? Mathf.Abs(_turn[i]) < .1f : !atEnd) continue;
                    _releaseHand = i; _releaseStart = _turn[i];
                    _releaseEnd = centered ? 0f : _spinDirection > 0 ? lo : hi;
                    _releaseTime = 0f;
                    _releaseDuration = Mathf.Clamp(Mathf.Abs(_releaseEnd - _releaseStart) / Mathf.Max(240f, Mathf.Abs(delta) / dt), .35f, .65f);
                    break;
                }
            }
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 180f - _setup.V[3] : _setup.V[3];
                float top = i == 0 ? 110f : 70f;
                _top[i] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_turn[i] / (top - side)));
            }
        }

        private static void StartEditor()
        {
            if (_setup == null || !Game.InCar || Game.Dead) return;
            _editor = true; _preview = _editPose; _hintNext = 0f;
            UpdateHint();
        }
        private static void StopEditor()
        {
            if (_dirty && _setup != null) Save();
            _dirty = false;
            _editor = false; _preview = _handlebars ? 3 : 2; _hint = ""; _hintNext = 0f;
        }
        private static float Axis(KeyCode plus, KeyCode minus)
        { return (GameBindings.KeyHeld(plus) ? 1f : 0f) - (GameBindings.KeyHeld(minus) ? 1f : 0f); }

        private static void CopySetup()
        {
            if (_setup == null) return;
            Save(); _copiedSetup = (float[])_setup.V.Clone(); _copiedWheel = _name; _hintNext = 0f;
            PlayerBodyModule.Info("Wheel grip setup copied: " + _key);
        }
        private static void PasteSetup()
        {
            if (_setup == null || _copiedSetup == null) return;
            System.Array.Copy(_copiedSetup, _setup.V, System.Math.Min(_copiedSetup.Length, _setup.V.Length));
            if (_handlebars && _copiedSetup.Length < 84)
                for (int pose = 1; pose < 3; pose++) System.Array.Copy(_setup.V, 12, _setup.V, PoseOffset(pose, 0), 24);
            if (_handlebars && _copiedSetup.Length <= 84) System.Array.Clear(_setup.V, 84, 12);
            ResetStroke(_lastSpin); Save(); _hintNext = 0f;
            PlayerBodyModule.Info("Wheel grip setup pasted: " + _copiedWheel + " -> " + _key);
        }

        private static void TickEditor()
        {
            if (_setup == null) return;
            bool ready = PlayerBodyModule.Running && WildWasteland.Plugin.EnableDebug.Value && WildWasteland.Plugin.DeveloperMode.Value;
            bool input = ready && !Game.Paused && !ToolWindow.IsOpen && !ApocasetterBridge.InputBlocked && Application.isFocused;
            if (!ready) { if (_editor) StopEditor(); return; }
            if (!input) { if (_dirty) Save(); return; }
            if (_editor && GameBindings.KeyDown(KeyCode.KeypadPeriod)) { StopEditor(); return; }
            if (!_editor) return;
            if (_owner != "wheel") { if (_dirty) Save(); _hintNext = 0f; UpdateHint(); return; }
            if (GameBindings.KeyDown(KeyCode.Keypad0)) { _target = NextTarget(); _hintNext = 0f; }
            if (GameBindings.KeyDown(KeyCode.Keypad5)) { _rotate = !_rotate; _hintNext = 0f; }
            if (GameBindings.KeyDown(KeyCode.Keypad9)) { _editPose = (_editPose + 1) % (_handlebars ? 3 : 2); _preview = _editPose; _hintNext = 0f; }
            if (GameBindings.KeyDown(KeyCode.Keypad3)) { int live = _handlebars ? 3 : 2; _preview = _preview == live ? _editPose : live; _hintNext = 0f; }
            if (GameBindings.KeyDown(KeyCode.KeypadEnter)) { Save(); _hintNext = 0f; }
            if (GameBindings.KeyDown(KeyCode.KeypadDivide)) CopySetup();
            if (GameBindings.KeyDown(KeyCode.KeypadMultiply)) PasteSetup();
            float up = Axis(KeyCode.Keypad8, KeyCode.Keypad2), right = Axis(KeyCode.Keypad6, KeyCode.Keypad4), forward = Axis(KeyCode.Keypad7, KeyCode.Keypad1);
            if (up != 0f || right != 0f || forward != 0f)
            {
                _preview = _editPose;
                int kind = _target / 2;
                bool rotation = _target == 7 || kind == 1 || kind == 0 && _rotate;
                int o = EditOffset;
                var delta = rotation ? new Vector3(up, right, forward) : new Vector3(right, up, (_handlebars ? 1f : _towardDriver) * forward);
                float rate = rotation ? 20f : 5f;
                if (GameBindings.KeyHeld(KeyCode.LeftShift) || GameBindings.KeyHeld(KeyCode.RightShift)) rate *= .2f;
                delta *= rate * Mathf.Min(Time.unscaledDeltaTime, .05f);
                float limit = _target == 6 ? 50f : _target == 7 ? 60f : rotation ? 90f : _handlebars ? 150f : 50f;
                for (int i = 0; i < 3; i++) _setup.V[o + i] = Mathf.Clamp(_setup.V[o + i] + delta[i], -limit, limit);
                if (!_handlebars && _target < 6) MirrorPose(_setup.V, _editPose, _target & 1);
                _dirty = true;
            }
            else if (_dirty) { Save(); _hintNext = 0f; }
            UpdateHint();
        }

        private static void UpdateHint()
        {
            if (Time.unscaledTime < _hintNext) return;
            _hintNext = Time.unscaledTime + .15f;
            int kind = _target / 2;
            bool rotation = _target == 7 || kind == 1 || kind == 0 && _rotate;
            int o = EditOffset;
            var v = _setup.V;
            var arm = _arms[_target < 6 ? _target & 1 : 1];
            _hint = "WHEEL " + _name + " | " + (_handlebars ? HandlebarPoses[_editPose] + (_preview == 3 ? " / LIVE" : " / PREVIEW") : _preview == 2 ? "LIVE STEERING" : _editPose == 0 ? "SIDE POSE" : "TOP POSE") + " | " + Targets[_target] + " | " + (rotation ? "ROTATE" : "MOVE")
                + "\n" + v[o].ToString("F1", Inv) + " / " + v[o + 1].ToString("F1", Inv) + " / " + v[o + 2].ToString("F1", Inv) + (rotation ? " degrees" : " cm")
                + " | owner: " + _owner + (_handlebars && arm != null && arm.BendLimited ? " | REACH LIMIT: move hand closer or adjust shoulder" : "")
                + "\nNum0 target | Num5 move/rotate | " + (_handlebars ? "Num9 straight/left/right | " : "Num9 SIDE/TOP | ") + "Num3 preview/live | Num8/2 up/pitch | Num6/4 right/yaw | Num7/1 forward-back/roll"
                + "\nShift fine | NumEnter SAVE | Num / COPY | Num * PASTE | Num decimal STOP | auto-save on release";
        }

        internal static void DrawEditorHint()
        {
            if (!EditorVisible || ToolWindow.IsOpen || Game.Paused || !Application.isFocused) return;
            if (_hintStyle == null) _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(20f, Screen.height - 132f, Screen.width - 40f, 116f);
            var color = GUI.color; GUI.color = Color.black;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), _hint, _hintStyle);
            GUI.color = Color.white; GUI.Label(r, _hint, _hintStyle); GUI.color = color;
        }

        private static void DrawPoseEditor(ToolLayout l)
        {
            if (l.Button("SAVE + COPY wheel (Num /)")) CopySetup();
            if (l.Button("PASTE wheel (Num *)", _copiedSetup != null)) PasteSetup();
            if (_copiedSetup != null) l.Value("Copied wheel", _copiedWheel);
            int button = _handlebars ? l.Buttons("STRAIGHT", "LEFT TURN", "RIGHT TURN", "LIVE steering") : l.Buttons("SIDE pose", "TOP pose", "LIVE steering");
            if (button >= 0) { _preview = button; if (button < (_handlebars ? 3 : 2)) _editPose = button; _editor = true; _hintNext = 0f; }
            l.Value("Editing pose / preview", _handlebars ? HandlebarPoses[_editPose] + (_preview == 3 ? " / LIVE" : " / PREVIEW") : (_editPose == 0 ? "SIDE" : "TOP") + " / " + (_preview == 2 ? "LIVE" : _preview == 0 ? "SIDE" : "TOP"));
            if (l.Button("Editing target: " + Targets[_target] + " (next)")) { _target = NextTarget(); _hintNext = 0f; }
            int kind = _target / 2;
            if (kind == 0 && l.Button(_rotate ? "Hand mode: ROTATE (switch)" : "Hand mode: MOVE (switch)")) _rotate = !_rotate;
            bool rotation = _target == 7 || kind == 1 || kind == 0 && _rotate;
            int o = EditOffset;
            string prefix = _key + "/Pose/" + _editPose + "/" + _target + "/" + rotation + "/";
            for (int i = 0; i < 3; i++)
            {
                string label = rotation ? (i == 0 ? "Pitch" : i == 1 ? "Yaw" : "Roll") : (i == 0 ? "Right (cm)" : i == 1 ? "Up (cm)" : _handlebars ? "Forward (cm)" : "Column Z (cm)");
                float limit = _target == 6 ? 50f : _target == 7 ? 60f : rotation ? 90f : _handlebars ? 150f : 50f, old = _setup.V[o + i];
                _setup.V[o + i] = l.Slider(label, prefix + i, old, -limit, limit);
                if (_setup.V[o + i] != old) { if (!_handlebars && _target < 6) MirrorPose(_setup.V, _editPose, _target & 1); _dirty = true; _hintNext = 0f; }
            }
            l.Text("Shoulder rotation moves the upper-arm root through the clavicle. Elbow movement controls its bend direction; bone lengths stay fixed. Move the hand closer when its target is beyond reach.");
            var a = _arms[_target & 1];
            if (a != null) l.Value("Requested / available reach (cm)", (a.Requested * 100f).ToString("F1", Inv) + " / " + (a.Reach * 100f).ToString("F1", Inv));
            if (!_handlebars && l.Button("Copy SIDE to TOP for both arms"))
            {
                for (int i = 0; i < 24; i++) _setup.V[36 + i] = _setup.V[12 + i];
                _dirty = true;
            }
        }
    }
}
