using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The chrome's countdown pill: a Button that authors the timer it shows as a
    // UXML attribute, so Screen.uxml names the timer once and the code reads it
    // (12.11). A plain element drops attributes it does not declare, which is
    // why the attribute needs an element of its own.
    [UxmlElement]
    public partial class TimerPill : Button
    {
        // The id of the timer this pill counts down, as authored in Screen.uxml.
        [UxmlAttribute("timer")]
        public string Timer { get; set; }
    }
}
