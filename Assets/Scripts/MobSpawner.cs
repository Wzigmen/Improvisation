using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Host only: keeps a few slimes around and brings back any that get knocked out.
public class MobSpawner : MonoBehaviour
{
    public static MobSpawner Instance { get; private set; }

    [SerializeField] GameObject mobPrefab;
    [SerializeField] Vector3[] homes;
    [SerializeField] float respawnDelay = 6f;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void OnHostStarted()
    {
        foreach (var home in homes) Spawn(home);
    }

    public void StopAll() => StopAllCoroutines();

    public void ScheduleRespawn(Vector3 home) => StartCoroutine(RespawnRoutine(home));

    IEnumerator RespawnRoutine(Vector3 home)
    {
        yield return new WaitForSeconds(respawnDelay);
        // The game may have ended while we were waiting.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkGame.InGame)
            Spawn(home);
    }

    void Spawn(Vector3 home)
    {
        var go = Instantiate(mobPrefab, home + Vector3.up * 0.2f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        go.GetComponent<Mob>().Home = home;
        go.GetComponent<NetworkObject>().Spawn(true);
    }
}
