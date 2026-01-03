using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Manages dynamic cloud spawning and wrapping.
/// Auto-initializes - no scene setup required.
/// Spawns local clouds immediately, converts to networked when host/client connects.
/// </summary>
public class CloudManager : MonoBehaviour
{
    public static CloudManager Instance { get; private set; }

    [Header("Cloud Settings")]
    public int cloudCount = 3;
    public float minSpeed = 1f;
    public float maxSpeed = 3f;

    [Header("Cloud Scales (one per cloud)")]
    public float[] cloudScales = new float[] { 3f, 6f, 9f };

    [Header("Height Range")]
    public float minHeight = 5f;
    public float maxHeight = 15f;

    private List<Cloud> _clouds = new List<Cloud>();
    private List<GameObject> _localClouds = new List<GameObject>(); // Non-networked preview clouds
    private Collider2D _leftBoundary;
    private Collider2D _rightBoundary;
    private GameObject _cloudPrefab;
    private bool _networkedCloudsSpawned = false;
    private bool _localCloudsSpawned = false;

    /// <summary>
    /// Auto-initialize when scene loads
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInit()
    {
        if (Instance != null) return;

        var obj = new GameObject("CloudManager");
        Instance = obj.AddComponent<CloudManager>();
        DontDestroyOnLoad(obj);

        Debug.Log("[CloudManager] Auto-initialized");
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Cache boundary references
        StartCoroutine(WaitForBoundariesAndInit());
    }

