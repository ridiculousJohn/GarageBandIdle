using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Story
{
    // One chapter-boundary card (design doc 10, 12.11): declared on the chapter
    // whose boundary it tells, opened by a row's button or by its own mark, and
    // latched read by a flag homed far enough out that a chapter reset cannot
    // replay it.
    [CreateAssetMenu(menuName = "Garage Band Idle/Story Beat")]
    public class StoryBeatDefinition : Definition
    {
        // The card's body. Required: an empty one is a card with nothing on it
        // (12.12).
        public string text;

        // The gate the row's button reads (12.11): live when it holds. Never
        // null (12.12); Always is how an author says the button is live from a
        // fresh chapter.
        [SerializeReference, SubclassPicker] public Condition availableWhen;

        // The seen latch, a flag id resolved outward from the beat's declaring
        // scope exactly as SetFlag resolves (12.3); chapter 1 declares both of
        // its latches at root (section 10).
        public string seenFlag;

        // Opt-in per beat: only a marked beat opens its card by itself, and the
        // rule is state - available and unseen - never a transition, so a crash
        // with the card up shows it again (section 10). Popping is the
        // exception, so the default is false.
        public bool opensWhenAvailable;

        // Fail-closed, and the domain owns the gate - never the UI's visibility,
        // the same ruling an event's takes. ctx must be rebased to the declaring
        // scope, where the gate is authored.
        public bool IsAvailable(GameContext homeCtx) => availableWhen != null && availableWhen.Evaluate(homeCtx);
    }
}
