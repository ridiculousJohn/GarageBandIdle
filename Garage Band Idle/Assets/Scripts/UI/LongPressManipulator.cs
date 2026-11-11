using System;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The hold gesture (design doc 12.11): UI Toolkit has no long-press event,
    // so a down starts a scheduled callback at the threshold and an up, a leave,
    // a cancel or the target losing the pointer pauses it before it fires. The
    // element's scheduler is panel time, which is presentation and never a
    // game read.
    //
    // Nothing here captures the pointer or stops propagation, so a scroll that
    // starts on the target still scrolls. On a row's text the target is never
    // a button and the two gestures share no element. On a button (a levelled
    // group's Auto) the button's own click has captured the pointer at the
    // down, so a hold that fires lets go of that capture: the click is then no
    // longer pressed, the release lands on whatever the hold opened, and a
    // hold is never also a tap. A drag that takes the pointer away from the
    // button (DragScrollManipulator) ends the hold before it fires.
    public sealed class LongPressManipulator : PointerManipulator
    {
        private readonly Action onLongPress;
        private readonly double holdSeconds;

        // The pending fire, alive only between a down and whichever of the
        // four endings comes first, and the pointer that pressed.
        private IVisualElementScheduledItem pending;
        private int pointerId = -1;

        public LongPressManipulator(Action onLongPress, double holdSeconds)
        {
            this.onLongPress = onLongPress;
            this.holdSeconds = holdSeconds;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        // Every callback listens on the way DOWN. A button's own click handler
        // stops a press and a release where it handles them, so a listener on
        // the way up never hears either on a button; the trickle phase runs
        // first and reaches the target on a label and a button alike.
        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerEnd, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerLeaveEvent>(OnPointerEnd, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(OnPointerEnd, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerEnd, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerLeaveEvent>(OnPointerEnd, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerEnd, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut, TrickleDown.TrickleDown);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!CanStartManipulation(evt))
                return;
            pending?.Pause();
            pointerId = evt.pointerId;
            pending = target.schedule.Execute(Fire).StartingIn((long)(holdSeconds * 1000));
        }

        // One handler for the endings: each of them means the hold did not
        // complete, and an item that never fires is simply paused.
        private void OnPointerEnd(EventBase evt)
        {
            pending?.Pause();
            pending = null;
        }

        // The target itself losing the pointer, a drag taking it: a child's
        // loss does not end the hold.
        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.target == target)
                OnPointerEnd(evt);
        }

        private void Fire()
        {
            pending = null;
            if (pointerId >= 0 && target.HasPointerCapture(pointerId))
                target.ReleasePointer(pointerId);
            pointerId = -1;
            onLongPress();
        }
    }
}