    private System.Collections.IEnumerator WaitForBoundariesAndInit()
    {
        // Wait for boundaries to be created by ScreenSetup
        while (_leftBoundary == null || _rightBoundary == null)
        {
            var leftObj = GameObject.Find("Left");
            var rightObj = GameObject.Find("Right");

            if (leftObj != null) _leftBoundary = leftObj.GetComponent<Collider2D>();
            if (rightObj != null) _rightBoundary = rightObj.GetComponent<Collider2D>();

            yield return new WaitForSeconds(0.1f);
        }

        Debug.Log("[CloudManager] Boundaries found");

        // Find cloud prefab from NetworkManager's prefab list
        FindCloudPrefab();

        // Spawn local (non-networked) clouds immediately
        if (!_localCloudsSpawned)
        {
            SpawnLocalClouds();
        }

        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            NetworkManager.Singleton.OnClientStarted += OnClientStarted;
        }
    }

    private void FindCloudPrefab()
    {
        // Try to find existing cloud in scene first
        var existingCloud = FindObjectOfType<Cloud>();
        if (existingCloud != null)
        {
            // Can't get prefab from instance easily, but we can use this as template
            Debug.Log("[CloudManager] Found existing cloud in scene");
        }

        // Find from NetworkManager's prefab list
        if (NetworkManager.Singleton != null)
        {
            foreach (var prefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (prefab.Prefab != null && prefab.Prefab.name.Contains("Cloud"))
                {
                    _cloudPrefab = prefab.Prefab;
                    Debug.Log($"[CloudManager] Found cloud prefab: {_cloudPrefab.name}");
                    break;
                }
            }
        }

        if (_cloudPrefab == null)
        {
            Debug.LogError("[CloudManager] Cloud prefab not found in NetworkPrefabsList!");
        }
    }

    private void OnServerStarted()
    {
        Debug.Log("[CloudManager] Server started - converting to networked clouds");

        // Destroy local preview clouds
        DestroyLocalClouds();

        // Spawn networked clouds
        if (!_networkedCloudsSpawned)
        {
            StartCoroutine(DelayedNetworkedSpawn());
        }
    }

    private void OnClientStarted()
    {
        Debug.Log("[CloudManager] Client started - removing local clouds (server will sync)");

        // Destroy local preview clouds - server's clouds will appear via network
        DestroyLocalClouds();
    }

    private System.Collections.IEnumerator DelayedNetworkedSpawn()
    {
        yield return new WaitForSeconds(0.2f);

        if (!_networkedCloudsSpawned && NetworkManager.Singleton.IsServer)
        {
            SpawnNetworkedClouds();
        }
    }

    /// <summary>
    /// Spawn local (non-networked) clouds for immediate visual feedback
    /// </summary>
    private void SpawnLocalClouds()
    {
        if (_cloudPrefab == null)
        {
            Debug.LogWarning("[CloudManager] Cannot spawn local clouds - prefab not found");
            return;
        }

        _localCloudsSpawned = true;

        float leftX = _leftBoundary.bounds.min.x;
        float rightX = _rightBoundary.bounds.max.x;

        for (int i = 0; i < cloudCount; i++)
        {
            float heightPercent = cloudCount > 1 ? (float)i / (cloudCount - 1) : 0.5f;
            float y = Mathf.Lerp(minHeight, maxHeight, heightPercent);
            float x = Random.Range(leftX, rightX);

            var cloud = Instantiate(_cloudPrefab, new Vector3(x, y, 0), Quaternion.identity);

            // Remove NetworkObject and Cloud components for local preview (they interfere without network)
            var netObj = cloud.GetComponent<NetworkObject>();
            if (netObj != null) Destroy(netObj);
            var cloudScript = cloud.GetComponent<Cloud>();
            if (cloudScript != null) Destroy(cloudScript);

            // Add local movement component
            var localCloud = cloud.AddComponent<LocalCloudMovement>();
            localCloud.speed = Random.Range(minSpeed, maxSpeed);

            // Use predefined scales
            float scale = (i < cloudScales.Length) ? cloudScales[i] : cloudScales[cloudScales.Length - 1];
            cloud.transform.localScale = Vector3.one * scale;

            Debug.Log($"[CloudManager] Local cloud {i}: scale={scale}, speed={localCloud.speed}");

            _localClouds.Add(cloud);
        }

        Debug.Log($"[CloudManager] Spawned {cloudCount} local preview clouds");
    }

    private void DestroyLocalClouds()
    {
        foreach (var cloud in _localClouds)
        {
            if (cloud != null)
            {
                Destroy(cloud);
            }
        }
        _localClouds.Clear();
        _localCloudsSpawned = false;
        Debug.Log("[CloudManager] Destroyed local clouds");
    }

    private void SpawnNetworkedClouds()
    {
        if (_cloudPrefab == null)
        {
            Debug.LogError("[CloudManager] Cannot spawn clouds - prefab not found");
            return;
        }

        if (_leftBoundary == null || _rightBoundary == null)
        {
            Debug.LogError("[CloudManager] Cannot spawn clouds - boundaries not found");
            return;
        }

        _networkedCloudsSpawned = true;

        // Get spawn range
        float leftX = _leftBoundary.bounds.min.x;
        float rightX = _rightBoundary.bounds.max.x;

        // Spawn clouds at different heights
        for (int i = 0; i < cloudCount; i++)
        {
            // Distribute heights evenly
            float heightPercent = cloudCount > 1 ? (float)i / (cloudCount - 1) : 0.5f;
            float y = Mathf.Lerp(minHeight, maxHeight, heightPercent);

            // Random X position across screen
            float x = Random.Range(leftX, rightX);

            // Use predefined scales
            float scale = (i < cloudScales.Length) ? cloudScales[i] : cloudScales[cloudScales.Length - 1];

            SpawnNetworkedCloud(x, y, scale);
        }

        Debug.Log($"[CloudManager] Spawned {cloudCount} networked clouds");
    }

    private void SpawnNetworkedCloud(float x, float y, float scale)
    {
        var cloud = Instantiate(_cloudPrefab, new Vector3(x, y, 0), Quaternion.identity);

        // Add Cloud script BEFORE spawning (so OnNetworkSpawn gets called)
        var cloudScript = cloud.GetComponent<Cloud>();
        if (cloudScript == null)
        {
            cloudScript = cloud.AddComponent<Cloud>();
        }

        // Random speed, but scale is passed in for even distribution
        float speed = Random.Range(minSpeed, maxSpeed);

        // Set pending values before spawn
        cloudScript.Initialize(speed, scale);
        Debug.Log($"[CloudManager] Networked cloud: scale={scale}, speed={speed}");

        // Now spawn the network object
        var netObj = cloud.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(true); // destroyWithScene = true
        }

        _clouds.Add(cloudScript);
        Debug.Log($"[CloudManager] Spawned networked cloud at ({x}, {y}) with speed={speed}, scale={scale}");
    }

    /// <summary>
    /// Called by Cloud when it reaches the right edge
    /// </summary>
    public void OnCloudReachedRightEdge(Cloud cloud)
    {
        if (_leftBoundary == null) return;

        // New random speed and scale from predefined scales
        float newScale = cloudScales[Random.Range(0, cloudScales.Length)];

        // Get sprite half width for positioning
        float spriteHalfWidth = 1f;
        var sr = cloud.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            spriteHalfWidth = sr.sprite.bounds.extents.x;
        }

        // Position so right edge of cloud is just outside left boundary
        float leftX = _leftBoundary.bounds.min.x - (spriteHalfWidth * newScale);
        cloud.SetPosition(new Vector3(leftX, cloud.transform.position.y, 0));

        cloud.Initialize(
            Random.Range(minSpeed, maxSpeed),
            newScale
        );

        Debug.Log($"[CloudManager] Cloud wrapped to left edge at y={cloud.transform.position.y}, scale={newScale}");
    }

    /// <summary>
    /// Get position of a random cloud for player respawn
    /// </summary>
    public Vector3 GetRandomCloudPosition()
    {
        if (_clouds.Count == 0)
        {
            // Fallback - return a position in cloud zone
            return new Vector3(0, (minHeight + maxHeight) / 2f, 0);
        }

        // Pick random cloud
        var cloud = _clouds[Random.Range(0, _clouds.Count)];
        return cloud.transform.position;
    }

    /// <summary>
    /// Check if clouds are spawned and ready
    /// </summary>
    public bool AreCloudsReady()
    {
        return _networkedCloudsSpawned && _clouds.Count > 0;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientStarted -= OnClientStarted;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}

