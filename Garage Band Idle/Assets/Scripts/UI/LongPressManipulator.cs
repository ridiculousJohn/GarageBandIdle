using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The hold gesture (design doc 12.11): UI Toolkit has no long-press event,
    // so a down schedules the fire on the target's own scheduler, which is
    // panel time - presentation, never a game read.
    //
    // The rule: a hold fires once, holdSeconds after the down, iff the same
    // press is still down, the pointer has not travelled the drag's start
    // distance from where it went down, it is still over the target, and no
    // element other than the target has taken that pointer since the down. A
    // press that travelled the drag distance is a drag, never a hold.
    //
    // Nothing here captures the pointer, and nothing is stopped while a hold
    // is armed, so a scroll that starts on the target still scrolls. What
    // ends a hold is heard where it can be heard:
    // - the up, the cancel, and a move that has travelled, on the target -
    //   all on the way down, since a button's own click handler stops a press
    //   and a release where it handles them;
    // - the pointer leaving the target itself (a leave trickles, so a child
    //   label's leave is heard here too and ignored);
    // - any element but the target taking the pointer, on the panel's root
    //   for as long as the hold is armed: a drag capturing the pointer sends
    //   its events to the dragging view alone, and the content moving with
    //   the pointer keeps the target under it, so the capture is the one
    //   thing the target can still hear of a drag;
    // - the target losing the pointer, and the target leaving its panel.
    // At the fire the rule is asked again of the panel: the pointer's pending
    // capture is nobody's or the target's, and the element under the last
    // position heard is the target or inside it.
    //
    // A hold is never also a tap, whatever the hold opens. On a button (a
    // levelled group's Auto, an upgrade tile) the button's own click has
    // captured the pointer at the down, so a hold that fires lets go of that
    // capture. The panel tells the click it lost the pointer only once the
    // next pointer event has been dispatched, and when nothing covers the
    // button that next event is the release, landing on the button while its
    // click is still pressed. So the release that follows a fire - the next
    // up or cancel of that pointer heard on the target - is stopped here on
    // the way down, before the button's click hears it; the click then hears
    // only the loss of the pointer, and lets go of the press.
    public sealed class LongPressManipulator : PointerManipulator
    {
        private readonly Action onLongPress;
        private readonly double holdSeconds;

        // The armed press, alive only between a down and whichever ending
        // comes first: the pending fire, the pointer that pressed, where it
        // went down and where it was last heard, and the panel root listened
        // on for captures. pointerId is -1 while nothing is armed.
        private IVisualElementScheduledItem pending;
        private int pointerId = -1;
        private Vector2 downPosition;
        private Vector2 lastPosition;
        private VisualElement root;

        // The pointer whose press fired, until its release has been heard on
        // the target and stopped; -1 while no fired press is waiting for one.
        private int firedPointerId = -1;

        public LongPressManipulator(Action onLongPress, double holdSeconds)
        {
            this.onLongPress = onLongPress;
            this.holdSeconds = holdSeconds;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerLeaveEvent>(OnPointerLeave, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut, TrickleDown.TrickleDown);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut, TrickleDown.TrickleDown);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            Disarm();
            firedPointerId = -1;
        }

        // The left button, a touch's primary press counting as one.
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse || !evt.isPrimary)
                return;
            Disarm();
            firedPointerId = -1;
            pointerId = evt.pointerId;
            downPosition = evt.position;
            lastPosition = downPosition;
            pending = target.schedule.Execute(Fire).StartingIn((long)(holdSeconds * 1000));
            root = target.panel?.visualTree;
            root?.RegisterCallback<PointerCaptureEvent>(OnAnyPointerCapture, TrickleDown.TrickleDown);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            lastPosition = evt.position;
            if ((lastPosition - downPosition).magnitude >= DragScrollManipulator.StartDistance)
                Disarm();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (StopsFiredRelease(evt))
                return;
            if (evt.pointerId == pointerId)
                Disarm();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (StopsFiredRelease(evt))
                return;
            if (evt.pointerId == pointerId)
                Disarm();
        }

        // The release of a press that fired: stopped before anything past this
        // callback hears it, so it is never also a click.
        private bool StopsFiredRelease(IPointerEvent pointer)
        {
            if (pointer.pointerId != firedPointerId)
                return false;
            firedPointerId = -1;
            ((EventBase)pointer).StopImmediatePropagation();
            return true;
        }

        // The pointer leaving the target itself; a child's leave trickles
        // through here as well and is not the pointer leaving the target.
        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (evt.pointerId == pointerId && evt.target == target)
                Disarm();
        }

        // Any element but the target taking the pointer, a descendant
        // included: a drag, or anything else that now owns the press.
        private void OnAnyPointerCapture(PointerCaptureEvent evt)
        {
            if (evt.pointerId == pointerId && evt.target != target)
                Disarm();
        }

        // The target itself losing the pointer; a child's loss trickles
        // through here as well and does not end the hold.
        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == pointerId && evt.target == target)
                Disarm();
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            Disarm();
            firedPointerId = -1;
        }

        private void Fire()
        {
            var id = pointerId;
            if (id < 0)
                return;
            var panel = target.panel;
            var still = panel != null && Holds(panel, id);
            Disarm();
            if (!still)
                return;
            if (target.HasPointerCapture(id))
                target.ReleasePointer(id);
            firedPointerId = id;
            onLongPress();
        }

        // The rule asked of the panel at the fire: nobody but the target holds
        // the pointer, and the pointer is over the target.
        private bool Holds(IPanel panel, int id)
        {
            var capturing = panel.GetCapturingElement(id);
            if (capturing != null && capturing != target)
                return false;
            var under = panel.Pick(lastPosition);
            return under != null && (under == target || target.Contains(under));
        }

        // Every ending: the fire is paused, the root is no longer heard, and
        // nothing is armed.
        private void Disarm()
        {
            pending?.Pause();
            pending = null;
            root?.UnregisterCallback<PointerCaptureEvent>(OnAnyPointerCapture, TrickleDown.TrickleDown);
            root = null;
            pointerId = -1;
        }
    }
}
