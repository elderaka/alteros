using System;
using System.Reflection;
using NUnit.Framework;

namespace AlterOS.Tests
{
    internal static class StatsTestAccess
    {
        internal static Type Find(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            Assert.Fail("Missing production type: " + fullName);
            return null;
        }

        internal static object Invoke(object instance, string method, params object[] arguments)
        {
            return instance.GetType().GetMethod(method).Invoke(instance, arguments);
        }

        internal static object NewSnapshot()
        {
            return Activator.CreateInstance(Find("AlterOS.Stats.StatSnapshot"));
        }

        internal static bool TrySet(object snapshot, string id, float value, out string error)
        {
            object[] arguments = { id, value, null };
            bool accepted = (bool)Invoke(snapshot, "TrySet", arguments);
            error = (string)arguments[2];
            return accepted;
        }

        internal static bool TryGet(object snapshot, string id, out float value)
        {
            object[] arguments = { id, 0f };
            bool found = (bool)Invoke(snapshot, "TryGet", arguments);
            value = (float)arguments[1];
            return found;
        }
    }
}
