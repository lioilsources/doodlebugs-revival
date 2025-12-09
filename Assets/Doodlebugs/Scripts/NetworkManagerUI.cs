using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
// using UnityEngine.UIElements; // unused

public class NetworkManagerUI : MonoBehaviour
{ 
    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
    }

    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        NetworkManager.Singleton.StartClient();
    }
}
