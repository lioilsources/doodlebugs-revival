using UnityEngine;

public class ForegroundTile : MonoBehaviour
{
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.CompareTag("Bullet"))
            gameObject.SetActive(false);
    }
}
