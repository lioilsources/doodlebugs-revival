﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Space : MonoBehaviour
{
    public GameObject player;
    void OnTriggerEnter2D(Collider2D other) {
        // if (other.gameObject.name == "Plane") {
        //     player.SendMessage("EngineOff");
        // }
    }
}
