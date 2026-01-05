using UnityEngine;

/// <summary>
/// Desktop input using keyboard (arrows/WASD for rotation, Space for shoot)
/// Custom smoothing for gradual input changes (more variability than Unity's default)
/// </summary>
public class DesktopInputProvider : IInputProvider
{
    // Smoothing settings
    private float smoothingSpeed = 2f;  // How fast input reaches target (lower = more gradual)

    // Current smoothed values
    private float smoothedHorizontal = 0f;
    private float smoothedVertical = 0f;

    public float GetHorizontalInput()
    {
        return smoothedHorizontal;
    }

    public float GetVerticalInput()
    {
        return smoothedVertical;
    }

    public bool GetShootInput()
    {
        return Input.GetKeyDown(KeyCode.Space);
    }

    public void UpdateInput()
    {
        // Get raw input (-1, 0, or 1)
        float targetHorizontal = Input.GetAxisRaw("Horizontal");
        float targetVertical = Input.GetAxisRaw("Vertical");

        // Smoothly move towards target
        smoothedHorizontal = Mathf.MoveTowards(smoothedHorizontal, targetHorizontal, smoothingSpeed * Time.deltaTime);
        smoothedVertical = Mathf.MoveTowards(smoothedVertical, targetVertical, smoothingSpeed * Time.deltaTime);
    }
}
