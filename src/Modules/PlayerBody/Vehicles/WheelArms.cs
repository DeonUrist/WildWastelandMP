using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ES3Internal;
using NWH.VehiclePhysics2;
using UnityEngine;
using WildWasteland.Core;
using WildWasteland.Core.UI;

namespace WildWasteland.Modules.PlayerBody
{
    internal static partial class WheelArms
    {
        private sealed class Setup
        {
            // Original 12 values, then arm poses; handlebars append sitting XYZ and straight/left/right torso Euler.
            internal readonly float[] V = new float[_handlebars ? 96 : 60];
            internal Setup() { V[0] = .95f; V[1] = .03f; V[2] = .85f; V[5] = 75f; }
        }
        private sealed class Arm
        {
            internal Transform Shoulder, Upper, Lower, Hand;
            internal Quaternion Offset;
            internal Vector3 BendLocal, PoleSeed;
            internal float Palm = .09f, Before, After, Requested, Reach;
            internal bool FingerFrame, BendLimited, PoleGuarded;
            internal float PoleDot;
        }
        private static readonly Dictionary<string, Setup> _setups = new Dictionary<string, Setup>();
        private static readonly Arm[] _arms = new Arm[2];
        private static readonly Arm[] _passengerArms = new Arm[2];
        private static Transform _passengerHips;
        private static Transform _car, _mount, _wheel, _hips;
        private static VehicleController _vehicle;
        private static Steering _steering;
        private static bool _handlebars, _removableWheel;
        private static readonly Transform[] _grips = new Transform[2];
        private static readonly Vector3[] _gripPoints = new Vector3[2];
        private static bool _returnToCenter;
        private static float _nextWheel, _nextLog, _towardDriver;
        private static Vector3 _center, _radius;
        private static string _key = "", _name = "", _owner = "off";
        private static Setup _setup;
        private static readonly float[] _top = new float[2];
        private static float _lastSpin;
        private static bool _returning;
        private static int _traceInput;
        private static float _traceUntil, _traceNext;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static bool MissingWheel { get { return _removableWheel && _mount != null && FittedWheel(_mount) == null; } }
        private static Transform FittedWheel(Transform mount)
        {
            if (mount != null) for (int i = 0; i < mount.childCount; i++)
            {
                var child = mount.GetChild(i);
                if (child.gameObject.activeInHierarchy && child.CompareTag("vehPart")) return child;
            }
            return null;
        }
        internal static void Patch(PlayerBodyModule owner)
        {
            Patches.Patch(owner, Patches.Method(typeof(Steering), "CalculateSteerAngles"), prefix: Patches.Method(typeof(WheelArms), "BeforeSteer"));
        }
        private static void BeforeSteer(Steering __instance)
        {
            if (!ReferenceEquals(__instance, _steering) || !MissingWheel) return;
            _vehicle.input.Steering = 0f; // Native steering still owns smoothing and centering, after all keyboard/mouse input writers.
        }

        internal static void Clear()
        {
            StopEditor();
            if (_vehicle != null && _steering != null) _steering.returnToCenter = _returnToCenter;
            _vehicle = null; _steering = null; _car = _mount = _wheel = _hips = null;
            _handlebars = _removableWheel = false; _grips[0] = _grips[1] = null;
            _arms[0] = _arms[1] = null; _setup = null; _key = _name = ""; _owner = "off";
            _passengerArms[0] = _passengerArms[1] = null; _passengerHips = null;
            _nextWheel = _nextLog = 0f;
            ResetStroke(0f);
            _traceInput = 0; _traceUntil = _traceNext = 0f;
        }

