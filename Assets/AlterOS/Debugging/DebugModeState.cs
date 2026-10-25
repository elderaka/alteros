using System;

namespace AlterOS.Debugging
{
    public sealed class DebugModeState
    {
        public bool IsEnabled { get; private set; }

        public event Action<bool> Changed;

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            var changed = Changed;
            if (changed != null) changed(enabled);
        }

        public void Toggle()
        {
            SetEnabled(!IsEnabled);
        }
    }
}
