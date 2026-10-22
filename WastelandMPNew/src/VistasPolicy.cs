namespace WastelandMPNew
{
    /// <summary>Pure compatibility decisions; the Compatibility module owns signature binding and invokes these rules through Core.</summary>
    public static class VistasPolicy
    {
        /// <summary>Per-machine spawn-track generation stays off during sessions because it changes terrain from local player position.</summary>
        public static bool SpawnTrack(bool session, bool localPreference)
        {
            return !session && localPreference;
        }

        /// <summary>Camera-relative dust-devil spawning stays off until a host-owned replicated replacement exists.</summary>
        public static bool DustDevils(bool session, bool localPreference)
        {
            return !session && localPreference;
        }

        /// <summary>Only the current simulation owner may apply wind, hill-assist, collector writes or similar gameplay mutation.</summary>
        public static bool MayMutate(bool session, bool localOwnsEntity)
        {
            return !session || localOwnsEntity;
        }

        /// <summary>Vistas heat excludes AI-held and never-entered patrol vehicles, including on the host.</summary>
        public static bool MayHeat(bool session, bool localOwnsEntity, bool crewLocked, bool patrolNeverEntered)
        {
            return !crewLocked && !patrolNeverEntered && MayMutate(session, localOwnsEntity);
        }

        /// <summary>Vistas follows the host weather on guests; visual-only randomness may remain camera-local.</summary>
        public static bool FollowHostWeather(bool session, bool host)
        {
            return session && !host;
        }
    }
}