        internal static void Tick()
        {
            var car = PlayerBodyModule.Running && !SaveBus.Busy && Game.Ready && Game.InCar && !Game.Dead && !CarSeat.Passenger ? Game.CarRoot : null;
            if (!ReferenceEquals(car, _car))
            {
                Clear(); _car = car;
                if (car != null)
                {
                    _vehicle = car.GetComponent<VehicleController>();
                    if (_vehicle != null)
                    {
                        _steering = _vehicle.steering;
                        if (_steering != null) _returnToCenter = _steering.returnToCenter;
                    }
                }
            }
            if (_vehicle == null || _steering == null) return;
            _steering.returnToCenter = true;   // native smoothing, road-wheel angles and visual all return together
            TickEditor();
            if (Time.unscaledTime < _nextWheel) return;
            _nextWheel = Time.unscaledTime + .5f;
            var mount = _steering.steeringWheel;
            var bars = mount != null ? mount.Find("steering front") : null;
            var wheel = bars != null ? bars : FittedWheel(mount);
            if (ReferenceEquals(mount, _mount) && ReferenceEquals(wheel, _wheel)) return;
            StopEditor();
            _mount = mount; _wheel = wheel; _setup = null; _key = _name = ""; _nextLog = 0f;
            _handlebars = bars != null; _removableWheel = mount != null && mount.name == "hinge_steeringwheel";
            _editPose = 0; _preview = _handlebars ? 3 : 2;
            if (!_handlebars && _target >= 6) _target = 0;
            _grips[0] = _grips[1] = null;
            ResetStroke(_steering.angle * _steering.steeringWheelTurnRatio);
            if (wheel == null)
            {
                if (_removableWheel) PlayerBodyModule.Verbose("Wheel arms: steering wheel missing; resting hands and blocking steering");
                return;
            }
            if (_handlebars)
            {
                _grips[0] = bars.Find("clutch"); _grips[1] = bars.Find("gas");
                for (int i = 0; i < 2; i++)
                {
                    var grip = _grips[i] != null ? _grips[i].GetComponent<MeshFilter>() : null;
                    if (grip == null || grip.sharedMesh == null) { PlayerBodyModule.Warn("Handlebar arms: missing clutch/throttle grip mesh; keeping the seat pose"); return; }
                    _gripPoints[i] = grip.sharedMesh.bounds.center;
                }
                _key = "handlebars";
                _name = "Motorcycle handlebars"; _towardDriver = -1f;
                _target = 1;
                if (!_setups.TryGetValue(_key, out _setup)) { _setup = Load(); _setups.Add(_key, _setup); }
                PlayerBodyModule.Verbose("Handlebar arms: " + Game.PathOf(bars) + "; setup " + _key + "; fixed clutch/throttle grips");
                return;
            }
            // Native wheels carry their rim mesh on the part root; child meshes can be ornaments, not rim geometry.
            var filter = wheel.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) { PlayerBodyModule.Warn("Wheel arms: fitted wheel has no rim mesh; keeping the seat pose"); return; }
            var mesh = filter.sharedMesh; var b = mesh.bounds; var bounds = new Bounds();
            for (int i = 0; i < 8; i++)
            {
                var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                p = mount.InverseTransformPoint(filter.transform.TransformPoint(p));
                if (i == 0) bounds = new Bounds(p, Vector3.zero); else bounds.Encapsulate(p);
            }
            if (bounds.extents.x < .02f || bounds.extents.y < .02f) { PlayerBodyModule.Warn("Wheel arms: rim geometry has no usable XY radius; keeping the seat pose"); return; }
            _center = bounds.center; _radius = bounds.extents;
            _towardDriver = Vector3.Dot(mount.forward, Game.Player.transform.position - mount.position) >= 0f ? 1f : -1f;
            var prefab = wheel.GetComponent<ES3Prefab>();
            _key = prefab != null ? "prefab-" + prefab.prefabId.ToString(Inv) : "mesh-" + mesh.name;
            foreach (char c in Path.GetInvalidFileNameChars()) _key = _key.Replace(c, '_');
            _name = mesh.name;
            if (!_setups.TryGetValue(_key, out _setup)) { _setup = Load(); _setups.Add(_key, _setup); }
            if (PlayerBodyModule.VerboseOn) PlayerBodyModule.Verbose("Wheel arms: " + Game.PathOf(wheel) + "; setup " + _key + "; center " + _center.ToString("F3")
                + "; XY radii " + _radius.x.ToString("F3", Inv) + "/" + _radius.y.ToString("F3", Inv) + "; column local Z " + _towardDriver
                + "; native returnToCenter " + _returnToCenter + " -> true; motion mirrored right reference");
        }

