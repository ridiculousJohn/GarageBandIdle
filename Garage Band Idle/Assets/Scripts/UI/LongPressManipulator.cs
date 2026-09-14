using System;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The hold gesture (design doc 12.11): UI Toolkit has no long-press event,
    // so a down starts a scheduled callback at the threshold and an up, a leave
    // or a cancel pauses it before it fires. The element's scheduler is panel
    // time, which is presentation and never a game read.
    //
    // Nothing here captures the pointer or stops propagation: the target is the
    // row's text and never its button, so the two gestures share no element, no
    // click needs suppressing, and a scroll that starts on the text still
    // scrolls.
    public sealed class LongPressManipulator : PointerManipulator
    {
        private readonly Action onLongPress;
        private readonly double holdSeconds;

        // The pending fire, alive only between a down and whichever of the
        // three endings comes first.
        private IVisualElementScheduledItem pending;

        public LongPressManipulator(Action onLongPress, double holdSeconds)
        {
            this.onLongPress = onLongPress;
            this.holdSeconds = holdSeconds;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerEnd);
            target.RegisterCallback<PointerLeaveEvent>(OnPointerEnd);
            target.RegisterCallback<PointerCancelEvent>(OnPointerEnd);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerEnd);
            target.UnregisterCallback<PointerLeaveEvent>(OnPointerEnd);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerEnd);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!CanStartManipulation(evt))
                return;
            pending?.Pause();
            pending = target.schedule.Execute(Fire).StartingIn((long)(holdSeconds * 1000));
        }

        // One handler for the three endings: each of them means the hold did
        // not complete, and an item that never fires is simply paused.
        private void OnPointerEnd(EventBase evt)
        {
            pending?.Pause();
            pending = null;
        }

        private void Fire()
        {
            pending = null;
            onLongPress();
        }
    }
}
