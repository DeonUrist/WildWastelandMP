using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;
using WildWasteland.Core;

namespace WildWasteland.Modules.Quests
{
    // Voice acting for hand-written quests. Layout (next to the DLL, next to quests.txt):
    //   Voice Actors\<actor>\<quest id>\offer.wav, accept.wav, remind.wav, done.wav     (the quest id is the [section] name in quests.txt)
    // When a line is spoken, the actors that have a folder for that quest (and that file) are tried in a random order, seeded by
    // the quest and the NPC so the same person keeps the same voice through the whole quest; the first one that has the file is played.
    internal static partial class Voice
    {
        static readonly bool PlaybackEnabled = false; // Temporary until the quest recordings are complete.
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> volume;
        static ConfigEntry<string> folderCfg;
        static ConfigEntry<float> spatial;
        static string root;
        static bool rootLogged;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> bad = new HashSet<string>();
        static AudioSource src;
        static readonly HashSet<string> logged = new HashSet<string>();

        public static void Init(VoiceSettings cfg, string dllDir)
        {
            enabled = cfg.enabled;
            Core.Log.Always("[Quests] Quest voicelines temporarily disabled (recording set incomplete).");
            volume = cfg.volume;

            spatial = cfg.spatial;
            folderCfg = cfg.folderCfg;
            root = dllDir;
            volume.SettingChanged += Changed; folderCfg.SettingChanged += Changed;
        }

        static string Root()
        {
            string c = folderCfg != null ? (folderCfg.Value ?? "").Trim() : "";
            string[] tries = c.Length > 0 ? new[] { c, Path.Combine(c, "Voice Actors") } : new[] { Path.Combine(root, "Voice Actors"), Path.Combine(Path.GetDirectoryName(root) ?? root, "Voice Actors") };
            foreach (string t in tries) if (Directory.Exists(t)) { if (!rootLogged) { rootLogged = true; Plugin.Log.LogInfo("Voice: using " + t); } return t; }
            return null;
        }

        // Plays the line (offer / accept / remind / done) of this quest if some voice actor recorded it. Returns true when something plays.
        public static bool Say(Quest q, string line, Transform npc, string npcName)
        {
            try
            {
                if (!PlaybackEnabled || enabled == null || !enabled.Value || q == null || string.IsNullOrEmpty(q.Id) || q.Id.StartsWith("gen:")) return false;
                string r = Root();
                if (r == null) return false;
                string[] actors = Directory.GetDirectories(r);
                if (actors.Length == 0) return false;
                // random order, but the same for the same quest and NPC, so one person keeps one voice
                var rng = new System.Random(Hash(q.Id + "|" + (npcName ?? "")));
                for (int i = actors.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); string t = actors[i]; actors[i] = actors[j]; actors[j] = t; }
                string safe = SafeName(q.Id);
                foreach (string a in actors)
                {
                    string path = Path.Combine(Path.Combine(Path.Combine(r, Path.GetFileName(a)), safe), line + ".wav");
                    if (!File.Exists(path)) continue;
                    AudioClip clip = Load(path);
                    if (clip == null) continue;
                    Play(clip, npc);
                    if (logged.Add(q.Id + "|" + line + "|" + a)) Plugin.Log.LogInfo("Voice: " + Path.GetFileName(a) + " says " + q.Id + "/" + line + ".");
                    return true;
                }
            }
            catch (Exception e) { Plugin.LogOnce("Voice", e); }
            return false;
        }

        public static void Stop() { try { if (src != null) src.Stop(); } catch (Exception) { } }

