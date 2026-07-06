using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Controls visual effects for the player plane:
/// - Soft glow outline (owner-only visibility)
/// - Expert gold sparkles (visible to all)
/// - Damage flash on hit (visible to all)
/// </summary>
public class PlaneVisualEffects : NetworkBehaviour
{
    [Header("Glow Outline")]
    [SerializeField] private SpriteRenderer glowOutlineRenderer;
    [SerializeField] private Color ownerGlowColor = Color.white;

    [Header("Expert Sparkles")]
    [SerializeField] private ParticleSystem sparkleParticles;
    [SerializeField] private float orbitRadius = 0.8f;
    [SerializeField] private float orbitSpeed = 2f;
    [SerializeField] private Color sparkleColor = new Color(1f, 0.85f, 0f, 1f); // Gold

    [Header("Damage Flash")]
    [SerializeField] private SpriteRenderer planeRenderer;
    [SerializeField] private float flashDuration = 0.2f;

    // NetworkVariable to sync expert status to all clients
    private NetworkVariable<bool> isExpert = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Material planeMaterialInstance;
    private Coroutine flashCoroutine;
    private Coroutine invulnerabilityBlinkCoroutine;
    private PlayerController playerController;
    private PlaneStats planeStats;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Get PlayerController reference first (needed for glow visibility check)
        playerController = GetComponent<PlayerController>();

        // Glow outline: only for network clients, not local couch co-op players
        UpdateGlowVisibility();

        // Setup damage flash material
        SetupDamageFlashMaterial();

        // Configure sparkle particles for orbiting effect
        ConfigureSparkleParticles();

        // Cache PlaneStats reference
        planeStats = GetComponent<PlaneStats>();

        // Subscribe to invulnerability changes for blink effect
        if (planeStats != null)
        {
            planeStats.NetInvulnerable.OnValueChanged += OnInvulnerabilityChanged;
        }

        // Subscribe to expert status changes
        isExpert.OnValueChanged += OnExpertStatusChanged;

        // If owner, subscribe to player's maturity changes (not global)
        if (IsOwner && playerController != null)
        {
            playerController.OnMaturityChanged += HandleLevelChange;
            // Check initial expert status
            RefreshExpertStatus();
        }

        // Apply initial expert status
        UpdateSparkleVisibility(isExpert.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Cleanup subscriptions
        isExpert.OnValueChanged -= OnExpertStatusChanged;

        if (planeStats != null)
        {
            planeStats.NetInvulnerable.OnValueChanged -= OnInvulnerabilityChanged;
        }

        if (IsOwner && playerController != null)
        {
            playerController.OnMaturityChanged -= HandleLevelChange;
        }
    }

