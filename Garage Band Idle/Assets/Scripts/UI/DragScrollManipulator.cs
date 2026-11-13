using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The mouse drag of a scroll view (design doc 12.11): a ScrollView
    // drag-scrolls for a touch or pen pointer alone, and the editor and a
    // desktop build drive the game with a mouse, so a left-button drag here
    // moves the view along its axis the way a finger does - vertically for the
    // page and the overlay lists, sideways for a section's strip. A touch or
    // pen pointer is left to the view's own handling.
    //
    // The arrangement is the ScrollView's own touch drag. The down trickles,
    // so the view sees a press that lands on a child - a row's button - before
    // the child does. Nothing is captured or stopped on the down: a press that
    // never travels the start distance is an ordinary click on whatever is
    // under it. Only once it has travelled does the view capture the pointer,
    // which takes the press away from the button that had it, so the release
    // clicks nothing.
    //
    // A button captures the pointer the moment it is pressed, and a captured
    // pointer's events go to the capturing element alone - they neither
    // trickle nor bubble. So while a press is tracked, the view listens on its
    // panel's root for every capture of that pointer: a child taking it is
    // hooked, and the moves and endings are heard on that child until it lets
    // go. A nested scroll view taking it is that view's drag, and an element
    // outside this view taking it is a drag or a gesture that is not this
    // view's; either way this view lets the press go.
    //
    // A strip sits inside the page, so one press is heard by both views. The
    // axis that has travelled further owns the drag (Starts): the two answers
    // never hold together, a tie going to the page, so one press moves one
    // view.
    //
    // A release can be lost (Unity's UUM-138133: the up never reaches the
    // view). Every ending therefore releases the view's capture whether or not
    // a drag ran, a move of the tracked pointer with the left button no longer
    // pressed is an ending, and a down finding the view still holding the
    // pointer releases it before anything else. No press can leave the page
    // holding the pointer.
    //
    // The statics hold the arithmetic, so a headless test reaches it with no
    // panel and no pointer.
    public sealed class DragScrollManipulator : PointerManipulator
    {
        public const float StartDistance = 24f;

        public static bool Starts(float dragDeltaY) => Mathf.Abs(dragDeltaY) >= StartDistance;

        // Whether a drag of `delta` starts on a view scrolling along `axis`:
        // the travel along the axis reaches the start distance and is the
        // larger of the two, strictly so for a sideways view.
        public static bool Starts(Vector2 delta, ScrollViewMode axis)
        {
            var along = axis == ScrollViewMode.Horizontal ? delta.x : delta.y;
            var across = axis == ScrollViewMode.Horizontal ? delta.y : delta.x;
            if (!Starts(along))
                return false;
            return axis == ScrollViewMode.Horizontal
                ? Mathf.Abs(along) > Mathf.Abs(across)
                : Mathf.Abs(along) >= Mathf.Abs(across);
        }

        public static float Offset(float startOffset, float dragDeltaY, float maxOffset) =>
            Mathf.Clamp(startOffset - dragDeltaY, 0f, Mathf.Max(0f, maxOffset));

        // The axis this view scrolls along: Vertical for the page and the
        // overlay lists, Horizontal for a strip.
        private readonly ScrollViewMode axis;

        // The tracked press, alive from a down to whichever ending comes first;
        // -1 while none is tracked.
        private int pointerId = -1;
        private Vector2 downPosition;
        private Vector2 startOffset;
        private bool dragging;

        // The panel root heard for captures while a press is tracked, kept so
        // the listener comes off the root it went on even once the view has
        // left its panel; null while none is tracked.
        private VisualElement root;

        // The child holding the tracked pointer, whose moves and endings are
        // heard on it directly; null while no child holds it.
        private VisualElement holder;

        public DragScrollManipulator(ScrollViewMode axis = ScrollViewMode.Vertical) => this.axis = axis;

        private ScrollView View => (ScrollView)target;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut, TrickleDown.TrickleDown);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut, TrickleDown.TrickleDown);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            End();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse || evt.button != (int)MouseButton.LeftMouse)
                return;
            // A press that is still tracked never saw its release, and a view
            // still holding this pointer lost one: both are let go first, so the
            // press starts from nothing held.
            End();
            if (target.HasPointerCapture(evt.pointerId))
                target.ReleasePointer(evt.pointerId);
            pointerId = evt.pointerId;
            downPosition = evt.position;
            startOffset = View.scrollOffset;
            dragging = false;
            root = target.panel?.visualTree;
            root?.RegisterCallback<PointerCaptureEvent>(OnAnyPointerCapture, TrickleDown.TrickleDown);
        }

        // Any element taking the tracked pointer. The view's own capture is
        // its drag. A child of the view taking it is hooked: from here its
        // events reach that child alone, so the view hears them there. A nested
        // scroll view, or anything outside this view, taking it ends the press
        // here - it is that element's drag or gesture, never this view's.
        private void OnAnyPointerCapture(PointerCaptureEvent evt)
        {
            if (evt.pointerId != pointerId || evt.target == target)
                return;
            Unhook();
            if (evt.target is not VisualElement element || element is ScrollView || !target.Contains(element))
            {
                End();
                return;
            }
            holder = element;
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
            // The left button let go without its up reaching the view: the
            // press is over, whatever the move says.
            if ((evt.pressedButtons & 1) == 0)
            {
                End();
                return;
            }
            var delta = (Vector2)evt.position - downPosition;
            if (!dragging)
            {
                if (!Starts(delta, axis))
                    return;
                dragging = true;
                target.CapturePointer(pointerId);
            }
            var view = View;
            view.scrollOffset = axis == ScrollViewMode.Horizontal
                ? new Vector2(Offset(startOffset.x, delta.x, view.horizontalScroller.highValue), startOffset.y)
                : new Vector2(startOffset.x, Offset(startOffset.y, delta.y, view.verticalScroller.highValue));
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
        // own loss of the pointer ends the drag; outside a drag the view holds
        // nothing of this press, so a loss then is the release a down made of
        // an earlier capture.
        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            if (evt.target == holder)
            {
                Unhook();
                return;
            }
            if (evt.target == target && dragging)
                End();
        }

        // The view leaving its panel takes its capture with it; the press is
        // over.
        private void OnDetach(DetachFromPanelEvent evt) => End();

        // Every ending: the press is forgotten, the child and the root are no
        // longer heard, and the view lets go of the pointer if it holds it,
        // dragging or not.
        private void End()
        {
            var id = pointerId;
            pointerId = -1;
            dragging = false;
            Unhook();
            root?.UnregisterCallback<PointerCaptureEvent>(OnAnyPointerCapture, TrickleDown.TrickleDown);
            root = null;
            if (id >= 0 && target != null && target.HasPointerCapture(id))
                target.ReleasePointer(id);
        }
    }
}
