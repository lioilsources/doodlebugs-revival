using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class SingletonNetwork<T> : NetworkBehaviour where T : Component
{
    [SerializeField]
    public static T Instance { get; private set; }

    public virtual void Awake()
    {
        if (Instance == null)
        {
            Instance = this as T;
        }
        else
        {
            Destroy(gameObject);
        }
    }
}

public class GameManager : SingletonNetwork<GameManager>
{
    public GameObject birdPrefab;

    [ServerRpc]
    public void SpawnBirdServerRpc()
    {
        Debug.Log($"GameManager.SpawnBird {OwnerClientId}");
        //NetworkObjectSpawner.SpawnNewNetworkObject(birdPrefab, birdPrefab.transform.position);

        var bullet = Instantiate(birdPrefab, birdPrefab.transform.position, birdPrefab.transform.rotation);
    }
}
