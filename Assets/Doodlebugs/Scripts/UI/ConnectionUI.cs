using Doodlebugs.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Doodlebugs.UI
{
    public class ConnectionUI : MonoBehaviour
    {
        [SerializeField] private Text _statusText;
        [SerializeField] private GameObject _statusPanel;
        [SerializeField] private float _hideDelay = 2f;

        private ConnectionManager _connectionManager;

        private void Start()
        {
            _connectionManager = ConnectionManager.Instance;

            if (_connectionManager == null)
            {
                Debug.LogError("[ConnectionUI] ConnectionManager.Instance is null!");
                return;
            }

            _connectionManager.OnStatusMessage += OnStatusMessage;
            _connectionManager.OnStateChanged += OnStateChanged;
            Debug.Log("[ConnectionUI] Subscribed to ConnectionManager events");

            // Setup panel - anchor to bottom-left
            if (_statusPanel != null)
            {
                _statusPanel.SetActive(true);

                var panelRect = _statusPanel.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    // Anchor to bottom-left corner
                    panelRect.anchorMin = new Vector2(0, 0);
                    panelRect.anchorMax = new Vector2(0, 0);
                    panelRect.pivot = new Vector2(0, 0);

                    // Use safe area to avoid notch/rounded corners
                    Rect safeArea = Screen.safeArea;
                    float leftPadding = safeArea.x + 20; // Safe area offset + padding
                    float bottomPadding = safeArea.y + 20;

                    panelRect.anchoredPosition = new Vector2(leftPadding, bottomPadding);
                    panelRect.sizeDelta = new Vector2(500, 50);

                    Debug.Log($"[ConnectionUI] SafeArea: {safeArea}, Panel pos: ({leftPadding}, {bottomPadding})");
                }

                var image = _statusPanel.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color(0, 0, 0, 0.7f);
                }
            }

            // Setup text - stretch to fill panel
            if (_statusText != null)
            {
                var textRect = _statusText.GetComponent<RectTransform>();
                if (textRect != null)
                {
                    // Reset and stretch to fill parent panel
                    textRect.anchorMin = Vector2.zero;
                    textRect.anchorMax = Vector2.one;
                    textRect.pivot = new Vector2(0.5f, 0.5f);
                    textRect.anchoredPosition = Vector2.zero; // Reset position
                    textRect.offsetMin = new Vector2(10, 5); // Left, Bottom padding
                    textRect.offsetMax = new Vector2(-10, -5); // Right, Top padding (negative!)
                    textRect.localScale = Vector3.one;
                    textRect.localRotation = Quaternion.identity;
                }

                _statusText.color = Color.white;
                _statusText.fontSize = 24;
                _statusText.alignment = TextAnchor.MiddleLeft;
                _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _statusText.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        private void OnDestroy()
        {
            if (_connectionManager != null)
            {
                _connectionManager.OnStatusMessage -= OnStatusMessage;
                _connectionManager.OnStateChanged -= OnStateChanged;
            }
        }

        private void OnStatusMessage(string message)
        {
            Debug.Log($"[ConnectionUI] OnStatusMessage: {message}");

            if (_statusText != null)
            {
                _statusText.text = message;
            }
            else
            {
                Debug.LogError("[ConnectionUI] _statusText is null!");
            }

            // Make sure panel is visible
            if (_statusPanel != null)
            {
                _statusPanel.SetActive(true);
            }
        }

        private void OnStateChanged(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    // Hide UI after delay when connected
                    Invoke(nameof(HidePanel), _hideDelay);
                    break;

                case ConnectionState.Disconnected:
                    // Show panel when disconnected
                    if (_statusPanel != null)
                    {
                        _statusPanel.SetActive(true);
                    }
                    break;
            }
        }

        private void HidePanel()
        {
            if (_statusPanel != null)
            {
                _statusPanel.SetActive(false);
            }
        }
    }
}
