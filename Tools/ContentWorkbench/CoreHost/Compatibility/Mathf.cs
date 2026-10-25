using System;

namespace UnityEngine
{
    // Host-only BCL shim for the production time-window helper; this is not a Unity assembly dependency.
    public static class Mathf
    {
        public static int RoundToInt(float value)
        {
            return (int)Math.Round(value, MidpointRounding.ToEven);
        }
    }
}
