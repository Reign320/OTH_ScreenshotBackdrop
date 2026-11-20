using System;
using System.IO;

using MelonLoader;
using MelonLoader.Utils;

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
        private const string DefaultColor = "#E9DFCB";
        private const string DefaultToggleKey = "F9";
        private const KeyCode DiagnosticKey = KeyCode.F10;
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
                LoggerInstance.Msg("Diagnostics key: " + DiagnosticKey + ". Press it in-game to scan cameras and UI canvases.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error("Initialization failed: " + ex);
                DestroyBackdrop();
            }
        }

        public override void OnUpdate()
        {
            try
            {
                if (Input.GetKeyDown(DiagnosticKey))
                    RunDiagnostics();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("[Diagnostics] Could not start scan: " + ex);
            }

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


        private void RunDiagnostics()
        {
            LoggerInstance.Msg("[Diagnostics] ===== UI / RENDERING SCAN START =====");
            LoggerInstance.Msg("[Diagnostics] Backdrop active: " + _enabled +
                " | Color: " + _colorHex +
                " | Sorting order: " + BackdropSortingOrder);

            try
            {
                ScanCameras();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("[Diagnostics] Camera scan failed: " + ex);
            }

            try
            {
                ScanCanvases();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("[Diagnostics] Canvas scan failed: " + ex);
            }

            try
            {
                ScanTranslucentImageComponents();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("[Diagnostics] TranslucentImage scan failed: " + ex);
            }

            LoggerInstance.Msg("[Diagnostics] Note: Canvas scanning cannot enumerate arbitrary OnGUI callbacks, custom render passes or native/plugin rendering.");
            LoggerInstance.Msg("[Diagnostics] ===== UI / RENDERING SCAN END =====");
        }

        private void ScanCameras()
        {
            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            int logged = 0;

            LoggerInstance.Msg("[Diagnostics] Cameras found: " + (cameras == null ? 0 : cameras.Length));

            if (cameras == null)
                return;

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];

                try
                {
                    if (camera == null || !camera.gameObject.scene.IsValid())
                        continue;

                    string target = camera.targetTexture == null ? "Screen" : camera.targetTexture.name;

                    LoggerInstance.Msg(
                        "[Diagnostics] Camera #" + (++logged) +
                        " | Name=" + camera.name +
                        " | Active=" + camera.gameObject.activeInHierarchy +
                        " | Enabled=" + camera.enabled +
                        " | Type=" + camera.cameraType +
                        " | Depth=" + camera.depth +
                        " | ClearFlags=" + camera.clearFlags +
                        " | CullingMask=" + camera.cullingMask +
                        " | Target=" + target +
                        " | Display=" + camera.targetDisplay
                    );
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning("[Diagnostics] Could not inspect camera #" + i + ": " + ex.Message);
                }
            }

            LoggerInstance.Msg("[Diagnostics] Scene cameras logged: " + logged);
        }

        private void ScanCanvases()
        {
            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            int logged = 0;

            LoggerInstance.Msg("[Diagnostics] Canvases found (including inactive/assets): " + (canvases == null ? 0 : canvases.Length));

            if (canvases == null)
                return;

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];

                try
                {
                    if (canvas == null || !canvas.gameObject.scene.IsValid())
                        continue;

                    if (canvas.gameObject == _canvasObject)
                        continue;

                    int graphicsCount = -1;
                    int masksCount = -1;
                    int rectMasksCount = -1;
                    int groupsCount = -1;
                    string shaderSummary = "unavailable";

                    try
                    {
                        Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
                        Mask[] masks = canvas.GetComponentsInChildren<Mask>(true);
                        RectMask2D[] rectMasks = canvas.GetComponentsInChildren<RectMask2D>(true);
                        CanvasGroup[] groups = canvas.GetComponentsInChildren<CanvasGroup>(true);

                        graphicsCount = graphics == null ? 0 : graphics.Length;
                        masksCount = masks == null ? 0 : masks.Length;
                        rectMasksCount = rectMasks == null ? 0 : rectMasks.Length;
                        groupsCount = groups == null ? 0 : groups.Length;

                        shaderSummary = BuildShaderSummary(graphics);
                    }
                    catch (Exception ex)
                    {
                        shaderSummary = "component scan failed: " + ex.Message;
                    }

                    string cameraName = canvas.worldCamera == null ? "None" : canvas.worldCamera.name;

                    LoggerInstance.Msg(
                        "[Diagnostics] Canvas #" + (++logged) +
                        " | Name=" + canvas.name +
                        " | Active=" + canvas.gameObject.activeInHierarchy +
                        " | Enabled=" + canvas.enabled +
                        " | RenderMode=" + canvas.renderMode +
                        " | SortingLayer=" + canvas.sortingLayerName +
                        " | SortingOrder=" + canvas.sortingOrder +
                        " | OverrideSorting=" + canvas.overrideSorting +
                        " | Root=" + canvas.isRootCanvas +
                        " | WorldCamera=" + cameraName +
                        " | TargetDisplay=" + canvas.targetDisplay +
                        " | Graphics=" + graphicsCount +
                        " | Mask=" + masksCount +
                        " | RectMask2D=" + rectMasksCount +
                        " | CanvasGroup=" + groupsCount +
                        " | Shaders={" + shaderSummary + "}"
                    );
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning("[Diagnostics] Could not inspect canvas #" + i + ": " + ex.Message);
                }
            }

            LoggerInstance.Msg("[Diagnostics] Scene canvases logged: " + logged);
        }

        private void ScanTranslucentImageComponents()
        {
            Component[] components = Resources.FindObjectsOfTypeAll<Component>();
            int logged = 0;

            LoggerInstance.Msg("[Diagnostics] TranslucentImage-related components: scanning " +
                (components == null ? 0 : components.Length) + " components.");

            if (components == null)
                return;

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];

                try
                {
                    if (component == null || component.gameObject == null ||
                        !component.gameObject.scene.IsValid())
                        continue;

                    System.Type componentType = component.GetType();
                    string typeName = componentType.FullName ?? componentType.Name;

                    if (typeName.IndexOf("TranslucentImage", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string hierarchy = component.gameObject.name;
                    Transform parent = component.transform.parent;
                    int depth = 0;

                    while (parent != null && depth < 12)
                    {
                        hierarchy = parent.name + "/" + hierarchy;
                        parent = parent.parent;
                        depth++;
                    }

                    string enabledState = "n/a";
                    Behaviour behaviour = component as Behaviour;
                    if (behaviour != null)
                        enabledState = behaviour.enabled.ToString();

                    LoggerInstance.Msg(
                        "[Diagnostics] Blur component #" + (++logged) +
                        " | Type=" + typeName +
                        " | Object=" + hierarchy +
                        " | Active=" + component.gameObject.activeInHierarchy +
                        " | Enabled=" + enabledState
                    );

                    System.Reflection.FieldInfo[] fields = componentType.GetFields(
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic
                    );

                    int relevantFields = 0;
                    for (int fieldIndex = 0; fieldIndex < fields.Length && relevantFields < 16; fieldIndex++)
                    {
                        System.Reflection.FieldInfo field = fields[fieldIndex];
                        string fieldName = field.Name;

                        if (fieldName.IndexOf("source", StringComparison.OrdinalIgnoreCase) < 0 &&
                            fieldName.IndexOf("texture", StringComparison.OrdinalIgnoreCase) < 0 &&
                            fieldName.IndexOf("camera", StringComparison.OrdinalIgnoreCase) < 0 &&
                            fieldName.IndexOf("blur", StringComparison.OrdinalIgnoreCase) < 0 &&
                            fieldName.IndexOf("material", StringComparison.OrdinalIgnoreCase) < 0 &&
                            fieldName.IndexOf("capture", StringComparison.OrdinalIgnoreCase) < 0 &&
                            fieldName.IndexOf("command", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        string fieldValue = "(unavailable)";
                        try
                        {
                            object value = field.GetValue(component);
                            if (value == null)
                                fieldValue = "null";
                            else
                            {
                                UnityEngine.Object unityObject = value as UnityEngine.Object;
                                if (unityObject != null)
                                    fieldValue = unityObject.GetType().Name + ":" + unityObject.name;
                                else if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum)
                                    fieldValue = value.ToString();
                                else
                                    fieldValue = value.GetType().FullName;
                            }
                        }
                        catch (Exception ex)
                        {
                            fieldValue = "read failed: " + ex.GetType().Name;
                        }

                        LoggerInstance.Msg("[Diagnostics]   Field " + fieldName +
                            " (" + field.FieldType.Name + ") = " + fieldValue);
                        relevantFields++;
                    }

                    if (relevantFields == 0)
                        LoggerInstance.Msg("[Diagnostics]   No source/texture/camera-related instance fields exposed.");
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning("[Diagnostics] Could not inspect blur component #" + i + ": " + ex.Message);
                }
            }

            LoggerInstance.Msg("[Diagnostics] TranslucentImage-related components logged: " + logged);
        }

        private string BuildShaderSummary(Graphic[] graphics)
        {
            if (graphics == null || graphics.Length == 0)
                return "none";

            System.Collections.Generic.Dictionary<string, int> shaderCounts =
                new System.Collections.Generic.Dictionary<string, int>();

            for (int i = 0; i < graphics.Length; i++)
            {
                try
                {
                    Graphic graphic = graphics[i];
                    if (graphic == null)
                        continue;

                    Material material = graphic.material;
                    if (material == null || material.shader == null)
                        continue;

                    string shaderName = material.shader.name;
                    if (string.IsNullOrEmpty(shaderName))
                        shaderName = "(unnamed)";

                    if (shaderCounts.ContainsKey(shaderName))
                        shaderCounts[shaderName]++;
                    else
                        shaderCounts.Add(shaderName, 1);
                }
                catch
                {
                    // Skip individual graphics that cannot be inspected safely.
                }
            }

            if (shaderCounts.Count == 0)
                return "no explicit material shaders";

            System.Text.StringBuilder result = new System.Text.StringBuilder();
            int written = 0;

            foreach (System.Collections.Generic.KeyValuePair<string, int> pair in shaderCounts)
            {
                if (written >= 8)
                {
                    result.Append(", ...");
                    break;
                }

                if (written > 0)
                    result.Append(", ");

                result.Append(pair.Key);
                result.Append(" x");
                result.Append(pair.Value);
                written++;
            }

            return result.ToString();
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
            bool migratedDefaultColor = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("["))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();

                if (key.Equals("Color", StringComparison.OrdinalIgnoreCase))
                {
                    // Migrate the previous built-in default without changing user-selected colors.
                    if ((value.Equals("#00FF00", StringComparison.OrdinalIgnoreCase) || value.Equals("#F4EDE0", StringComparison.OrdinalIgnoreCase)))
                    {
                        _colorHex = DefaultColor;
                        lines[i] = "Color=" + DefaultColor;
                        migratedDefaultColor = true;
                    }
                    else if (TryNormalizeColor(value, out string normalizedColor))
                    {
                        _colorHex = normalizedColor;
                    }
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

            if (migratedDefaultColor)
                File.WriteAllLines(_configPath, lines);
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
