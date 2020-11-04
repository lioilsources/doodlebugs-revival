using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlaygroundRight : MonoBehaviour
{
    public GameObject player;
    void OnTriggerEnter2D() {
        player.SendMessage("Flip");
    }
}
