using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class NetworkManagerUI : MonoBehaviour
{
    //public Button serverBtn;
    //public Button hostBtn;
    //public Button clientBtn;

    //private void Awake()
    //{
    //    serverBtn.clicked += () =>
    //    {
    //        NetworkManager.Singleton.StartServer();
    //    };

    //    hostBtn.clicked += () =>
    //    {
    //        NetworkManager.Singleton.StartHost();
    //    };

    //    clientBtn.clicked += () =>
    //    {
    //        NetworkManager.Singleton.StartClient();
    //    };
    //}

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
