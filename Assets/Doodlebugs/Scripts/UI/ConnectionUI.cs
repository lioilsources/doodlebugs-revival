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

            if (_connectionManager != null)
            {
                _connectionManager.OnStatusMessage += OnStatusMessage;
                _connectionManager.OnStateChanged += OnStateChanged;
            }

            // Setup text style
            if (_statusText != null)
            {
                _statusText.color = Color.white;
                _statusText.fontSize = 28;
                _statusText.alignment = TextAnchor.MiddleCenter;
            }

            // Setup panel background
            if (_statusPanel != null)
            {
                _statusPanel.SetActive(true);
                var image = _statusPanel.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color(0, 0, 0, 0.7f); // Semi-transparent black
                }
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
            if (_statusText != null)
            {
                _statusText.text = message;
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
