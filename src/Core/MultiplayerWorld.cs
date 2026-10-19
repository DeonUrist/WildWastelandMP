using System;
using System.IO;
using UnityEngine;

namespace WildWasteland.Core
{
    internal static class MultiplayerWorld
    {
        static string _slot;
        static float _next;
        internal static event Action Ready, Leaving;
        internal static string PersonalSlot { get { return Multiplayer.Client ? _slot : SaveBus.Slot; } }
        internal static string GuestSlot { get { return _slot; } }
        internal static void Reset()
        {
            if (_slot != null) { var leaving = Leaving; if (leaving != null) leaving(); }
            _slot = null; _next = 0;
        }
        internal static void Tick()
        {
            if (!Multiplayer.Client || !Multiplayer.WorldReady || _slot != null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 3f; Multiplayer.Send(5, w => { });
        }
        internal static void Receive(ulong peer, byte kind, BinaryReader reader)
        {
            if (kind == 5 && Multiplayer.Hosting && SaveBus.Slot != null)
            { string slot = HostSaveSystem.Lineage(SaveBus.Slot); Multiplayer.Send(6, w => w.Write(slot), peer); return; }
            if (kind != 6 || peer != 0 || !Multiplayer.Client || _slot != null) return;
            string name = reader.ReadString();
            if (name.Length > 100 || !MultiplayerHosting.IsSaveFile(name)) return;
            _slot = name; var ready = Ready; if (ready != null) ready();
        }
    }
}
