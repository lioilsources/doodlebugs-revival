﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Cloud : MonoBehaviour
{
    public GameObject player;
    void OnTriggerEnter2D() {
        player.SendMessage("Flip");
    }
}
