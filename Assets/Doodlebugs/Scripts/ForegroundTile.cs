using System.Collections;
using UnityEngine;

public class ForegroundTile : MonoBehaviour
{
    [HideInInspector] public int Col;
    [HideInInspector] public int Row;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.gameObject.CompareTag("Bullet")) return;

        // Destroy 2x2 square: hit tile + right + below + below-right
        int[,] offsets = { { 0, 0 }, { 1, 0 }, { 0, -1 }, { 1, -1 } };
        for (int i = 0; i < 4; i++)
        {
            int c = Col + offsets[i, 0];
            int r = Row + offsets[i, 1];
            if (i == 0)
            {
                LaunchDebris();
                continue;
            }
            var neighbor = transform.parent?.Find($"Tile_{c}_{r}");
            if (neighbor != null && neighbor.gameObject.activeSelf)
                neighbor.GetComponent<ForegroundTile>()?.LaunchDebris();
        }
    }

    public void LaunchDebris()
    {
        // Detach from the scrolling parent, keeping current world position
        transform.SetParent(null, true);
        GetComponent<Collider2D>().enabled = false;
        StartCoroutine(FlyAway());
    }

    private IEnumerator FlyAway()
    {
        float rotSpeed = Random.Range(180f, 450f) * (Random.value > 0.5f ? 1f : -1f);
        var velocity = new Vector3(
            Random.Range(-2f, 2f),
            Random.Range(5f, 10f),
            0f);

        float elapsed = 0f;
        const float duration = 4f; // enough to fly well above the camera (~35 world units)

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(0f, 0f, rotSpeed * Time.deltaTime);
            yield return null;
        }

        Destroy(gameObject);
    }
}
