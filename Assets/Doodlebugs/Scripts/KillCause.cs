/// <summary>
/// How a plane died - drives the kill-feed line format.
/// Serialized as a byte in SyncKillFeedClientRpc; keep values stable.
/// </summary>
public enum KillCause : byte
{
    Shot = 0,        // bullet kill (killer credited)
    Collision = 1,   // plane-vs-plane midair
    Ground = 2,      // crashed into ground/obstacle
    OutOfBounds = 3  // left the play area
}