/// <summary>
/// Simple movement component for non-networked preview clouds.
/// Moves the cloud to the right and wraps at screen edge.
/// </summary>
public class LocalCloudMovement : MonoBehaviour
{
    public float speed = 2f;

    private Collider2D _rightBoundary;
    private Collider2D _leftBoundary;
    private float _spriteHalfWidth = 1f;

    void Start()
    {
        var rightObj = GameObject.Find("Right");
        if (rightObj != null)
        {
            _rightBoundary = rightObj.GetComponent<Collider2D>();
        }

        var leftObj = GameObject.Find("Left");
        if (leftObj != null)
        {
            _leftBoundary = leftObj.GetComponent<Collider2D>();
        }

        // Cache sprite half width (at scale 1)
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            _spriteHalfWidth = sr.sprite.bounds.extents.x;
        }
    }

    void Update()
    {
        // Move right
        transform.position += Vector3.right * speed * Time.deltaTime;

        // Wrap when left edge of cloud passes right boundary
        if (_rightBoundary != null)
        {
            float currentScale = transform.localScale.x;
            float cloudLeftEdge = transform.position.x - (_spriteHalfWidth * currentScale);
            float rightBoundaryX = _rightBoundary.bounds.max.x;

            if (cloudLeftEdge > rightBoundaryX && _leftBoundary != null)
            {
                // Spawn at left edge with right edge of cloud just outside view
                float newScale = currentScale;
                if (CloudManager.Instance != null && CloudManager.Instance.cloudScales.Length > 0)
                {
                    newScale = CloudManager.Instance.cloudScales[Random.Range(0, CloudManager.Instance.cloudScales.Length)];
                    transform.localScale = Vector3.one * newScale;
                }

                float leftX = _leftBoundary.bounds.min.x - (_spriteHalfWidth * newScale);
                transform.position = new Vector3(leftX, transform.position.y, 0);

                // Randomize speed on wrap
                speed = Random.Range(1f, 3f);
            }
        }
    }
}