        static string SafeName(string id)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return id;
        }

        static int Hash(string s)
        {
            unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; }
        }

        static void Play(AudioClip clip, Transform npc)
        {
            if (src == null)
            {
                var go = new GameObject("ApocalypterQuests_Voice");
                UnityEngine.Object.DontDestroyOnLoad(go);
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
            }
            src.Stop();
            if (npc != null && spatial.Value > 0f) src.transform.position = npc.position + Vector3.up * 1.6f;
            src.spatialBlend = Mathf.Clamp01(spatial.Value);
            src.minDistance = 4f; src.maxDistance = 40f;
            src.volume = Mathf.Clamp01(volume.Value / 5f);
            src.clip = clip;
            src.Play();
        }

        static AudioClip Load(string path)
        {
            AudioClip c;
            if (clips.TryGetValue(path, out c) && c != null) return c;
            if (bad.Contains(path)) return null;
            try
            {
                c = ReadWav(path);
                if (c != null) { clips[path] = c; return c; }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Voice: cannot read " + path + ": " + e.Message); }
            bad.Add(path);
            return null;
        }

        // PCM 8/16/24/32-bit and 32-bit float WAV, any channel count and rate
        static AudioClip ReadWav(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') throw new Exception("not a WAV file");
            int fmtTag = 0, ch = 0, rate = 0, bits = 0, dataPos = -1, dataLen = 0;
            int p = 12;
            while (p + 8 <= b.Length)
            {
                string id = "" + (char)b[p] + (char)b[p + 1] + (char)b[p + 2] + (char)b[p + 3];
                int len = BitConverter.ToInt32(b, p + 4);
                int body = p + 8;
                if (id == "fmt ")
                {
                    fmtTag = BitConverter.ToUInt16(b, body);
                    ch = BitConverter.ToUInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (fmtTag == 0xFFFE && len >= 26) fmtTag = BitConverter.ToUInt16(b, body + 24);       // extensible: the real tag
                }
                else if (id == "data")
                {
                    dataPos = body;
                    dataLen = (len < 0 || body + len > b.Length) ? b.Length - body : len;
                    break;
                }
                p = body + len + (len & 1);
            }
            if (dataPos < 0 || ch <= 0 || rate <= 0) throw new Exception("no audio data");
            int bytes = bits / 8;
            if (bytes <= 0) throw new Exception("bad bit depth");
            int count = dataLen / bytes;
            count -= count % ch;
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                int o = dataPos + i * bytes;
                float v;
                if (fmtTag == 3 && bits == 32) v = BitConverter.ToSingle(b, o);
                else if (bits == 8) v = (b[o] - 128) / 128f;
                else if (bits == 16) v = BitConverter.ToInt16(b, o) / 32768f;
                else if (bits == 24) v = (((b[o + 2] << 24) | (b[o + 1] << 16) | (b[o] << 8)) >> 8) / 8388608f;
                else if (bits == 32) v = BitConverter.ToInt32(b, o) / 2147483648f;
                else throw new Exception("unsupported format (" + bits + "-bit)");
                data[i] = v;
            }
            // level every recording the same (peak to 0.9), then boost for Volume above 5 with a soft clip so it cannot crackle
            float peak = 0f;
            for (int i = 0; i < count; i++) { float a = Mathf.Abs(data[i]); if (a > peak) peak = a; }
            float lvl = peak > 0.0005f ? 0.9f / peak : 1f;
            float boost = Mathf.Max(1f, volume.Value / 5f);
            for (int i = 0; i < count; i++)
            {
                float v = data[i] * lvl * boost;
                data[i] = boost > 1f ? (float)Math.Tanh(v) : v;
            }
            AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), count / ch, ch, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
        internal static void Shutdown()
        {
            if (volume != null) volume.SettingChanged -= Changed; if (folderCfg != null) folderCfg.SettingChanged -= Changed;
            Stop(); if (src != null) UnityEngine.Object.Destroy(src.gameObject); src = null;
            foreach (var clip in clips.Values) if (clip != null) UnityEngine.Object.Destroy(clip);
            clips.Clear(); bad.Clear(); logged.Clear(); root = null; rootLogged = false;
        }
        static void Changed(object sender, EventArgs args)
        {
            Stop(); if (src != null) src.clip = null;
            foreach (var clip in clips.Values) if (clip != null) UnityEngine.Object.Destroy(clip);
            clips.Clear(); bad.Clear(); rootLogged = false;
        }
    }
}
