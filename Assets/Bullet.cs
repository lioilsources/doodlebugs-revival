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

    // not triggered why?
    void OnCollisionEnter2D(Collision2D other) {
        var effect = Instantiate(hitEffect, transform.position, Quaternion.identity);
        Destroy(effect, 0.8f);
        Destroy(gameObject);

        Debug.Log("Bullet collision");
    }

    void OnTriggerEnter2D(Collider2D other) {

        Debug.Log($"{other.GetComponent<GameObject>().GetInstanceID()}");
        Debug.Log($"{GameObject.Find("Cloud").GetInstanceID()}");

        if (other.gameObject.name != "Plane") {
            var effect = Instantiate(hitEffect, transform.position, Quaternion.identity);
            Destroy(effect, 0.8f);
            Destroy(gameObject);
        }

        Debug.Log($"Bullet trigger {other}");
    }
}