    private void ConfigureSparkleParticles()
    {
        if (sparkleParticles == null) return;

        // Main module - basic settings
        var main = sparkleParticles.main;
        main.startSpeed = 0f; // No initial velocity - orbit handles movement
        main.startLifetime = 2f;
        main.startSize = 0.15f;
        main.startColor = sparkleColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 20;

        // Emission - steady stream of particles
        var emission = sparkleParticles.emission;
        emission.rateOverTime = 8f;

        // Shape - emit from circle edge
        var shape = sparkleParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = orbitRadius;
        shape.radiusThickness = 0f; // Emit only from edge

        // Velocity over lifetime - orbital motion
        var velocityOverLifetime = sparkleParticles.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
        velocityOverLifetime.orbitalZ = orbitSpeed; // Orbit around Z axis (2D plane)
        velocityOverLifetime.radial = 0f; // No radial movement

        // Size over lifetime - fade out
        var sizeOverLifetime = sparkleParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(0.7f, 1f);
        sizeCurve.AddKey(1f, 0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Color over lifetime - golden glow with fade
        var colorOverLifetime = sparkleParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(sparkleColor, 0f),
                new GradientColorKey(sparkleColor, 0.8f),
                new GradientColorKey(sparkleColor, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = gradient;
    }

    private void SetupDamageFlashMaterial()
    {
        if (planeRenderer == null) return;

        // Check if the plane already has a material with DamageFlash shader
        // If not, we'll animate using SpriteRenderer.color instead
        Shader damageFlashShader = Shader.Find("Custom/DamageFlash");
        if (damageFlashShader != null && planeRenderer.material.shader.name != "Custom/ColorReplace")
        {
            // Only create new material if not using ColorReplace shader
            planeMaterialInstance = new Material(damageFlashShader);
            planeMaterialInstance.SetTexture("_MainTex", planeRenderer.sprite.texture);
            planeRenderer.material = planeMaterialInstance;
        }
        else
        {
            // Keep existing material, we'll use color flash fallback
            planeMaterialInstance = planeRenderer.material;
        }
    }

    /// <summary>
    /// Update glow outline visibility. Called on spawn and when local player index changes.
    /// </summary>
    public void UpdateGlowVisibility()
    {
        if (glowOutlineRenderer != null)
        {
            // Only show outline for network clients (not local couch co-op players)
            // Local players: playerController.IsLocalPlayer = true → no outline
            // Network clients: playerController.IsLocalPlayer = false → white outline
            bool isLocalPlayer = playerController != null && playerController.IsLocalPlayer;
            bool showOutline = IsOwner && !isLocalPlayer;

            glowOutlineRenderer.enabled = showOutline;
            if (showOutline)
            {
                glowOutlineRenderer.color = ownerGlowColor;
            }
        }
    }

    private void HandleLevelChange(PilotMaturityLevel newLevel)
    {
        Debug.Log($"[PlaneVisualEffects] HandleLevelChange: {newLevel}, IsOwner={IsOwner}");
        if (IsOwner)
        {
            bool expert = newLevel == PilotMaturityLevel.Expert;
            Debug.Log($"[PlaneVisualEffects] Sending expert status: {expert}");
            UpdateExpertStatusServerRpc(expert);
        }
    }

    private void RefreshExpertStatus()
    {
        if (playerController != null)
        {
            bool expert = playerController.MaturityLevel == PilotMaturityLevel.Expert;
            UpdateExpertStatusServerRpc(expert);
        }
    }

    [ServerRpc]
    private void UpdateExpertStatusServerRpc(bool expert)
    {
        isExpert.Value = expert;
    }

    private void OnExpertStatusChanged(bool previousValue, bool newValue)
    {
        UpdateSparkleVisibility(newValue);
    }

    private void UpdateSparkleVisibility(bool expert)
    {
        Debug.Log($"[PlaneVisualEffects] UpdateSparkleVisibility: expert={expert}, sparkleParticles={sparkleParticles != null}");
        if (sparkleParticles != null)
        {
            if (expert)
            {
                Debug.Log("[PlaneVisualEffects] Playing sparkle particles!");
                sparkleParticles.Play();
            }
            else
            {
                sparkleParticles.Stop();
            }
        }
        else
        {
            Debug.LogWarning("[PlaneVisualEffects] sparkleParticles is NULL - cannot show expert effect!");
        }
    }

    /// <summary>
    /// Triggers the damage flash effect. Call this from PlayerController when hit.
    /// Only server should call this method.
    /// </summary>
    /// <param name="shieldHit">True when the shield absorbed the whole hit (different sound).</param>
    public void TriggerDamageFlash(bool shieldHit = false)
    {
        if (!IsServer) return;
        TriggerDamageFlashClientRpc(shieldHit);
    }

    [ClientRpc]
    private void TriggerDamageFlashClientRpc(bool shieldHit)
    {
        if (shieldHit) SfxManager.PlayShieldHit();
        else SfxManager.PlayHullHit();

        // Stop any existing flash
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }
        flashCoroutine = StartCoroutine(DamageFlashCoroutine());
    }

    private void OnInvulnerabilityChanged(bool prev, bool current)
    {
        if (current)
        {
            if (invulnerabilityBlinkCoroutine != null)
                StopCoroutine(invulnerabilityBlinkCoroutine);
            invulnerabilityBlinkCoroutine = StartCoroutine(InvulnerabilityBlinkCoroutine());
        }
        else
        {
            if (invulnerabilityBlinkCoroutine != null)
            {
                StopCoroutine(invulnerabilityBlinkCoroutine);
                invulnerabilityBlinkCoroutine = null;
            }
            // Ensure sprite is visible
            if (planeRenderer != null)
                planeRenderer.enabled = true;
        }
    }

    private IEnumerator InvulnerabilityBlinkCoroutine()
    {
        if (planeRenderer == null) yield break;

        float blinkRate = 10f; // blinks per second
        while (planeStats != null && planeStats.IsInvulnerable)
        {
            planeRenderer.enabled = !planeRenderer.enabled;
            yield return new WaitForSeconds(1f / (blinkRate * 2f));
        }

        planeRenderer.enabled = true;
        invulnerabilityBlinkCoroutine = null;
    }

    private IEnumerator DamageFlashCoroutine()
    {
        if (planeRenderer == null) yield break;

        float halfDuration = flashDuration / 2f;
        Color originalColor = planeRenderer.color;

        // Check if we can use shader-based flash
        bool useShaderFlash = planeMaterialInstance != null &&
                              planeMaterialInstance.HasProperty("_FlashAmount");

        if (useShaderFlash)
        {
            // Shader-based flash (white overlay)
            // Flash in (0 → 1)
            for (float t = 0; t < halfDuration; t += Time.deltaTime)
            {
                float amount = t / halfDuration;
                planeMaterialInstance.SetFloat("_FlashAmount", amount);
                yield return null;
            }
            planeMaterialInstance.SetFloat("_FlashAmount", 1f);

            // Flash out (1 → 0)
            for (float t = 0; t < halfDuration; t += Time.deltaTime)
            {
                float amount = 1f - (t / halfDuration);
                planeMaterialInstance.SetFloat("_FlashAmount", amount);
                yield return null;
            }
            planeMaterialInstance.SetFloat("_FlashAmount", 0f);
        }
        else
        {
            // Fallback: Color-based flash (works with any shader)
            Color flashColor = Color.white;

            // Flash in
            for (float t = 0; t < halfDuration; t += Time.deltaTime)
            {
                float amount = t / halfDuration;
                planeRenderer.color = Color.Lerp(originalColor, flashColor, amount);
                yield return null;
            }
            planeRenderer.color = flashColor;

            // Flash out
            for (float t = 0; t < halfDuration; t += Time.deltaTime)
            {
                float amount = t / halfDuration;
                planeRenderer.color = Color.Lerp(flashColor, originalColor, amount);
                yield return null;
            }
            planeRenderer.color = originalColor;
        }

        flashCoroutine = null;
    }
}
