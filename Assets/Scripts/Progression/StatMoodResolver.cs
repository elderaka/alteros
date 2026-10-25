namespace AlterOS.Progression
{
    public static class StatMoodResolver
    {
        public static void Resolve(AlterOSProgressionState state)
        {
            if (state == null || state.altera == null)
            {
                return;
            }

            ResolveAltera(state);
            ResolveEnvironment(state);
        }

        private static void ResolveAltera(AlterOSProgressionState state)
        {
            AlteraStatsState altera = state.altera;

            if (altera.energy < 25f)
            {
                altera.mood = "tired";
                altera.availability = "tired";
            }
            else if (altera.stimulation > 85f || altera.engagement > 90f)
            {
                altera.mood = "excited";
                altera.availability = "present";
            }
            else if (altera.engagement < 25f)
            {
                altera.mood = "restless";
                altera.availability = "quiet";
            }
            else if (altera.reassurance < 30f)
            {
                altera.mood = "shy";
                altera.availability = "present";
            }
            else if (altera.trust > 70f && altera.affinity > 70f)
            {
                altera.mood = "warm";
                altera.availability = "present";
            }
            else if (altera.stimulation > 70f)
            {
                altera.mood = "curious";
                altera.availability = "present";
            }
            else
            {
                altera.mood = "calm";
                altera.availability = "present";
            }
        }

        private static void ResolveEnvironment(AlterOSProgressionState state)
        {
            if (state.environment == null)
            {
                return;
            }

            EnvironmentStatsState environment = state.environment;

            if (environment.cleanliness < 25f)
            {
                environment.roomMood = "messy";
            }
            else if (environment.focusAtmosphere > 75f)
            {
                environment.roomMood = "focused";
            }
            else if (environment.coziness > 70f && environment.noise < 35f)
            {
                environment.roomMood = "cozy";
            }
            else if (environment.noise > 70f)
            {
                environment.roomMood = "restless";
            }
            else
            {
                environment.roomMood = "neutral";
            }
        }
    }
}