        private static Arm Bind(Dictionary<string, Transform> bones, string side)
        {
            var a = new Arm(); string prefix = "mixamorig:" + side;
            if (!bones.TryGetValue(prefix + "Arm", out a.Upper) || !bones.TryGetValue(prefix + "ForeArm", out a.Lower) || !bones.TryGetValue(prefix + "Hand", out a.Hand)) return null;
            bones.TryGetValue(prefix + "Shoulder", out a.Shoulder);
            Transform mid, index, little;
            bones.TryGetValue(prefix + "HandMiddle1", out mid); bones.TryGetValue(prefix + "HandIndex1", out index); bones.TryGetValue(prefix + "HandPinky1", out little);
            if (mid != null) a.Palm = (mid.position - a.Hand.position).magnitude;
            if (mid != null && index != null && little != null)
            {
                var dir = mid.position - a.Hand.position; var up = Vector3.Cross(dir, little.position - index.position);
                a.FingerFrame = dir.sqrMagnitude > 1e-8f && up.sqrMagnitude > 1e-8f;
                if (a.FingerFrame) a.Offset = Quaternion.Inverse(Quaternion.LookRotation(dir, up)) * a.Hand.rotation;
            }
            return a;
        }

        internal static void Solve(Dictionary<string, Transform> bones, string owner)
        {
            if (_setup == null || _mount == null || _wheel == null || _wheel.parent != _mount) return;
            Transform hips;
            if (!bones.TryGetValue("mixamorig:Hips", out hips)) return;
            if (_handlebars) hips.position += _car.TransformDirection(new Vector3(_setup.V[84], _setup.V[85], _setup.V[86]) * .01f);
            if (owner != _owner) { _owner = owner; _nextLog = 0f; }
            if (owner != "wheel" && owner != "tool")
            {
                ResetStroke(_steering.angle * _steering.steeringWheelTurnRatio);
                if (PlayerBodyModule.VerboseOn && Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 5f; PlayerBodyModule.Verbose("Wheel arms: owner " + owner + "; IK bypassed"); }
                return;
            }
            if (hips != _hips)
            {
                _hips = hips; _arms[0] = Bind(bones, "Left"); _arms[1] = Bind(bones, "Right");
                _nextLog = 0f;
            }
            if (_handlebars) { SolveHandlebars(bones, owner); return; }
            var v = _setup.V;
            float spin = _steering.angle * _steering.steeringWheelTurnRatio;
            var neutral = _mount.rotation * Quaternion.AngleAxis(-spin, Vector3.forward);
            TickStroke(spin, Mathf.Clamp(v[5] * 2f, 120f, 180f));
            // The mesh center may be offset from the column; keep that measured center fixed across multiple turns.
            var scale = _mount.lossyScale;
            var center = _mount.position + neutral * Vector3.Scale(_center, scale);
            int traceInput = _vehicle.input.Steering < -.04f ? -1 : _vehicle.input.Steering > .04f ? 1 : 0;
            if (traceInput != _traceInput) { _traceInput = traceInput; _traceUntil = Time.unscaledTime + 2f; _traceNext = 0f; }
            bool onset = PlayerBodyModule.VerboseOn && traceInput != 0 && Time.unscaledTime <= _traceUntil && Time.unscaledTime >= _traceNext;
            bool report = PlayerBodyModule.VerboseOn && (Time.unscaledTime >= _nextLog || onset);
            if (onset) _traceNext = Time.unscaledTime + .05f;
            var rightArm = _arms[1];
            var rightPole = rightArm != null ? rightArm.Lower.position - rightArm.Upper.position : Vector3.zero;
            var rightWrist = rightArm != null ? rightArm.Hand.rotation : Quaternion.identity;
            for (int i = 0; i < 2; i++)
            {
                if (i == 1 && owner == "tool") continue;
                var a = _arms[i]; if (a == null || a.Hand == null) continue;
                float side = i == 0 ? -1f : 1f;
                int motionArm = spin > 0f ? 1 - i : i;
                float turn = (spin > 0f ? -1f : 1f) * _turn[motionArm];
                float top = _editor && _preview < 2 ? _preview : _top[motionArm];
                int o = PoseOffset(0, i), t = PoseOffset(1, i);
                float sideAngle = i == 0 ? 180f - v[3] : v[3];
                bool preview = _editor && _preview < 2;
                float angle = preview ? Mathf.Lerp(sideAngle, i == 0 ? 110f : 70f, top) : sideAngle + turn;
                float release = preview ? 0f : _release[motionArm];
                var radial = Quaternion.AngleAxis(angle, Vector3.forward) * Vector3.right;
                var rim = new Vector3(radial.x * _radius.x * Mathf.Abs(scale.x), radial.y * _radius.y * Mathf.Abs(scale.y), 0f) * v[0];
                rim *= 1f + release * .12f;
                var fingers = neutral * -radial;
                // Keep the palm frame continuous across twelve o'clock (the old cross product flips there).
                var gripRotation = Quaternion.LookRotation(fingers, neutral * Vector3.forward * -side);
                // Fingerless rigs retain the fresh seated hand frame, turned from their side grip toward the top grip.
                var wristTurn = neutral * Quaternion.AngleAxis(angle - (i == 0 ? 180f - v[3] : v[3]), Vector3.forward) * Quaternion.Inverse(neutral);
                var rotation = (a.FingerFrame ? gripRotation * a.Offset : wristTurn * a.Hand.rotation) * Quaternion.Euler(PoseVector(v, o + 3, t + 3, top));
                if (i == 0 && rightArm != null && !a.FingerFrame)
                {
                    var correction = rightWrist * Quaternion.Euler(PoseVector(v, PoseOffset(0, 1) + 3, PoseOffset(1, 1) + 3, top)) * Quaternion.Inverse(rightWrist);
                    rotation = wristTurn * MirrorRotation(correction, neutral) * a.Hand.rotation;
                }
                var target = center + neutral * rim - fingers * (a.Palm * v[2]) + _mount.forward * (_towardDriver * v[1]) + neutral * PoseVector(v, o, t, top) * .01f;
                float clearance = Mathf.Clamp(Mathf.Max(_radius.x * Mathf.Abs(scale.x), _radius.y * Mathf.Abs(scale.y)) * .5f, .05f, .12f);
                target += _mount.forward * (_towardDriver * release * clearance);
                var shoulder = neutral * Quaternion.Euler(PoseVector(v, o + 6, t + 6, top)) * Quaternion.Inverse(neutral);
                if (a.Shoulder != null) a.Shoulder.rotation = shoulder * a.Shoulder.rotation;
                a.PoleSeed = i == 0 && rightArm != null ? shoulder * MirrorVector(rightPole, neutral) : a.Lower.position - a.Upper.position;
                if (report) a.Before = Vector3.Distance(a.Hand.position, target);
                Reach(a, target, side < 0f ? v[4] : -v[4], neutral * PoseVector(v, o + 9, t + 9, top) * .01f);
                a.Hand.rotation = rotation;
                if (report)
                {
                    a.After = Vector3.Distance(a.Hand.position, target);
                    PlayerBodyModule.Verbose("Wheel arms: " + (i == 0 ? "left" : "right") + "; owner " + owner + "; " + (a.FingerFrame ? "finger frame" : "seated wrist fallback") + "; palm " + a.Palm.ToString("F3", Inv)
                        + "; wrist error before/after " + a.Before.ToString("F3", Inv) + "/" + a.After.ToString("F3", Inv)
                        + " m; requested/reach " + a.Requested.ToString("F3", Inv) + "/" + a.Reach.ToString("F3", Inv) + " m; spin " + spin.ToString("F1", Inv)
                        + "; input " + _vehicle.input.Steering.ToString("F2", Inv) + "; top blend " + top.ToString("F2", Inv) + "; returning " + _returning
                        + "; grip angle " + angle.ToString("F1", Inv) + "; release " + release.ToString("F2", Inv) + "; elbow constrained " + a.BendLimited
                        + "; onset " + onset + "; frame " + Time.frameCount + "; raw pole dot " + a.PoleDot.ToString("F3", Inv) + "; pole guarded " + a.PoleGuarded
                        + "; elbow car " + _car.InverseTransformPoint(a.Lower.position).ToString("F3") + "; release hand " + _releaseHand + "; phase " + _releaseTime.ToString("F3", Inv) + "; mirrored turn " + (spin > 0f));
                }
            }
            if (report) _nextLog = Time.unscaledTime + 5f;
        }

