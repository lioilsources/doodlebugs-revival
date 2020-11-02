using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Cloud : MonoBehaviour
{
    public GameObject player;
    void OnTriggerEnter2D() {
        //player.transform.rotation = Quaternion.Inverse(player.transform.rotation);
        player.transform.localRotation *= Quaternion.Euler(0, 180, 0);

        Debug.Log("Cloud OnTriggerEnter2D");
    }
}
