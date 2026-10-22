using System;

namespace WastelandMPNew
{
    /// <summary>Persistent world-space position; scene/floating-origin conversion belongs to Core at the boundary.</summary>
    public struct WorldPoint
    {
        /// <summary>World X in metres.</summary>
        public readonly double X;
        /// <summary>World Y in metres.</summary>
        public readonly double Y;
        /// <summary>World Z in metres.</summary>
        public readonly double Z;

        /// <summary>Rejects NaN and infinity before spatial validation or terrain sampling.</summary>
        public WorldPoint(double x, double y, double z)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z)) throw new ArgumentException("Non-finite world position");
            X = x; Y = y; Z = z;
        }

        /// <summary>Tests numeric validity without a Unity or newer-runtime dependency.</summary>
        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
