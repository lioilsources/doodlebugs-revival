using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Desktop input using keyboard with support for multiple local players.
/// Each player has unique key bindings:
/// P1: A/D, W/S, Space
/// P2: J/L, I/K, H
/// P3: Arrows, / (slash)
/// P4: O/P, [/], ; (semicolon)
/// </summary>
public class DesktopInputProvider : IInputProvider
{
    // Smoothing settings
    private float smoothingSpeed = 2f;

    // Current smoothed values
    private float smoothedHorizontal = 0f;
    private float smoothedVertical = 0f;

    // Player index (0-3)
    private int playerIndex;

    // Key bindings for this player
    private Key leftKey;
    private Key rightKey;
    private Key upKey;
    private Key downKey;
    private Key shootKey;

    public DesktopInputProvider(int playerIndex = 0)
    {
        this.playerIndex = playerIndex;
        SetupKeysForPlayer(playerIndex);
    }

    private void SetupKeysForPlayer(int index)
    {
        switch (index)
        {
            case 0: // P1: A/D, W/S, Space
                leftKey = Key.A;
                rightKey = Key.D;
                upKey = Key.W;
                downKey = Key.S;
                shootKey = Key.Space;
                break;

            case 1: // P2: J/L, I/K, H
                leftKey = Key.J;
                rightKey = Key.L;
                upKey = Key.I;
                downKey = Key.K;
                shootKey = Key.H;
                break;

            case 2: // P3: Arrows, / (slash)
                leftKey = Key.LeftArrow;
                rightKey = Key.RightArrow;
                upKey = Key.UpArrow;
                downKey = Key.DownArrow;
                shootKey = Key.Slash;
                break;

            case 3: // P4: O/P, [/], ; (semicolon)
                leftKey = Key.O;
                rightKey = Key.P;
                upKey = Key.LeftBracket;
                downKey = Key.RightBracket;
                shootKey = Key.Semicolon;
                break;

            default:
                Debug.LogWarning($"[DesktopInputProvider] Invalid player index: {index}, using P1 defaults");
                leftKey = Key.A;
                rightKey = Key.D;
                upKey = Key.W;
                downKey = Key.S;
                shootKey = Key.Space;
                break;
        }

        Debug.Log($"[DesktopInputProvider] P{index + 1} keys: {leftKey}/{rightKey}, {upKey}/{downKey}, shoot={shootKey}");
    }

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
        var keyboard = Keyboard.current;
        if (keyboard == null) return false;

        return keyboard[shootKey].wasPressedThisFrame;
    }

    public void UpdateInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Get raw input from assigned keys
        float targetHorizontal = 0f;
        float targetVertical = 0f;

        if (keyboard[leftKey].isPressed) targetHorizontal -= 1f;
        if (keyboard[rightKey].isPressed) targetHorizontal += 1f;
        if (keyboard[upKey].isPressed) targetVertical += 1f;
        if (keyboard[downKey].isPressed) targetVertical -= 1f;

        // Smoothly move towards target
        smoothedHorizontal = Mathf.MoveTowards(smoothedHorizontal, targetHorizontal, smoothingSpeed * Time.deltaTime);
        smoothedVertical = Mathf.MoveTowards(smoothedVertical, targetVertical, smoothingSpeed * Time.deltaTime);
    }
}
