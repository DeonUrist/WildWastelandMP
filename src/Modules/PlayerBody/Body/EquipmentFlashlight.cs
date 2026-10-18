using System;
using System.IO;
using UnityEngine;

namespace WildWasteland.Modules.PlayerBody
{
    internal sealed partial class Equipment
    {
        internal struct Flashlight
        {
            internal bool On;
            internal float Range, Intensity, Angle;
            internal Color Color;
            internal Quaternion Aim;
            internal LightShadows Shadows;
            internal bool Changed(Flashlight other)
            { return On != other.On || On && (Range != other.Range || Intensity != other.Intensity || Angle != other.Angle || Color != other.Color || Shadows != other.Shadows || Quaternion.Angle(Aim, other.Aim) > .5f); }
            internal void Write(BinaryWriter w)
            {
                w.Write(On); if (!On) return;
                w.Write(Range); w.Write(Intensity); w.Write(Angle); w.Write(Color.r); w.Write(Color.g); w.Write(Color.b);
                w.Write(Aim.x); w.Write(Aim.y); w.Write(Aim.z); w.Write(Aim.w); w.Write((byte)Shadows);
            }
            internal static Flashlight Read(BinaryReader r)
            {
                var value = new Flashlight { On = r.ReadBoolean() }; if (!value.On) return value;
                value.Range = r.ReadSingle(); value.Intensity = r.ReadSingle(); value.Angle = r.ReadSingle();
                value.Color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1);
                value.Aim = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); value.Shadows = (LightShadows)r.ReadByte();
                float norm = value.Aim.x*value.Aim.x + value.Aim.y*value.Aim.y + value.Aim.z*value.Aim.z + value.Aim.w*value.Aim.w;
                if (!(value.Range > 0 && value.Range <= 200) || !(value.Intensity >= 0 && value.Intensity <= 20) || !(value.Angle >= 1 && value.Angle < 180)
                    || !(value.Color.r >= 0 && value.Color.r <= 4) || !(value.Color.g >= 0 && value.Color.g <= 4) || !(value.Color.b >= 0 && value.Color.b <= 4)
                    || !(norm > .9f && norm < 1.1f) || !Enum.IsDefined(typeof(LightShadows), value.Shadows)) throw new InvalidDataException("Invalid remote flashlight");
                value.Aim = Quaternion.Normalize(value.Aim); return value;
            }
        }
        static Transform _lightCamera;
        static Light _nativeBeam;
        Light _remoteBeam;
        Flashlight _remoteFlashlight;
        internal static Flashlight CaptureFlashlight()
        {
            if (_lightCamera != Game.PlayerCamera || _nativeBeam == null)
            {
                _lightCamera = Game.PlayerCamera;
                var node = _lightCamera != null ? _lightCamera.Find("Flashlight/Spot Light") : null;
                _nativeBeam = node != null ? node.GetComponent<Light>() : null;
            }
            var light = _nativeBeam;
            if (light == null || !light.enabled || !light.gameObject.activeSelf || !light.transform.parent.gameObject.activeSelf || Game.Dead || Game.Player == null) return default(Flashlight);
            return new Flashlight { On = true, Range = light.range, Intensity = light.intensity, Angle = light.spotAngle, Color = light.color,
                Aim = Quaternion.Inverse(Game.Player.transform.rotation)*light.transform.rotation, Shadows = light.shadows };
        }
        void CreateRemoteBeam(Mount mount, GameObject item, Transform lens)
        {
            var node = new GameObject("Apocaplayer.RemoteFlashlight"); node.transform.SetParent(lens, false);
            _remoteBeam = node.AddComponent<Light>(); _remoteBeam.type = LightType.Spot; _remoteBeam.enabled = false;
            var info = Core.FsmUtil.Find(item, "imageUI");
            if (info != null) _remoteBeam.cookie = info.FsmVariables.GetFsmTexture("flashlightCookie").Value;
        }
        internal void ApplyFlashlight(Flashlight value)
        {
            _remoteFlashlight = value;
            if (_remoteBeam != null && value.On)
            { _remoteBeam.range = value.Range; _remoteBeam.intensity = value.Intensity; _remoteBeam.spotAngle = value.Angle; _remoteBeam.color = value.Color; _remoteBeam.shadows = value.Shadows; if (_glow != null) _glow.color = value.Color; }
            TickFlashlight();
        }
        internal void TickFlashlight()
        {
            if (_remoteBeam == null) return;
            bool on = _remoteFlashlight.On;
            if (_remoteBeam.enabled != on) _remoteBeam.enabled = on;
            if (_glow != null && _glow.enabled != on) _glow.enabled = on;
            if (!on) return;
            var root = _root.transform.parent != null ? _root.transform.parent : _root.transform;
            _remoteBeam.transform.rotation = Quaternion.Slerp(_remoteBeam.transform.rotation, root.rotation*_remoteFlashlight.Aim, 1f-Mathf.Exp(-Time.unscaledDeltaTime*20f));
        }
    }
}
