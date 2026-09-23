using UnityEngine;

// Where things are in the world. Shared by the scene builder and the game code.
// The fight ring lives in the same scene as the start field, far away from it; "loading the arena" is a fade
// to black, a teleport there and a fade back in.
public static class GameLayout
{
    public static readonly Vector3 RingCenter = new Vector3(200f, 0f, 0f);
    public const float RingHalfSize = 12f;      // the ring is 24 x 24 m, the start field is 60 x 60 m
    const float RingSpawnRadius = 6f;

    // Winners' podiums, a bit behind each color zone's pad (same numbers build the steps and stand on them, so
    // the two always agree - see CartoonPlayerBuilder.BuildPodium and MatchManager.PlacePodium).
    public static readonly Vector3 PlayersPodiumCenter = new Vector3(5f, 0f, 23f);
    public static readonly Vector3 BotsPodiumCenter = new Vector3(-5f, 0f, 23f);
    public static readonly float[] PodiumOffsetX = { 0f, -1.75f, 1.75f };   // 1st (middle), 2nd (left), 3rd (right)
    public static readonly float[] PodiumStepHeight = { 0.75f, 0.5f, 0.3f };

    // Where the winner of `place` (0 = first) stands on the podium, and which way they face: south, back towards
    // the field, the way they came from.
    public static void PodiumSpot(Vector3 center, int place, out Vector3 position, out float yaw)
    {
        int i = Mathf.Clamp(place, 0, PodiumOffsetX.Length - 1);
        position = center + new Vector3(PodiumOffsetX[i], PodiumStepHeight[i] + 0.1f, 0f);
        yaw = 180f;
    }

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
