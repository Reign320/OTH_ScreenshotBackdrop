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
            LoggerInstance.Msg("[Diagnostics] ===== BLUR SOURCE SCAN START =====");
            LoggerInstance.Msg("[Diagnostics] Backdrop active: " + _enabled +
                " | Color: " + _colorHex +
                " | Sorting order: " + BackdropSortingOrder);

            try
            {
                ScanBlurTargets();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("[Diagnostics] Blur scan failed: " + ex.Message);
            }

            LoggerInstance.Msg("[Diagnostics] ===== BLUR SOURCE SCAN END =====");
        }

        private void ScanBlurTargets()
        {
            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            System.Collections.Generic.HashSet<int> inspected = new System.Collections.Generic.HashSet<int>();
            int logged = 0;

            if (canvases == null)
            {
                LoggerInstance.Warning("[Diagnostics] No canvases were returned by Unity.");
                return;
            }

            for (int canvasIndex = 0; canvasIndex < canvases.Length; canvasIndex++)
            {
                Canvas canvas = canvases[canvasIndex];
                if (canvas == null || !canvas.gameObject.scene.IsValid() ||
                    canvas.gameObject == _canvasObject)
                    continue;

                Graphic[] graphics;
                try
                {
                    graphics = canvas.GetComponentsInChildren<Graphic>(true);
                }
                catch
                {
                    continue;
                }

                if (graphics == null)
                    continue;

                for (int graphicIndex = 0; graphicIndex < graphics.Length; graphicIndex++)
                {
                    Graphic graphic = graphics[graphicIndex];
                    if (graphic == null || !graphic.gameObject.activeInHierarchy ||
                        !graphic.gameObject.name.Equals("BlurredBackground", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int instanceId = graphic.gameObject.GetInstanceID();
                    if (!inspected.Add(instanceId))
                        continue;

                    string hierarchy = GetHierarchyPath(graphic.transform, 8);
                    if (hierarchy.IndexOf("Time", StringComparison.OrdinalIgnoreCase) < 0 &&
                        hierarchy.IndexOf("OTH_CustomRoadMeter", StringComparison.OrdinalIgnoreCase) < 0 &&
                        hierarchy.IndexOf("OTH_SpeedPlate", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (logged >= 8)
                        continue;

                    logged++;
                    Material material = graphic.materialForRendering;
                    if (material == null || material.shader == null)
                        material = graphic.material;

                    LoggerInstance.Msg("[Diagnostics] Target #" + logged +
                        " | Canvas=" + canvas.name +
                        " | Path=" + hierarchy +
                        " | Shader=" + (material == null || material.shader == null ? "None" : material.shader.name));

                    LogBlurHierarchyComponents(graphic.transform, 3);

                    if (material != null)
                    {
                        try
                        {
                            string[] properties = material.GetTexturePropertyNames();
                            System.Text.StringBuilder textures = new System.Text.StringBuilder();
                            if (properties != null)
                            {
                                for (int i = 0; i < properties.Length; i++)
                                {
                                    if (i > 0)
                                        textures.Append(", ");

                                    Texture texture = material.GetTexture(properties[i]);
                                    textures.Append(properties[i]);
                                    textures.Append("=");
                                    textures.Append(texture == null ? "null" : texture.GetType().Name + ":" + texture.name);
                                }
                            }

                            LoggerInstance.Msg("[Diagnostics]   Material=" + material.name +
                                " | TextureProperties={" + (textures.Length == 0 ? "none" : textures.ToString()) + "}");
                        }
                        catch (Exception ex)
                        {
                            LoggerInstance.Msg("[Diagnostics]   Material texture inspection unavailable: " + ex.Message);
                        }
                    }
                }
            }

            LoggerInstance.Msg("[Diagnostics] Relevant active blur targets logged: " + logged +
                " (maximum 8; duplicate nested-canvas entries skipped).");
        }

        private void LogBlurHierarchyComponents(Transform target, int parentLevels)
        {
            Transform current = target;
            int level = 0;

            while (current != null && level <= parentLevels)
            {
                try
                {
                    Component[] components = current.GetComponents<Component>();
                    System.Text.StringBuilder names = new System.Text.StringBuilder();

                    if (components != null)
                    {
                        for (int i = 0; i < components.Length; i++)
                        {
                            Component component = components[i];
                            if (component == null)
                                continue;

                            if (names.Length > 0)
                                names.Append(", ");

                            Type type = component.GetType();
                            names.Append(type.FullName ?? type.Name);
                        }
                    }

                    LoggerInstance.Msg("[Diagnostics]   Hierarchy +" + level +
                        " [" + current.name + "] Components={" +
                        (names.Length == 0 ? "none" : names.ToString()) + "}");
                }
                catch (Exception ex)
                {
                    LoggerInstance.Msg("[Diagnostics]   Hierarchy +" + level +
                        " [" + current.name + "] component scan unavailable: " + ex.Message);
                }

                current = current.parent;
                level++;
            }
        }

        private string GetHierarchyPath(Transform transform, int maxDepth)
        {
            if (transform == null)
                return "(unknown)";

            System.Collections.Generic.List<string> names =
                new System.Collections.Generic.List<string>();

            Transform current = transform;
            int depth = 0;

            while (current != null && depth < maxDepth)
            {
                names.Insert(0, current.name);
                current = current.parent;
                depth++;
            }

            return string.Join("/", names.ToArray());
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
