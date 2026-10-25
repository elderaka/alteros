using System;

namespace UnityEngine.Scripting
{
    // Standalone host shim: SQLite-net uses PreserveAttribute only as an attribute base type.
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    public class PreserveAttribute : Attribute { }
}
