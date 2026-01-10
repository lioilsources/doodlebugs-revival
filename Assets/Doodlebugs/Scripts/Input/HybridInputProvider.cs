using UnityEngine;

/// <summary>
/// Hybrid input provider that combines keyboard and gamepad input for a single player.
/// Gamepad input takes priority when detected, otherwise falls back to keyboard.
/// Each player gets their own keyboard bindings and gamepad index:
/// - P1: Gamepad.all[0] + A/D/W/S/Space
/// - P2: Gamepad.all[1] + J/L/I/K/H
/// - P3: Gamepad.all[2] + Arrows/RCtrl
/// - P4: Gamepad.all[3] + O/P/[/]/\
/// </summary>
public class HybridInputProvider : IInputProvider
{
    private DesktopInputProvider keyboard;
    private GamepadInputProvider gamepad;
    private int playerIndex;

    // Threshold for considering gamepad input as "active"
    private const float GAMEPAD_ACTIVE_THRESHOLD = 0.1f;

    public HybridInputProvider(int playerIndex)
    {
        this.playerIndex = playerIndex;
        keyboard = new DesktopInputProvider(playerIndex);
        gamepad = new GamepadInputProvider(playerIndex);
    }

    public float GetHorizontalInput()
    {
        // Check gamepad first - if it has input, use it
        float gpInput = gamepad.GetHorizontalInput();
        if (Mathf.Abs(gpInput) > GAMEPAD_ACTIVE_THRESHOLD)
            return gpInput;

        // Fall back to keyboard
        return keyboard.GetHorizontalInput();
    }

    public float GetVerticalInput()
    {
        // Check gamepad first - if it has input, use it
        float gpInput = gamepad.GetVerticalInput();
        if (Mathf.Abs(gpInput) > GAMEPAD_ACTIVE_THRESHOLD)
            return gpInput;

        // Fall back to keyboard
        return keyboard.GetVerticalInput();
    }

    public bool GetShootInput()
    {
        // Check both - either can trigger shoot
        return gamepad.GetShootInput() || keyboard.GetShootInput();
    }

    public void UpdateInput()
    {
        // Update both providers
        keyboard.UpdateInput();
        gamepad.UpdateInput();
    }
}
