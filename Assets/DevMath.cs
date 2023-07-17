using UnityEngine;

// internal class DevMath
// {
// }

public static class DevMath {
    // calculate direction = destination - source
    // Vector3 direction = enemy.position - transform.position

    public static Vector2 CalculateDirection(Vector2 destination, Vector2 source) {
        var direction = destination - source;
        return direction;
    }
}
