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
}
