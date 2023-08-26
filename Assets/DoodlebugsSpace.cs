using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DoodlebugsSpace : MonoBehaviour
{
    public GameObject bird;
    void OnTriggerEnter2D(Collider2D other) {
        if (other.gameObject.name == "Bird") {
            bird.SendMessage("EngineOff");
        }
    }
}
