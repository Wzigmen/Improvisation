using UnityEngine;

// Where things are in the world. Shared by the scene builder and the game code.
// The fight ring lives in the same scene as the start field, far away from it; "loading the arena" is a fade
// to black, a teleport there and a fade back in.
public static class GameLayout
{
    public static readonly Vector3 RingCenter = new Vector3(200f, 0f, 0f);
    public const float RingHalfSize = 12f;      // the ring is 24 x 24 m, the start field is 60 x 60 m
    const float RingSpawnRadius = 6f;

    // Fighters start on a circle around the middle of the ring, facing the centre.
    public static Vector3 RingSpawn(int index, int count)
    {
        float angle = (count > 0 ? index / (float)count : 0f) * Mathf.PI * 2f;
        return RingCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * RingSpawnRadius + Vector3.up * 0.1f;
    }

    public static float YawTowardsRingCenter(Vector3 from)
    {
        Vector3 d = RingCenter - from;
        d.y = 0f;
        return d.sqrMagnitude > 0.001f ? Quaternion.LookRotation(d).eulerAngles.y : 0f;
    }
}