        private static void SolveHandlebars(Dictionary<string, Transform> bones, string owner)
        {
            if (_grips[0] == null || _grips[1] == null) return;
            float steering = _steering.angle / Mathf.Max(1f, _steering.maximumSteerAngle);
            int pose = steering < 0f ? 1 : 2;
            float blend = Mathf.Clamp01(Mathf.Abs(steering));
            if (_editor && _preview < 3) { pose = _editPose; blend = pose == 0 ? 0f : 1f; }
            var v = _setup.V;
            var torso = PoseVector(v, 87, 87 + pose * 3, blend);
            float seatYaw = Mathf.Clamp(_steering.angle * .25f, -8f, 8f);
            torso.y += seatYaw;
            var right = _arms[1];
            if (right == null || right.Hand == null) return;
            var rightWrist = Quaternion.AngleAxis(seatYaw, _car.up) * right.Hand.rotation;
            Transform spine;
            if (bones.TryGetValue("mixamorig:Spine", out spine))
                spine.rotation = _car.rotation * Quaternion.Euler(torso) * Quaternion.Inverse(_car.rotation) * spine.rotation;
            var frame = _wheel.rotation;
            var turn = Quaternion.AngleAxis(_steering.angle * _steering.steeringWheelTurnRatio, _mount.forward);
            var fingers = _wheel.forward;
            var gripBase = _grips[1].TransformPoint(_gripPoints[1]) - fingers * (right.Palm * v[2]) + _wheel.up * v[1];
            var wristBase = right.FingerFrame ? Quaternion.LookRotation(fingers, _wheel.up) * right.Offset : turn * rightWrist;
            var pole = right.Lower.position - right.Upper.position;
            bool report = PlayerBodyModule.VerboseOn && Time.unscaledTime >= _nextLog;
            for (int i = 1; i >= 0; i--)
            {
                var a = _arms[i]; if (a == null || a.Hand == null || i == 1 && owner == "tool") continue;
                int o = PoseOffset(0, i), t = PoseOffset(pose, i);
                var handTarget = (i == 1 ? gripBase : _wheel.position + MirrorVector(gripBase - _wheel.position, frame))
                    + frame * PoseVector(v, o, t, blend) * .01f;
                var handRotation = (i == 1 ? wristBase : MirrorOrientation(wristBase, frame))
                    * Quaternion.Euler(PoseVector(v, o + 3, t + 3, blend));
                var shoulderRotation = frame * Quaternion.Euler(PoseVector(v, o + 6, t + 6, blend)) * Quaternion.Inverse(frame);
                if (a.Shoulder != null) a.Shoulder.rotation = shoulderRotation * a.Shoulder.rotation;
                a.PoleSeed = i == 1 ? pole : MirrorVector(pole, frame);
                Reach(a, handTarget, (i == 1 ? -1f : 1f) * v[4], frame * PoseVector(v, o + 9, t + 9, blend) * .01f);
                a.Hand.rotation = handRotation;
                if (report) PlayerBodyModule.Verbose("Handlebar arms: " + (i == 0 ? "left" : "right") + "; owner " + owner
                    + "; requested/reach " + a.Requested.ToString("F3", Inv) + "/" + a.Reach.ToString("F3", Inv)
                    + " m; wrist error " + Vector3.Distance(a.Hand.position, handTarget).ToString("F3", Inv) + " m; steering " + _steering.angle.ToString("F1", Inv)
                    + "; pose " + pose + "; blend " + blend.ToString("F2", Inv) + "; reach limited " + a.BendLimited
                    + "; torso " + torso.ToString("F1") + "; sitting cm " + new Vector3(v[84], v[85], v[86]).ToString("F1"));
            }
            if (report) _nextLog = Time.unscaledTime + 5f;
        }

