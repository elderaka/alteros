using AlterOS.Stats;

namespace AlterOS.Progression
{
    /// <summary>Copies only legacy values whose meaning matches the reviewed catalogue.</summary>
    public static class CompanionStatSnapshotBuilder
    {
        public static StatSnapshot Build(AlteraStatsState legacy)
        {
            var snapshot = new StatSnapshot();
            if (legacy == null)
                return snapshot;

            // Invalid or semantically unmapped legacy values stay unknown, never zero-filled.
            string ignoredError;
            snapshot.TrySet("alterra.energy", legacy.energy, out ignoredError);
            snapshot.TrySet("alterra.engagement", legacy.engagement, out ignoredError);
            if (legacy.hydrationKnown)
                snapshot.TrySet("alterra.hydration", legacy.hydration, out ignoredError);
            return snapshot;
        }
    }
}
