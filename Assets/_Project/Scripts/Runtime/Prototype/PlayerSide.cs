namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Which side of the arena a puppet belongs to. Phase 1.1 only uses this to
    /// mirror the rope/pulley layout and the camera; the puppet body itself and
    /// the "left rope / right rope" input semantics stay tied to the puppet's own
    /// anatomy regardless of side, so a future PvP setup can drop a Right-side
    /// puppet in without rewriting the control code.
    /// </summary>
    public enum PlayerSide
    {
        Left = 0,
        Right = 1,
    }

    public static class PlayerSideExtensions
    {
        /// <summary>-1 for Left (rope layout hangs off the -X screen edge), +1 for Right.</summary>
        public static float Sign(this PlayerSide side) => side == PlayerSide.Left ? -1f : 1f;
    }
}
