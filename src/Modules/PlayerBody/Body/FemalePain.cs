using System;
using System.Collections;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WildWasteland.Modules.PlayerBody
{
    internal static class FemalePain
    {
        private static readonly string[] Names = { "FEMPAIN1", "FEMPAIN2", "FEMPAIN3", "FEMPAIN4" };
        private static readonly AudioClip[] Clips = new AudioClip[Names.Length];
        private static FsmObject[] _pain;
        private static FsmFloat[] _weights;
        private static FsmObject _death;
        private static readonly AudioClip[] Shouts = new AudioClip[2];
        internal static AudioClip PlayerShout { get { return Shout(PlayerBodyModule.Character != null ? (int)PlayerBodyModule.Character.Value : 1); } }
        internal static AudioClip Shout(int choice)
        { return Shouts[choice == (int)PlayerBodyModule.Gender.Female || choice == (int)PlayerBodyModule.Gender.Gungirl || choice == (int)PlayerBodyModule.Gender.Shoota ? 1 : 0]; }

        // (WW) the clips are decoded once per game session (module Init), the patch is a module patch through Core.Patches
        internal static IEnumerator LoadClips()
        {
            yield return null;
            if (!PlayerBodyModule.Running) yield break;
            for (int i = 0; i < Names.Length; i++)
            {
                if (Clips[i] != null) continue;
                try { Clips[i] = Core.VoiceAudio.LoadWav(PlayerBodyModule.ModPath("Sounds/Female/" + Names[i] + ".wav"), Names[i]); PlayerBodyModule.Verbose("Female pain loaded: " + Names[i]); }
                catch (Exception e) { PlayerBodyModule.Warn("Female pain " + Names[i] + ": " + e.Message); }
            }
            _pain = new FsmObject[Clips.Length]; _weights = new FsmFloat[Clips.Length];
            for (int i = 0; i < Clips.Length; i++)
            {
                if (Clips[i] == null) { _pain = null; break; }
                _pain[i] = new FsmObject { ObjectType = typeof(AudioClip), Value = Clips[i] };
                _weights[i] = new FsmFloat { Value = 1f };
            }
            if (_death == null)
            {
                try { _death = new FsmObject { ObjectType = typeof(AudioClip), Value = Core.VoiceAudio.LoadWav(PlayerBodyModule.ModPath("Sounds/Female/FEMDEATH.wav"), "FEMDEATH", 0.2f) }; PlayerBodyModule.Verbose("Female death loaded: FEMDEATH"); }
                catch (Exception e) { PlayerBodyModule.Warn("Female death: " + e.Message); }
            }
            for (int i = 0; i < Shouts.Length; i++)
            {
                if (Shouts[i] != null) continue;
                string name = i == 0 ? "shout_male" : "shout_female";
                try
                {
                    Shouts[i] = Core.VoiceAudio.LoadWav(PlayerBodyModule.ModPath("Sounds/" + name + ".wav"), name, 0.35f);
                    PlayerBodyModule.Verbose("Shout sound loaded: " + name + ".wav");
                }
                catch (Exception e) { PlayerBodyModule.Warn("Shout sound " + name + ": " + e.Message); }
            }
        }

        public static void Patch(Core.IModule owner)
        {
            Core.Patches.Patch(owner, HarmonyLib.AccessTools.Method(typeof(PlayRandomSound), "OnEnter"),
                prefix: HarmonyLib.AccessTools.Method(typeof(FemalePain), nameof(Before)),
                finalizer: HarmonyLib.AccessTools.Method(typeof(FemalePain), nameof(Finally)));
            Core.Patches.Patch(owner, HarmonyLib.AccessTools.Method(typeof(SetAudioClip), "OnEnter"),
                prefix: HarmonyLib.AccessTools.Method(typeof(FemalePain), nameof(BeforeDeath)),
                finalizer: HarmonyLib.AccessTools.Method(typeof(FemalePain), nameof(FinallyDeath)));
        }

        internal struct SoundState { internal FsmObject[] Clips; internal FsmFloat[] Weights; }

        internal static void Before(PlayRandomSound __instance, out SoundState __state)
        {
            __state = new SoundState();
            if (!PlayerBodyModule.Running || !PlayerBodyModule.PainSounds.Value || !PlayerBodyModule.Female || _pain == null || __instance.Fsm == null || __instance.audioClips == null) return;
            string fsm = __instance.Fsm.Name;
            if (fsm != "DamageEffectSound" && fsm != "DamageEffectSound_InCar") return;
            var owner = __instance.Owner;
            if (owner == null) return;
            bool player = Game.Player != null ? owner == Game.Player || owner.transform.IsChildOf(Game.Player.transform)
                : owner.name == "Player" || owner.name == "head" && owner.transform.parent != null && owner.transform.parent.name == "Player";
            if (!player) return;
            __state.Clips = __instance.audioClips; __state.Weights = __instance.weights;
            __instance.audioClips = _pain; __instance.weights = _weights;
        }

        internal static Exception Finally(PlayRandomSound __instance, SoundState __state, Exception __exception)
        {
            if (__state.Clips != null) { __instance.audioClips = __state.Clips; __instance.weights = __state.Weights; }
            return __exception;
        }

        internal static void BeforeDeath(SetAudioClip __instance, out FsmObject __state)
        {
            __state = null;
            if (!PlayerBodyModule.Running || !PlayerBodyModule.PainSounds.Value || !PlayerBodyModule.Female || _death == null || __instance.Fsm == null || __instance.audioClip == null) return;
            if (__instance.Fsm.Name != "Health" || __instance.Fsm.ActiveStateName != "playerDeath") return;
            var owner = __instance.Owner;
            if (owner == null || (Game.Player != null ? owner != Game.Player : owner.name != "Player")) return;
            __state = __instance.audioClip; __instance.audioClip = _death;
            PlayerBodyModule.Verbose("Female death: FEMDEATH selected");
        }

        internal static Exception FinallyDeath(SetAudioClip __instance, FsmObject __state, Exception __exception)
        {
            if (__state != null) __instance.audioClip = __state;
            return __exception;
        }
    }
}
