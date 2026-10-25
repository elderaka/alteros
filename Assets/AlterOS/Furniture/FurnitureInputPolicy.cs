namespace AlterOS.Furniture
{
    public enum FurnitureInputControl
    {
        T,
        Q,
        E,
        LeftMouseButton,
        RightMouseButton,
        ScrollUp,
        ScrollDown,
        Z,
        X,
        C,
        F
    }

    public enum FurnitureInputIntent
    {
        None,
        Interact,
        PickUpPlacedFurniture,
        OpenInventory,
        ConfirmPlacement,
        ReturnHeldItem,
        RotateClockwise,
        RotateCounterClockwise,
        ToggleGridSnap,
        CancelHandling
    }

    public sealed class FurnitureInputContext
    {
        public bool holdingFurniture;
        public bool modalOwnsInput;
        public bool textEntryActive;
        public bool pointerOverUi;
    }

    public static class FurnitureInputPolicy
    {
        public static FurnitureInputIntent Resolve(FurnitureInputControl control, FurnitureInputContext context)
        {
            if (context == null || context.modalOwnsInput || context.textEntryActive) return FurnitureInputIntent.None;
            if (context.pointerOverUi && IsMouseButton(control)) return FurnitureInputIntent.None;

            if (!context.holdingFurniture)
            {
                switch (control)
                {
                    case FurnitureInputControl.T: return FurnitureInputIntent.Interact;
                    case FurnitureInputControl.Q: return FurnitureInputIntent.PickUpPlacedFurniture;
                    case FurnitureInputControl.E: return FurnitureInputIntent.OpenInventory;
                    default: return FurnitureInputIntent.None;
                }
            }

            switch (control)
            {
                case FurnitureInputControl.Q:
                case FurnitureInputControl.LeftMouseButton:
                    return FurnitureInputIntent.ConfirmPlacement;
                case FurnitureInputControl.E:
                case FurnitureInputControl.RightMouseButton:
                    return FurnitureInputIntent.ReturnHeldItem;
                case FurnitureInputControl.ScrollUp:
                case FurnitureInputControl.Z:
                    return FurnitureInputIntent.RotateClockwise;
                case FurnitureInputControl.ScrollDown:
                case FurnitureInputControl.X:
                    return FurnitureInputIntent.RotateCounterClockwise;
                case FurnitureInputControl.C:
                    return FurnitureInputIntent.ToggleGridSnap;
                case FurnitureInputControl.F:
                    return FurnitureInputIntent.CancelHandling;
                default:
                    return FurnitureInputIntent.None;
            }
        }

        private static bool IsMouseButton(FurnitureInputControl control)
        {
            return control == FurnitureInputControl.LeftMouseButton || control == FurnitureInputControl.RightMouseButton;
        }
    }
}
