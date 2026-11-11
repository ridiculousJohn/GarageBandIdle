using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The page's mouse drag (design doc 12.11): a ScrollView drag-scrolls for a
    // touch pointer alone, and the editor and a desktop build drive the page
    // with a mouse, so a left-button drag here moves the view vertically the
    // way a finger does. A touch or pen pointer is left to the view's own
    // handling.
    //
    // The down trickles, so the view sees a press that lands on a child - a
    // row's button - before the child does. Nothing is captured or stopped on
    // the down: a press that never travels the start distance is an ordinary
    // click on whatever is under it. Only once it has travelled does the
    // target capture the pointer, which takes the press away from the button
    // that had it, so the release does not click.
    //
    // A button captures the pointer the moment it is pressed, and a captured
    // pointer's events go to the capturing element alone - they neither
    // trickle nor bubble. So the view also listens for a child TAKING the
    // pointer and, while that child holds it, hears the moves and the endings
    // on the child itself. That is the arrangement the ScrollView's own touch
    // drag uses for the same reason.
    //
    // The two statics hold the arithmetic, so a headless test reaches it with
    // no panel and no pointer.
    public sealed class DragScrollManipulator : PointerManipulator
    {
        public const float StartDistance = 24f;

        public static bool Starts(float dragDeltaY) => Mathf.Abs(dragDeltaY) >= StartDistance;

        public static float Offset(float startOffset, float dragDeltaY, float maxOffset) =>
            Mathf.Clamp(startOffset - dragDeltaY, 0f, Mathf.Max(0f, maxOffset));

        // The tracked press, alive from a down to whichever ending comes first;
        // -1 while none is tracked.
        private int pointerId = -1;
        private Vector2 downPosition;
        private Vector2 startOffset;
        private bool dragging;

        // The child holding the tracked pointer, whose moves and endings are
        // heard on it directly; null while no child holds it.
        private VisualElement holder;

        private ScrollView View => (ScrollView)target;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCaptureEvent>(OnPointerCapture, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureEvent>(OnPointerCapture, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut, TrickleDown.TrickleDown);
            Unhook();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse || evt.button != (int)MouseButton.LeftMouse)
                return;
            Unhook();
            pointerId = evt.pointerId;
            downPosition = evt.position;
            startOffset = View.scrollOffset;
            dragging = false;
        }

        // A child taking the tracked pointer: from here its events reach that
        // child alone, so the view hears them there.
        private void OnPointerCapture(PointerCaptureEvent evt)
        {
            if (evt.pointerId != pointerId || evt.target == target)
                return;
            if (evt.target is not VisualElement child)
                return;
            Unhook();
            holder = child;
            holder.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            holder.RegisterCallback<PointerUpEvent>(OnPointerUp);
            holder.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        }

        private void Unhook()
        {
            if (holder == null)
                return;
            holder.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            holder.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            holder.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            holder = null;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            var deltaY = evt.position.y - downPosition.y;
            if (!dragging)
            {
                if (!Starts(deltaY))
                    return;
                dragging = true;
                target.CapturePointer(pointerId);
            }
            var view = View;
            view.scrollOffset = new Vector2(startOffset.x,
                Offset(startOffset.y, deltaY, view.verticalScroller.highValue));
            evt.StopPropagation();
        }

        // The up is the one ending a click could be read from, so a drag
        // stops it here and nothing beneath sees a release.
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            if (dragging)
                evt.StopPropagation();
            End();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            End();
        }

        // The callback trickles down, so it hears a CHILD losing the pointer
        // as well as the view. A child giving it up - to this very drag, or to
        // anything else - only ends the listening on that child. The view's
        // own loss of the pointer ends the drag.
        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            if (evt.target == holder)
            {
                Unhook();
                return;
            }
            if (evt.target == target)
                End();
        }

        private void End()
        {
            var id = pointerId;
            var wasDragging = dragging;
            pointerId = -1;
            dragging = false;
            Unhook();
            if (wasDragging && target.HasPointerCapture(id))
                target.ReleasePointer(id);
        }
    }
}
