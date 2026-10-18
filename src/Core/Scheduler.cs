using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace WildWasteland.Core
{
    public enum Phase
    {
        /// <summary>Runs before every game script (execution order -1000). Use for input/climb controllers that must see the frame first.</summary>
        EarlyUpdate,
        Update,
        LateUpdate,
        /// <summary>Runs after every game script's LateUpdate (execution order 20000).</summary>
        LateUpdateLast,
        EarlyFixedUpdate,
        FixedUpdate,
        /// <summary>OnGUI of the Core runner. Draw jobs only; keep GUI.depth >= 0 (the Apocasetter window owns -1000).</summary>
        Gui
    }

    /// <summary>
    /// A unit of periodic work owned by a module (AGENTS.md 5.4). Created through <see cref="Scheduler"/>.
    /// Every job is timed with a Stopwatch; the totals feed the F7 overlay and docs/perf-log.md.
    /// </summary>
    public sealed class Job
    {
        public readonly IModule Owner;
        public readonly string Name;
        public readonly Phase Phase;
        public bool Enabled = true;
        internal Action Tick;
        internal Func<bool> Step;       // sliced: returns false when the queue is empty
        internal float Interval;        // 0 = every frame
        internal double NextAt;         // unscaled time
        internal int BudgetMicros;      // sliced
        internal bool Removed;
        internal bool Scaled;           // interval uses Time.time (pauses with the game) instead of unscaled time
        internal Func<bool> Wants;      // Gui jobs: null = always draws; else drawn (and the GUI runner kept on) only while it returns true

        public long Calls;
        public double TotalMs, LastMs, MaxMs;
        public int Exceptions;
        internal float ErrorWindowStart; internal int WindowErrors;

        internal Job(IModule owner, string name, Phase phase) { Owner = owner; Name = name; Phase = phase; }
        public void Remove() { Removed = true; }
        /// <summary>Force an interval job to run on its next phase tick.</summary>
        public void RunNow() { NextAt = 0; }
        public override string ToString() { return (Owner != null ? Owner.Id : "Core") + "." + Name; }
    }

    /// <summary>
    /// The only update loop of the mod. Modules register jobs; the runner MonoBehaviours call <see cref="Run"/> per phase.
    /// Jobs run in registration order within a phase (module init order), which preserves each source mod's internal order.
    /// </summary>
    public static class Scheduler
    {
        static readonly List<Job>[] _jobs = NewLists();
        static readonly Stopwatch _sw = new Stopwatch();
        static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
        static readonly System.Random _rng = new System.Random();

        /// <summary>Total WW managed time of the current frame (ms), reset in EarlyUpdate. Sliced jobs stop when it exceeds <see cref="FrameBudgetMs"/>.</summary>
        public static double FrameMs;
        public static double FrameBudgetMs = 2.0;
        const int JobErrorLimit = 20;   // errors per 10 s that switch one job off
        public static double MaxFrameMs;
        public static long Frames;
        public static double AvgFrameMs { get { return Frames > 0 ? _sumFrameMs / Frames : 0; } }
        static double _sumFrameMs;

        static List<Job>[] NewLists()
        {
            var a = new List<Job>[Enum.GetValues(typeof(Phase)).Length];
            for (int i = 0; i < a.Length; i++) a[i] = new List<Job>();
            return a;
        }

        public static Job EveryFrame(IModule owner, string name, Action tick, Phase phase = Phase.Update)
        {
            var j = new Job(owner, name, phase) { Tick = tick };
            _jobs[(int)phase].Add(j);
            return j;
        }

        /// <summary>Runs <paramref name="tick"/> every <paramref name="seconds"/> (unscaled time, random phase so jobs spread over frames).</summary>
        public static Job Interval(IModule owner, string name, float seconds, Action tick, Phase phase = Phase.Update, bool scaledTime = false)
        {
            var j = new Job(owner, name, phase) { Tick = tick, Interval = seconds, Scaled = scaledTime };
            j.NextAt = Now(scaledTime) + seconds * _rng.NextDouble();
            _jobs[(int)phase].Add(j);
            return j;
        }

        /// <summary>
        /// Work queue processed for at most <paramref name="micros"/> microseconds per frame: <paramref name="step"/> is called
        /// repeatedly while it returns true (more work) and the budget (job + whole-frame) is not spent.
        /// </summary>
        public static Job Sliced(IModule owner, string name, int micros, Func<bool> step, Phase phase = Phase.Update)
        {
            var j = new Job(owner, name, phase) { Step = step, BudgetMicros = micros };
            _jobs[(int)phase].Add(j);
            return j;
        }

        /// <summary>OnGUI draw job (Phase.Gui). Always called (no predicate): keeps the GUI runner on every frame.</summary>
        public static Job Gui(IModule owner, string name, Action draw)
        {
            return EveryFrame(owner, name, draw, Phase.Gui);
        }

        /// <summary>
        /// OnGUI draw job that only draws while <paramref name="wants"/> returns true (checked once a frame after LateUpdate and before each
        /// call). When no Gui job wants to draw and the F7 window is closed, Unity's OnGUI (Layout + Repaint + every input event) is not run at all.
        /// The predicate must be cheap and must not allocate.
        /// </summary>
        public static Job Gui(IModule owner, string name, Action draw, Func<bool> wants)
        {
            var j = EveryFrame(owner, name, draw, Phase.Gui);
            j.Wants = wants;
            return j;
        }

        /// <summary>True when some Gui job has to run this frame (no predicate, or its predicate is true). Called by the runner.</summary>
        internal static bool GuiWanted()
        {
            var list = _jobs[(int)Phase.Gui];
            for (int i = 0; i < list.Count; i++)
            {
                var j = list[i];
                if (j.Removed || !j.Enabled) continue;
                if (j.Wants == null) return true;
                try { if (j.Wants()) return true; }
                catch (Exception e) { j.Wants = null; Log.Error("[" + (j.Owner != null ? j.Owner.Id : "Core") + "] Gui job '" + j.Name + "' predicate failed - now always drawn", e); return true; }
            }
            return false;
        }

        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            var r = Runner.Instance;
            return r != null ? r.StartCoroutine(routine) : null;
        }
        public static void StopCoroutine(Coroutine c)
        {
            var r = Runner.Instance;
            if (r != null && c != null) r.StopCoroutine(c);
        }

        public static void RemoveAll(IModule owner)
        {
            for (int p = 0; p < _jobs.Length; p++)
            {
                var list = _jobs[p];
                for (int i = list.Count - 1; i >= 0; i--) if (list[i].Owner == owner) list.RemoveAt(i);
            }
        }

        public static IEnumerable<Job> Jobs(Phase phase) { return _jobs[(int)phase]; }
        public static IEnumerable<Job> AllJobs()
        {
            for (int p = 0; p < _jobs.Length; p++) for (int i = 0; i < _jobs[p].Count; i++) yield return _jobs[p][i];
        }

        static double Now(bool scaled) { return scaled ? Time.timeAsDouble : Time.unscaledTimeAsDouble; }

        /// <summary>Called by the runner MonoBehaviours. Not for modules.</summary>
        internal static void Run(Phase phase)
        {
            if (phase == Phase.EarlyUpdate)
            {
                if (Frames > 0) { _sumFrameMs += FrameMs; if (FrameMs > MaxFrameMs) MaxFrameMs = FrameMs; }
                Frames++;
                FrameMs = 0;
            }
            var list = _jobs[(int)phase];
            double unscaled = Time.unscaledTimeAsDouble, scaled = Time.timeAsDouble;
            for (int i = 0; i < list.Count; i++)
            {
                var j = list[i];
                if (j.Removed) { list.RemoveAt(i); i--; continue; }
                if (!j.Enabled) continue;
                if (MultiplayerSaving.Frozen && j.Owner != null && j.Owner != Multiplayer.PatchOwner
                    && j.Owner.Id != "CleanHUD" && j.Owner.Id != "HUD") continue;
                if (j.Wants != null)
                {
                    bool want;
                    try { want = j.Wants(); } catch (Exception) { want = true; j.Wants = null; }
                    if (!want) continue;
                }
                if (j.Interval > 0)
                {
                    double now = j.Scaled ? scaled : unscaled;
                    if (now < j.NextAt) continue;
                    j.NextAt = now + j.Interval;
                }
                long t0 = _sw.ElapsedTicks;
                if (!_sw.IsRunning) { _sw.Start(); t0 = _sw.ElapsedTicks; }
                try
                {
                    if (j.Step != null)
                    {
                        long budget = (long)(j.BudgetMicros * (Stopwatch.Frequency / 1000000.0));
                        long t = t0;
                        while (j.Step())
                        {
                            t = _sw.ElapsedTicks;
                            if (t - t0 > budget) break;
                            if (FrameMs + (t - t0) * TicksToMs > FrameBudgetMs) break;
                        }
                    }
                    else j.Tick();
                }
                catch (Exception e)
                {
                    j.Exceptions++;
                    ModuleHost.ReportJobException(j, e);
                    // failure isolation per job: a loop that throws every frame is switched off (the rest of its module keeps running);
                    // Init after a module off -> on registers fresh jobs
                    if (unscaled - j.ErrorWindowStart > 10.0) { j.ErrorWindowStart = (float)unscaled; j.WindowErrors = 0; }
                    if (++j.WindowErrors >= JobErrorLimit && j.Enabled)
                    {
                        j.Enabled = false;
                        Log.Error("[" + (j.Owner != null ? j.Owner.Id : "Core") + "] job '" + j.Name + "' threw " + j.WindowErrors + " times in 10 s - job DISABLED for this session (last: " + e.Message + "). Switch the module off and on (F6 / F7 Modules) to retry.");
                    }
                }
                double ms = (_sw.ElapsedTicks - t0) * TicksToMs;
                j.Calls++; j.LastMs = ms; j.TotalMs += ms; if (ms > j.MaxMs) j.MaxMs = ms;
                FrameMs += ms;
            }
        }

        public static void ResetStats()
        {
            Frames = 0; _sumFrameMs = 0; MaxFrameMs = 0;
            foreach (var j in AllJobs()) { j.Calls = 0; j.TotalMs = 0; j.MaxMs = 0; j.LastMs = 0; }
        }
    }
}
