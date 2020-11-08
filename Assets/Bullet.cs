using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Bullet Prefab script
public class Bullet : MonoBehaviour
{
    public GameObject hitEffect;

    private void Start() {
        Debug.Log("Bullet");
    }

    void OnTriggerEnter2D(Collider2D other) {
        if (other.gameObject.name != "Plane" && other.gameObject.name != "Space") {
            var effect = Instantiate(hitEffect, transform.position, Quaternion.identity);
            Destroy(effect, 0.8f);
            Destroy(gameObject);
        }

        Debug.Log($"Bullet trigger {other}");
    }
}
