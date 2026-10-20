using System.Collections.Generic;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.PlayerBody
{
    // Core.Services.Animation: the cross-module face of ModAPI (AGENTS.md 5.1 / PORTING.md 6). NPC and Patrols call this instead of
    // Apocaplayer.ModAPI + a Chainloader GUID check + Rider.Clip reflection. Pure forwarding: every call lands on the static ModAPI
    // (which stays public in this module's namespace). One adapter object per ModAPI.Character, cached, so Attach/Find return the
    // same IAnimatedCharacter for the same Animator, as ModAPI does.
    internal sealed class AnimationService : IAnimationApi
    {
        private readonly Dictionary<ModAPI.Character, CharacterAdapter> _wrapped = new Dictionary<ModAPI.Character, CharacterAdapter>();
        private readonly List<ModAPI.Character> _dead = new List<ModAPI.Character>();

        public int ApiVersion { get { return ModAPI.ApiVersion; } }
        public string Version { get { return ModAPI.PluginVersion; } }
        public bool Ready { get { return ModAPI.Ready; } }
        public bool PlayerThirdPerson { get { return ThirdPerson.On && !ThirdPerson.Peek; } }
        public AudioClip PlayerShout { get { return FemalePain.PlayerShout; } }
        public AudioClip PlayerShoutFor(ulong peer)
        { return peer == Multiplayer.LocalPeer ? FemalePain.PlayerShout : MultiplayerAvatars.Shout(peer); }
        public bool TryPlayerJumpHead(GameObject player, out Vector3 position) { return BodyRunner.TryPlayerJumpHead(player, out position); }
        public bool TryPlayerWeaponMuzzle(out Vector3 position)
        {
            position = Vector3.zero;
            var body = BodyRunner.PlayerBody;
            return PlayerBodyModule.Running && PlayerThirdPerson && !Game.Dead && body != null && body.Alive && body.TryWeaponMuzzle(out position);
        }
        public string[] ClipNames() { return ModAPI.ClipNames(); }
        public bool IsPlayerClip(string name) { return ModAPI.IsPlayerClip(name); }
        public AnimationClip GetClip(string name) { return ModAPI.GetClip(name); }
        public string WeaponKey(string objectName) { return ModAPI.WeaponKey(objectName); }
        public string WeaponKind(string weaponKey) { return ModAPI.WeaponKind(weaponKey); }
        public bool HasWeaponPoses(string weaponKey) { return ModAPI.HasWeaponPoses(weaponKey); }
        public bool WeaponPose(string weaponKey, string clip, out Vector3 localPosition, out Quaternion localRotation) { return ModAPI.WeaponPose(weaponKey, clip, out localPosition, out localRotation); }
        public string ReloadClip(string weaponKey) { return ModAPI.ReloadClip(weaponKey); }
        public float ReloadClipSeconds(string weaponKey) { return ModAPI.ReloadClipSeconds(weaponKey); }
        public float RoundSeconds(string weaponKey) { return ModAPI.RoundSeconds(weaponKey); }
        public bool ReloadsOneRoundAtATime(string weaponKey) { return ModAPI.ReloadsOneRoundAtATime(weaponKey); }
        public bool CocksAfterShot(string weaponKey) { return ModAPI.CocksAfterShot(weaponKey); }
        public float NativeSpeed(string tier) { return ModAPI.NativeSpeed(tier); }
        public int Count { get { return ModAPI.Count; } }
        public IAnimatedCharacter Attach(Animator animator) { return Wrap(ModAPI.Attach(animator)); }
        public IAnimatedCharacter Find(Animator animator) { return Wrap(ModAPI.Find(animator)); }

        private IAnimatedCharacter Wrap(ModAPI.Character c)
        {
            if (c == null) return null;
            CharacterAdapter a;
            if (_wrapped.TryGetValue(c, out a)) return a;
            if (_wrapped.Count >= 32 && _wrapped.Count > ModAPI.Count * 2) Sweep();
            a = new CharacterAdapter(c);
            _wrapped[c] = a;
            return a;
        }

        // adapters of disposed characters (the character itself is already out of ModAPI's list)
        private void Sweep()
        {
            _dead.Clear();
            foreach (var kv in _wrapped) if (!kv.Key.Valid) _dead.Add(kv.Key);
            for (int i = 0; i < _dead.Count; i++) _wrapped.Remove(_dead[i]);
            _dead.Clear();
        }

        internal void Forget(ModAPI.Character c) { if (c != null) _wrapped.Remove(c); }
        internal void Clear() { _wrapped.Clear(); }

        private sealed class CharacterAdapter : IAnimatedCharacter
        {
            private readonly ModAPI.Character _c;
            internal CharacterAdapter(ModAPI.Character c) { _c = c; }

            public Animator Animator { get { return _c.Animator; } }
            public bool Valid { get { return _c.Valid; } }
            public string WeaponKey { get { return _c.WeaponKey; } }
            public string WeaponKind { get { return _c.WeaponKind; } }
            public bool Reloading { get { return _c.Reloading; } }
            public bool Pumping { get { return _c.Pumping; } }
            public string HandsClip { get { return _c.HandsClip; } }
            public string ActionClip { get { return _c.ActionClip; } }
            public string UpperClip { get { return _c.UpperClip; } }
            public float SpeedCap { get { return _c.SpeedCap; } }
            public bool ManualVelocity { get { return _c.ManualVelocity; } set { _c.ManualVelocity = value; } }
            public Vector3 Velocity { get { return _c.Velocity; } set { _c.Velocity = value; } }
            public bool Crouched { get { return _c.Crouched; } set { _c.Crouched = value; } }
            public bool Aiming { get { return _c.Aiming; } set { _c.Aiming = value; } }
            public bool Firing { get { return _c.Firing; } set { _c.Firing = value; } }
            public bool Airborne { get { return _c.Airborne; } set { _c.Airborne = value; } }
            public float AimPitch { get { return _c.AimPitch; } set { _c.AimPitch = value; } }
            public bool TurnInPlace { get { return _c.TurnInPlace; } set { _c.TurnInPlace = value; } }
            public bool WalkToStop { get { return _c.WalkToStop; } set { _c.WalkToStop = value; } }
            public bool Suspended { get { return _c.Suspended; } set { _c.Suspended = value; } }
            public void SetWeapon(Transform model) { _c.SetWeapon(model); }
            public void SetWeapon(Transform model, string weaponKey) { _c.SetWeapon(model, weaponKey); }
            public void Shoot() { _c.Shoot(); }
            public void Shoot(float seconds) { _c.Shoot(seconds); }
            public void Reload(float seconds) { _c.Reload(seconds); }
            public void Reload(float seconds, int rounds) { _c.Reload(seconds, rounds); }
            public void CancelReload() { _c.CancelReload(); }
            public void Pump() { _c.Pump(); }
            public void Jump() { _c.Jump(); }
            public void Kick() { _c.Kick(); }
            public void Throw() { _c.Throw(); }
            public void Throw(bool rightHand) { _c.Throw(rightHand); }
            public void Strike() { _c.Strike(); }
            public void Strike(float seconds) { _c.Strike(seconds); }
            public void Dispose()
            {
                _c.Dispose();
                var s = Services.Animation as AnimationService;
                if (s != null) s.Forget(_c);
            }
        }
    }
}