        internal static void RestPassengerArms(Dictionary<string, Transform> bones, string owner)
        {
            if (owner != "wheel" && owner != "tool") return;
            Transform hips; var player = Game.Player;
            if (player == null || !bones.TryGetValue("mixamorig:Hips", out hips)) return;
            if (_passengerHips != hips)
            {
                _passengerHips = hips; _passengerArms[0] = Bind(bones, "Left"); _passengerArms[1] = Bind(bones, "Right");
            }
            var frame = player.transform;
            for (int i = 0; i < 2; i++)
            {
                var a = _passengerArms[i]; Transform knee;
                if (a == null || i == 1 && owner == "tool" || !bones.TryGetValue(i == 0 ? "mixamorig:LeftLeg" : "mixamorig:RightLeg", out knee)) continue;
                a.PoleSeed = -frame.up + frame.right * (i == 0 ? -.2f : .2f);
                var rotation = a.FingerFrame ? Quaternion.LookRotation(frame.forward, frame.up) * a.Offset : a.Hand.rotation;
                Reach(a, knee.position + frame.up * .07f - frame.forward * a.Palm, 0f, Vector3.zero, frame);
                a.Hand.rotation = rotation;
            }
        }

        // WWW ArmIK.Solve: two rigid bones, projected fresh seat elbow, clamped reach; never translate or stretch a hand.
        private static void Reach(Arm a, Vector3 target, float swing, Vector3 elbowOffset, Transform frame = null)
        {
            if (frame == null) frame = _car;
            Vector3 sp = a.Upper.position, ep = a.Lower.position, hp = a.Hand.position;
            float upper = (ep - sp).magnitude, lower = (hp - ep).magnitude;
            if (upper < .001f || lower < .001f) return;
            var st = target - sp; a.Requested = st.magnitude; a.Reach = upper + lower;
            float extended = Mathf.Min(a.Reach - .001f, Mathf.Sqrt(upper * upper + lower * lower + 2f * upper * lower * Mathf.Cos(10f * Mathf.Deg2Rad)));
            a.BendLimited = a.Requested > extended;
            float d = Mathf.Clamp(a.Requested, Mathf.Abs(upper - lower) + .001f, extended);
            var dir = st.sqrMagnitude > 1e-8f ? st.normalized : (hp - sp).normalized;
            var bend = Vector3.ProjectOnPlane(a.PoleSeed + elbowOffset, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(-frame.up, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(frame.right, dir);
            bend = Quaternion.AngleAxis(swing, dir) * bend.normalized;
            var previous = Vector3.ProjectOnPlane(frame.TransformDirection(a.BendLocal), dir);
            a.PoleDot = previous.sqrMagnitude > 1e-6f ? Vector3.Dot(previous.normalized, bend) : 1f;
            a.PoleGuarded = false;
            a.BendLocal = frame.InverseTransformDirection(bend);
            float along = (upper * upper - lower * lower + d * d) / (2f * d);
            var elbow = sp + dir * along + bend * Mathf.Sqrt(Mathf.Max(upper * upper - along * along, 0f));
            a.Upper.rotation = Quaternion.FromToRotation(ep - sp, elbow - sp) * a.Upper.rotation;
            a.Lower.rotation = Quaternion.FromToRotation(a.Hand.position - a.Lower.position, sp + dir * d - a.Lower.position) * a.Lower.rotation;
        }

        private static string FilePath { get { return Path.Combine(PlayerBodyModule.DataDir, "WheelGrips", _key + ".txt"); } }
        private static string LoadPath()
        {
            string path = FilePath;
            if (!_handlebars || File.Exists(path)) return path;
            var folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder)) return path;
            var newest = DateTime.MinValue;
            foreach (var legacy in Directory.GetFiles(folder, "handlebars-*.txt"))
            {
                var written = File.GetLastWriteTimeUtc(legacy);
                if (written > newest) { newest = written; path = legacy; }
            }
            return path;
        }
        private static Setup Load(bool defaultsOnly = false)
        {
            var s = new Setup();
            try
            {
                string text;
                var path = defaultsOnly ? FilePath : LoadPath();
                if (!defaultsOnly && File.Exists(path)) text = File.ReadAllText(path);
                else
                    using (var stream = typeof(WheelArms).Assembly.GetManifestResourceStream("PlayerBody.WheelGrips." + _key + ".txt"))
                    {
                        if (stream == null) return s;
                        using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
                        if (PlayerBodyModule.VerboseOn) PlayerBodyModule.Verbose("Wheel arms: built-in calibrated defaults " + _key);
                    }
                var fields = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 12 && fields.Length != 60 && !(_handlebars && fields.Length == 84) && fields.Length != s.V.Length) throw new FormatException("expected 12/60 legacy, 84 or 96 handlebar grip values");
                for (int i = 0; i < fields.Length; i++)
                {
                    float n;
                    if (!float.TryParse(fields[i], NumberStyles.Float, Inv, out n) || float.IsNaN(n) || float.IsInfinity(n)) throw new FormatException("invalid grip value");
                    s.V[i] = n;
                }
                s.V[0] = Mathf.Clamp(s.V[0], .5f, 1.1f); s.V[1] = Mathf.Clamp(s.V[1], -.1f, .1f); s.V[2] = Mathf.Clamp(s.V[2], 0f, 1.2f);
                s.V[3] = Mathf.Clamp(s.V[3], -35f, 35f); s.V[4] = Mathf.Clamp(s.V[4], -60f, 60f); s.V[5] = Mathf.Clamp(s.V[5], 20f, 120f);
                for (int i = 6; i < s.V.Length; i++)
                {
                    int field = (i - 12) % 12;
                    float limit = _handlebars && i >= 12 && (field < 3 || field >= 9) ? 150f : 90f;
                    if (_handlebars && i >= 84) limit = i < 87 ? 50f : 60f;
                    s.V[i] = Mathf.Clamp(s.V[i], -limit, limit);
                }
                if (fields.Length == 12)
                    for (int pose = 0; pose < 2; pose++) for (int arm = 0; arm < 2; arm++)
                        for (int k = 0; k < 3; k++) s.V[PoseOffset(pose, arm) + 3 + k] = s.V[6 + arm * 3 + k];
                if (_handlebars && fields.Length < 84)
                    for (int pose = 1; pose < 3; pose++) Array.Copy(s.V, 12, s.V, PoseOffset(pose, 0), 24);
                if (!_handlebars) for (int pose = 0; pose < 2; pose++) MirrorPose(s.V, pose, 1);
            }
            catch (Exception e) { PlayerBodyModule.Warn("Wheel grip setup " + _key + ": " + e.Message); return new Setup(); }
            return s;
        }
        private static void Save()
        {
            try
            {
                if (!_handlebars) for (int pose = 0; pose < 2; pose++) MirrorPose(_setup.V, pose, 1);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var fields = new string[_setup.V.Length];
                for (int i = 0; i < fields.Length; i++) fields[i] = _setup.V[i].ToString("R", Inv);
                File.WriteAllText(FilePath, string.Join(" ", fields)); _dirty = false;
                PlayerBodyModule.Info("Wheel grip setup saved: " + FilePath);
            }
            catch (Exception e) { PlayerBodyModule.Warn("Saving wheel grip setup: " + e.Message); }
        }

        internal static void Draw(ToolLayout l)
        {
            l.Heading("Steering-wheel hands");
            l.Text(_handlebars ? "One setup for all motorcycles, with independent left/right hand, wrist, shoulder and elbow calibration for each steering pose. Sitting offset applies to every turn; torso bend/rotation is saved for each steering pose." : "Each wheel's right-arm fit is mirrored to its left arm. Editing either side updates the mirrored pair; left-turn motion mirrors the right-turn sequence.");
            l.Text(_handlebars ? "Start with the button below; numpad decimal stops the editor. Num9 selects STRAIGHT / LEFT TURN / RIGHT TURN; Num3 preview/live. Num0 selects left/right hand/shoulder/elbow, sitting offset, torso rotation/bend. Num5 hand move/rotate; 8/2 up/pitch, 6/4 right/yaw, 7/1 forward-back/roll. Enter saves." : "Per-wheel SIDE and TOP poses. Start with the button below; numpad decimal stops the editor. 0 selects hand/shoulder/elbow, 5 move/rotate, 9 side/top, 3 live steering. 8/2 up/down, 6/4 right/left, 7/1 toward/away. Numpad Enter saves. Edits also save on key release.");
            l.Value("Wheel model / setup", _setup != null ? _name + " / " + _key : "no fitted wheel");
            l.Value("Arm owner", _owner);
            if (_setup == null || _mount == null || _wheel == null) return;
            if (l.Button(_editor ? "Stop live wheel editor" : "START live numpad editor", true, true))
            {
                if (_editor) StopEditor(); else ToolWindow.CloseThen(StartEditor);
            }
            DrawPoseEditor(l);
            var v = _setup.V;
            if (!_handlebars) l.Value("Measured rim diameter (cm)", (200f * _radius.x * Mathf.Abs(_mount.lossyScale.x)).ToString("F1", Inv));
            if (!_handlebars) v[0] = l.Slider("Rim radius (%)", _key + "/Radius", v[0] * 100f, 50f, 110f) * .01f;
            v[1] = l.Slider(_handlebars ? "Palm lift above grip (cm)" : "Palm lift toward driver (cm)", _key + "/Lift", v[1] * 100f, -10f, 10f) * .01f;
            v[2] = l.Slider("Palm reach", _key + "/Reach", v[2], 0f, 1.2f);
            if (!_handlebars) v[3] = l.Slider("Grip elevation (degrees)", _key + "/Elevation", v[3], -35f, 35f);
            v[4] = l.Slider("Elbow swing (degrees)", _key + "/Elbow", v[4], -60f, 60f);
            if (!_handlebars) v[5] = l.Slider("Cross-wheel pull (degrees)", _key + "/Stroke", Mathf.Clamp(v[5] * 2f, 120f, 180f), 120f, 180f) * .5f;
            int hit = l.Buttons("SAVE this wheel", "RESET this wheel");
            if (hit == 0) Save();
            else if (hit == 1) { _setup = Load(true); _setups[_key] = _setup; Save(); }
        }
    }
}
