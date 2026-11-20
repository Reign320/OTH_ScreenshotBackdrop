using System;
using System.IO;

using MelonLoader;

using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(OTH_ScreenshotBackdrop.ScreenshotBackdropMod), "OTH Screenshot Backdrop", "1.0.0", "Reign320")]
[assembly: MelonGame(null, null)]

namespace OTH_ScreenshotBackdrop
{
    public sealed class ScreenshotBackdropMod : MelonMod
    {
        private const string ModFolderName = "OTH_ScreenshotBackdrop";
        private const string ConfigFileName = "OTH_ScreenshotBackdrop.cfg";
        private const string DefaultColor = "#00FF00";
        private const string DefaultToggleKey = "F9";
        private const int BackdropSortingOrder = -32768;

        private GameObject _canvasObject;
        private Image _backdropImage;

        private string _configPath;
        private string _colorHex = DefaultColor;
        private KeyCode _toggleKey = KeyCode.F9;
        private bool _enabled;

        public override void OnInitializeMelon()
        {
            try
            {
                LoadConfig();
                CreateBackdrop();

                LoggerInstance.Msg("Initialized. Backdrop is disabled.");
                LoggerInstance.Msg(
                    "Toggle key: " +
                    (_toggleKey == KeyCode.None ? "Disabled" : _toggleKey.ToString()) +
                    " | Color: " + _colorHex
                );
            }
            catch (Exception ex)
            {
                LoggerInstance.Error("Initialization failed: " + ex);
                DestroyBackdrop();
            }
        }

        public override void OnUpdate()
        {
            if (_toggleKey == KeyCode.None || _canvasObject == null)
                return;

            try
            {
                if (Input.GetKeyDown(_toggleKey))
                    SetEnabled(!_enabled);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("Could not process toggle input: " + ex.Message);
            }
        }

        public override void OnDeinitializeMelon()
        {
            DestroyBackdrop();
        }

        private void LoadConfig()
        {
            string userDataPath = Path.Combine(
                MelonEnvironment.UserDataDirectory,
                ModFolderName
            );

            Directory.CreateDirectory(userDataPath);
            _configPath = Path.Combine(userDataPath, ConfigFileName);

            if (!File.Exists(_configPath))
            {
                WriteDefaultConfig();
                return;
            }

            string[] lines = File.ReadAllLines(_configPath);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("["))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();

                if (key.Equals("Color", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryNormalizeColor(value, out string normalizedColor))
                        _colorHex = normalizedColor;
                }
                else if (key.Equals("ToggleKey", StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse(value, true, out KeyCode parsedKey) &&
                        Enum.IsDefined(typeof(KeyCode), parsedKey))
                    {
                        _toggleKey = parsedKey;
                    }
                }
            }
        }

        private void WriteDefaultConfig()
        {
            string[] lines =
            {
                "; OTH Screenshot Backdrop configuration",
                "; Changes are loaded when the game starts.",
                "",
                "[General]",
                "; Hex color in #RRGGBB or #RRGGBBAA format.",
                "Color=" + DefaultColor,
                "; Key used to toggle the backdrop. Set to None to disable the hotkey.",
                "ToggleKey=" + DefaultToggleKey,
                ""
            };

            File.WriteAllLines(_configPath, lines);
        }

        private void CreateBackdrop()
        {
            _canvasObject = new GameObject("OTH_ScreenshotBackdrop_Canvas");
            _canvasObject.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_canvasObject);

            Canvas canvas = _canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = BackdropSortingOrder;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = _canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _backdropImage = _canvasObject.AddComponent<Image>();
            _backdropImage.color = ParseColor(_colorHex);
            _backdropImage.raycastTarget = false;
            _backdropImage.type = Image.Type.Simple;

            RectTransform rectTransform = _canvasObject.GetComponent<RectTransform>();
            if (rectTransform == null)
                throw new InvalidOperationException("The backdrop Canvas has no RectTransform.");

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            _canvasObject.SetActive(false);
            _enabled = false;
        }

        private void SetEnabled(bool enabled)
        {
            if (_canvasObject == null)
                return;

            _enabled = enabled;
            _canvasObject.SetActive(enabled);

            LoggerInstance.Msg(
                "Backdrop " + (enabled ? "enabled" : "disabled") +
                " (" + _colorHex + ")."
            );
        }

        private void DestroyBackdrop()
        {
            _enabled = false;

            if (_canvasObject != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(_canvasObject);
                }
                catch
                {
                    // Mod shutdown should continue even if Unity has already torn down the object.
                }
            }

            _canvasObject = null;
            _backdropImage = null;
        }

        private static bool TryNormalizeColor(string value, out string normalized)
        {
            normalized = DefaultColor;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string candidate = value.Trim();

            if (!candidate.StartsWith("#", StringComparison.Ordinal))
                candidate = "#" + candidate;

            if (candidate.Length != 7 && candidate.Length != 9)
                return false;

            if (!ColorUtility.TryParseHtmlString(candidate, out _))
                return false;

            normalized = candidate.ToUpperInvariant();
            return true;
        }

        private static Color ParseColor(string value)
        {
            if (ColorUtility.TryParseHtmlString(value, out Color parsed))
                return parsed;

            return Color.green;
        }
    }
}
