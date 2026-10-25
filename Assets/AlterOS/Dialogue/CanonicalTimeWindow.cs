using UnityEngine;

namespace AlterOS.Dialogue
{
    public static class CanonicalTimeWindow
    {
        public static bool IsWithin(float startHour, float endHour, int localHour)
        {
            if (localHour < 0)
                return true;

            int start = Mathf.RoundToInt(startHour);
            int end = Mathf.RoundToInt(endHour);
            if (start == end)
                return localHour == start;
            if (start < end)
                return localHour >= start && localHour < end;

            return localHour >= start || localHour < end;
        }
    }
}
