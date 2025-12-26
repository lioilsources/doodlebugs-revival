# Session 03 - Network Synchronization & Visual Improvements

## Summary

This session focused on fixing network synchronization issues, improving boundary wrapping logic, and adding visual distinction between host and client planes.

## Changes Made

### 1. Bullet Speed Based on Plane Speed (Shooting.cs)

Added plane velocity to bullet force:
```csharp
float planeSpeed = planeRb != null ? planeRb.velocity.magnitude : 0f;
ShootServerRpc(firePoint.position, firePoint.rotation, planeSpeed);

// In ServerRpc:
float totalForce = bulletForce + planeSpeed;
```

### 2. Fixed Network Teleport Interpolation (PlayerController.cs)

Problem: When plane wrapped from one edge to another, NetworkTransform interpolated the movement causing visible sweep across screen.

Solution: Use `NetworkTransform.Teleport()` to skip interpolation:
```csharp
NetworkTransform networkTransform;

// In teleport methods:
networkTransform.Teleport(newPos, transform.rotation, transform.localScale);
```

Important: Only owner can call Teleport() with ClientNetworkTransform (client authority):
```csharp
[ClientRpc]
private void WrapToPositionClientRpc(float targetX)
{
    if (!IsOwner) return;  // Only owner can teleport
    Vector3 newPos = new Vector3(targetX, transform.position.y, 0f);
    networkTransform.Teleport(newPos, transform.rotation, transform.localScale);
}
```

### 3. Fixed Boundary Wrapping Logic

Problem: Plane teleported too close to opposite boundary, immediately triggering another collision (infinite loop).

Solution: Use boundary collider bounds instead of position:
```csharp
private Collider2D leftBoundary;
private Collider2D rightBoundary;

// Hit left edge -> wrap to right side (position LEFT of right boundary's inner edge)
float targetX = rightBoundary.bounds.min.x - halfWidth - margin;

// Hit right edge -> wrap to left side (position RIGHT of left boundary's inner edge)
float targetX = leftBoundary.bounds.max.x + halfWidth + margin;
```

Also improved:
- Cached boundary references in Start() instead of GameObject.Find() on every collision
- Used `planeCollider.bounds.extents.x` to get plane half-width (accounts for rotation)

### 4. Network State Synchronization

Problem: Plane positions drifted between clients because game state wasn't synchronized.

Solution: Use NetworkVariables for critical state:
```csharp
private NetworkVariable<float> netSpeed = new NetworkVariable<float>(5f,
    NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
private NetworkVariable<bool> netEngineOff = new NetworkVariable<bool>(false,
    NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
private NetworkVariable<bool> netInSpace = new NetworkVariable<bool>(false,
    NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
private NetworkVariable<float> netGravity = new NetworkVariable<float>(0f,
    NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

// Property accessors for transparent usage:
private float speed
{
    get => netSpeed.Value;
    set => netSpeed.Value = value;
}
```

All ClientRpc methods that modify state now check `if (!IsOwner) return;`.

### 5. Out-of-Bounds Safety Check

Problem: Plane could escape all boundaries and never respawn.

Solution: Added safety check in FixedUpdate:
```csharp
private void CheckOutOfBounds()
{
    float safetyMargin = 50f;
    bool outOfBounds = false;

    if (leftBoundary != null && transform.position.x < leftBoundary.bounds.min.x - safetyMargin)
        outOfBounds = true;
    if (rightBoundary != null && transform.position.x > rightBoundary.bounds.max.x + safetyMargin)
        outOfBounds = true;
    // Only check lower bound - can fly into space freely
    if (transform.position.y < -safetyMargin)
        outOfBounds = true;

    if (outOfBounds)
        RequestRespawnServerRpc();
}
```

### 6. Color Distinction for Host/Client Planes

Created shader to replace red color with blue for client planes.

**New file: Assets/Doodlebugs/Shaders/ColorReplace.shader**
- Replaces pixels close to source color with target color
- Configurable threshold for color matching
- Smooth blending based on color distance

**PlayerController.cs changes:**
```csharp
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    SetPlaneColor();
}

private void SetPlaneColor()
{
    // Host keeps original colors, clients get red replaced with blue
    if (OwnerClientId != 0)
    {
        Shader colorReplaceShader = Shader.Find("Custom/ColorReplace");
        if (colorReplaceShader != null)
        {
            Material mat = new Material(colorReplaceShader);
            mat.SetColor("_SourceColor", Color.red);
            mat.SetColor("_TargetColor", Color.blue);
            mat.SetFloat("_Threshold", 0.4f);
            spriteRenderer.material = mat;
        }
    }
}
```

## Files Modified

- `Assets/Doodlebugs/Scripts/Shooting.cs` - Bullet speed based on plane speed
- `Assets/Doodlebugs/Scripts/PlayerController.cs` - Major networking and gameplay fixes

## Files Created

- `Assets/Doodlebugs/Shaders/ColorReplace.shader` - Color replacement shader for plane distinction

## Key Concepts

1. **NetworkTransform.Teleport()** - Skip interpolation for instant position changes
2. **NetworkVariable<T>** - Synchronize game state across network with owner write permission
3. **Collider2D.bounds** - Get actual collider bounds for accurate positioning
4. **ClientRpc + IsOwner check** - Only owner modifies NetworkVariables
5. **Shader color replacement** - Visual distinction without separate sprite assets
