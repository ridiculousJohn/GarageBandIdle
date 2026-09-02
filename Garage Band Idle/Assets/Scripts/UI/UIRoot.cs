using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The one UI object in the Boot scene (design doc 12.11): it holds the
    // UIDocument and is bound by GameManager from Start, never from the boot
    // path - boot runs in Awake, every Awake precedes every OnEnable, and the
    // UIDocument builds its tree in its own OnEnable. No Update either: the
    // driver's one Update drives Interpolate through here, so nothing depends
    // on script execution order.
    public class UIRoot : MonoBehaviour
    {
        [SerializeField] private UIDocument document;

        private ScreenHost host;

        public void Bind(GameSession session, ModuleRegistry registry, GameClock clock)
        {
            host = new ScreenHost(document.rootVisualElement, registry, session, clock);
            host.Render();          // unconditional, because a fresh game runs no transaction
        }

        public void Interpolate() => host?.Interpolate();

        private void OnDestroy() => host?.Dispose();
    }
}
