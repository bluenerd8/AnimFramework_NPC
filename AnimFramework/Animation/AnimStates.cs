namespace AnimFramework.Animation
{
    /// <summary>
    /// State names the framework recognizes. These match the <c>"state"</c> values in a sheet
    /// layout JSON's <c>rows</c>. Use these instead of raw strings to avoid typos.
    /// </summary>
    public static class AnimStates
    {
        public const string Idle = "Idle";
        public const string Walking = "Walking";
        public const string Attacking = "Attacking";
        public const string Talk = "Talk";
        public const string Sitting = "Sitting";

        /// <summary>Occasional Once-mode variant played while idle, on a randomized interval.</summary>
        public const string SpecialIdle = "SpecialIdle";

        /// <summary>Once-mode blink played between Idle loops, triggered by a randomized loop count.</summary>
        public const string IdleBlink = "IdleBlink";

        /// <summary>
        /// Name for a state group prefix. With "Alert" active, Walking/Idle/IdleBlink/SpecialIdle
        /// resolve to AlertWalking/AlertIdle/AlertIdleBlink/AlertSpecialIdle when authored.
        /// </summary>
        public static string Grouped(string groupPrefix, string baseState)
        {
            return groupPrefix + baseState;
        }
    }

    /// <summary>Overlay names the framework recognizes. These match the <c>"state"</c> values in a sheet layout JSON's <c>overlays</c>.</summary>
    public static class AnimOverlays
    {
        /// <summary>Blink layered over any state on a randomized timer (seconds). Different from <see cref="AnimStates.IdleBlink"/>.</summary>
        public const string Blink = "Blink";
    }
}
