using System;
using System.IO;
using System.Reflection;

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
                LoggerInstance.Msg("Diagnostics key: " + DiagnosticKey + ". Press it in-game to inspect the HUD blur materials.");
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

            try
            {
                ScanTranslucentImageSources();
                ScanTranslucentImageSourceSetters();
                ScanRelatedRuntimeTypes();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning("[Diagnostics] Extended blur scan failed: " + ex.Message);
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
                    Material sourceMaterial = graphic.material;
                    Material renderMaterial = graphic.materialForRendering;
                    Material material = renderMaterial != null ? renderMaterial : sourceMaterial;

                    LoggerInstance.Msg("[Diagnostics] Target #" + logged +
                        " | Canvas=" + canvas.name +
                        " | Path=" + hierarchy +
                        " | Shader=" + (material == null || material.shader == null ? "None" : material.shader.name));

                    if (logged == 1 && material != null && material.shader != null)
                        LogShaderProperties(material.shader);

                    LogBlurHierarchyComponents(graphic.transform, 3);

                    if (sourceMaterial != null)
                        LogMaterialState("SourceMaterial", sourceMaterial);

                    if (renderMaterial != null && renderMaterial != sourceMaterial)
                        LogMaterialState("RenderMaterial", renderMaterial);
                }
            }

            LoggerInstance.Msg("[Diagnostics] Relevant active blur targets logged: " + logged +
                " (maximum 8; duplicate nested-canvas entries skipped).");
        }



        private void ScanTranslucentImageSources()
        {
            const string sourceTypeName = "Il2CppLeTai.Asset.TranslucentImage.TranslucentImageSource";
            const string imageTypeName = "Il2CppLeTai.Asset.TranslucentImage.TranslucentImage";
            Type sourceType = FindLoadedType(sourceTypeName, "LeTai.Asset.TranslucentImage.TranslucentImageSource");
            Type imageType = FindLoadedType(imageTypeName, "LeTai.Asset.TranslucentImage.TranslucentImage");

            if (sourceType == null)
            {
                LoggerInstance.Warning("[Diagnostics][LeTai] TranslucentImageSource type not found.");
                return;
            }

            LoggerInstance.Msg("[Diagnostics][LeTai] Source type resolved: " + sourceType.FullName);
            if (imageType == null)
            {
                LoggerInstance.Warning("[Diagnostics][LeTai] TranslucentImage component type not found. " +
                    "Cannot follow source references from the known HUD blur targets.");
                return;
            }

            LoggerInstance.Msg("[Diagnostics][LeTai] Image type resolved: " + imageType.FullName);

            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            System.Collections.Generic.HashSet<int> inspectedSources =
                new System.Collections.Generic.HashSet<int>();
            int targetCount = 0;
            int sourceCount = 0;

            if (canvases == null)
            {
                LoggerInstance.Warning("[Diagnostics][LeTai] No canvases available for target-based source scan.");
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
                    if (graphic == null ||
                        !graphic.gameObject.name.Equals("BlurredBackground", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string hierarchy = GetHierarchyPath(graphic.transform, 8);
                    if (hierarchy.IndexOf("Time", StringComparison.OrdinalIgnoreCase) < 0 &&
                        hierarchy.IndexOf("OTH_CustomRoadMeter", StringComparison.OrdinalIgnoreCase) < 0 &&
                        hierarchy.IndexOf("OTH_SpeedPlate", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    targetCount++;
                    Component translucentImage = null;
                    try
                    {
                        // Enumerate attached components and compare runtime type names.
                        // This avoids the System.Type / Il2CppSystem.Type overload mismatch
                        // and does not require a compile-time reference to LeTai's assembly.
                        Component[] attachedComponents = graphic.GetComponents<Component>();
                        if (attachedComponents != null)
                        {
                            for (int componentIndex = 0; componentIndex < attachedComponents.Length; componentIndex++)
                            {
                                Component candidate = attachedComponents[componentIndex];
                                if (candidate == null)
                                    continue;

                                Type candidateType = candidate.GetType();
                                if (candidateType != null &&
                                    string.Equals(candidateType.FullName, imageType.FullName, StringComparison.Ordinal))
                                {
                                    translucentImage = candidate;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerInstance.Warning("[Diagnostics][LeTai] Component enumeration failed for " +
                            hierarchy + ": " + ex.GetType().Name + ": " + ex.Message);
                    }

                    if (translucentImage == null)
                    {
                        LoggerInstance.Warning("[Diagnostics][LeTai] No TranslucentImage component found on " + hierarchy);
                        continue;
                    }

                    object sourceValue = ReadDiagnosticMember(
                        translucentImage, imageType, "source", "_source");

                    Component sourceComponent = sourceValue as Component;
                    if (sourceComponent == null)
                    {
                        LoggerInstance.Warning("[Diagnostics][LeTai] source reference is not a Component on " +
                            hierarchy + " | Value=" + FormatDiagnosticValue(sourceValue));
                        continue;
                    }

                    int sourceId = sourceComponent.GetInstanceID();
                    if (!inspectedSources.Add(sourceId))
                    {
                        LoggerInstance.Msg("[Diagnostics][LeTai] Shared source reused by target: " + hierarchy +
                            " | SourceID=" + sourceId);
                        continue;
                    }

                    sourceCount++;
                    LoggerInstance.Msg("[Diagnostics][LeTai] Target=" + hierarchy +
                        " | TranslucentImage=" + GetHierarchyPath(translucentImage.transform, 8) +
                        " | SourceID=" + sourceId);
                    LogTranslucentImageSource(sourceComponent, sourceType, sourceCount);
                }
            }

            LoggerInstance.Msg("[Diagnostics][LeTai] Target-based scan complete. Targets=" +
                targetCount + " | Unique sources=" + sourceCount + ".");
        }

        private void ScanTranslucentImageSourceSetters()
        {
            const string setterTypeToken = "TranslucentImageSourceSetter";
            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            System.Collections.Generic.HashSet<int> inspected =
                new System.Collections.Generic.HashSet<int>();
            int found = 0;

            if (canvases == null)
            {
                LoggerInstance.Warning("[Diagnostics][Setter] No canvases available.");
                return;
            }

            for (int canvasIndex = 0; canvasIndex < canvases.Length; canvasIndex++)
            {
                Canvas canvas = canvases[canvasIndex];
                if (canvas == null || !canvas.gameObject.scene.IsValid() ||
                    canvas.gameObject == _canvasObject)
                    continue;

                Component[] components;
                try
                {
                    components = canvas.GetComponentsInChildren<Component>(true);
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning("[Diagnostics][Setter] Component scan failed on Canvas " +
                        canvas.name + ": " + ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

                if (components == null)
                    continue;

                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null)
                        continue;

                    Type componentType = component.GetType();
                    string fullName = componentType.FullName ?? componentType.Name;
                    if (fullName.IndexOf(setterTypeToken, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    int instanceId = component.GetInstanceID();
                    if (!inspected.Add(instanceId))
                        continue;

                    found++;
                    LoggerInstance.Msg("[Diagnostics][Setter] ===== SETTER #" + found + " =====");
                    LoggerInstance.Msg("[Diagnostics][Setter] Type=" + fullName +
                        " | Assembly=" + componentType.Assembly.GetName().Name +
                        " | Object=" + GetHierarchyPath(component.transform, 12) +
                        " | Enabled=" + (((Behaviour)component).enabled) +
                        " | Active=" + component.gameObject.activeInHierarchy +
                        " | InstanceID=" + instanceId);

                    LogComponentFields(component, 8);
                }
            }

            LoggerInstance.Msg("[Diagnostics][Setter] Scan complete. Setters found=" + found + ".");
        }

        private Type FindLoadedType(params string[] candidateNames)
        {
            if (candidateNames == null || candidateNames.Length == 0)
                return null;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                Assembly assembly = assemblies[assemblyIndex];
                if (assembly == null)
                    continue;

                for (int nameIndex = 0; nameIndex < candidateNames.Length; nameIndex++)
                {
                    try
                    {
                        Type candidate = assembly.GetType(candidateNames[nameIndex], false);
                        if (candidate != null)
                            return candidate;
                    }
                    catch
                    {
                        // Continue with the next candidate name or assembly.
                    }
                }
            }

            return null;
        }

        private void LogTranslucentImageSource(Component source, Type sourceType, int index)
        {
            LoggerInstance.Msg("[Diagnostics][LeTai] ===== SOURCE #" + index + " =====");
            LoggerInstance.Msg("[Diagnostics][LeTai] Object=" +
                GetHierarchyPath(source.transform, 10) +
                " | Enabled=" + ((Behaviour)source).enabled +
                " | Active=" + source.gameObject.activeInHierarchy +
                " | InstanceID=" + source.GetInstanceID());

            object cameraValue = ReadDiagnosticMember(source, sourceType, "Cam", "camera");
            Camera sourceCamera = cameraValue as Camera;
            if (sourceCamera != null)
            {
                LoggerInstance.Msg("[Diagnostics][LeTai] Camera=" +
                    GetHierarchyPath(sourceCamera.transform, 10) +
                    " | Enabled=" + sourceCamera.enabled +
                    " | Active=" + sourceCamera.gameObject.activeInHierarchy +
                    " | Depth=" + sourceCamera.depth.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " | Type=" + sourceCamera.cameraType +
                    " | ClearFlags=" + sourceCamera.clearFlags +
                    " | CullingMask=" + sourceCamera.cullingMask +
                    " | TargetTexture=" + (sourceCamera.targetTexture == null
                        ? "null"
                        : sourceCamera.targetTexture.name + " (" +
                            sourceCamera.targetTexture.width + "x" + sourceCamera.targetTexture.height + ")"));
            }
            else
            {
                LoggerInstance.Msg("[Diagnostics][LeTai] Camera member=" +
                    FormatDiagnosticValue(cameraValue));
            }

            LogLeTaiMember(source, sourceType, "BlurredScreen", "blurredScreen");
            LogLeTaiMember(source, sourceType, "isForOverlayCanvas");
            LogLeTaiMember(source, sourceType, "needRegisterCanvasPreRenderCallback");
            LogLeTaiMember(source, sourceType, "Downsample", "downsample");
            LogLeTaiMember(source, sourceType, "BlurRegion", "blurRegion");
            LogLeTaiMember(source, sourceType, "BlurRegionNormalizedScreenSpace");
            LogLeTaiMember(source, sourceType, "ActiveRegion");
            LogLeTaiMember(source, sourceType, "CamRectOverride");
            LogLeTaiMember(source, sourceType, "MaxUpdateRate", "maxUpdateRate");
            LogLeTaiMember(source, sourceType, "CullPadding", "cullPadding");
            LogLeTaiMember(source, sourceType, "SkipCulling", "skipCulling");
            LogLeTaiMember(source, sourceType, "Preview", "preview");

            object blurredValue = ReadDiagnosticMember(source, sourceType, "BlurredScreen", "blurredScreen");
            RenderTexture blurredTexture = blurredValue as RenderTexture;
            if (blurredTexture != null)
            {
                LoggerInstance.Msg("[Diagnostics][LeTai] BlurredScreen details: " +
                    blurredTexture.width + "x" + blurredTexture.height +
                    " | Format=" + blurredTexture.format +
                    " | Dimension=" + blurredTexture.dimension +
                    " | Depth=" + blurredTexture.depth +
                    " | IsCreated=" + blurredTexture.IsCreated() +
                    " | AntiAliasing=" + blurredTexture.antiAliasing);
            }

            LoggerInstance.Msg("[Diagnostics][LeTai] ===== END SOURCE #" + index + " =====");
        }

        private void LogLeTaiMember(Component source, Type sourceType, params string[] names)
        {
            object value = ReadDiagnosticMember(source, sourceType, names);
            string memberName = names == null || names.Length == 0 ? "(unknown)" : names[0];
            LoggerInstance.Msg("[Diagnostics][LeTai] " + memberName + "=" +
                FormatDiagnosticValue(value));
        }

        private object ReadDiagnosticMember(object instance, Type declaringType, params string[] names)
        {
            if (instance == null || declaringType == null || names == null)
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                try
                {
                    PropertyInfo property = declaringType.GetProperty(name, flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                        return property.GetValue(instance, null);

                    FieldInfo field = declaringType.GetField(name, flags);
                    if (field != null)
                        return field.GetValue(instance);
                }
                catch (Exception ex)
                {
                    LoggerInstance.Msg("[Diagnostics][LeTai] Could not read " + name +
                        ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            return null;
        }

        private void ScanRelatedRuntimeTypes()
        {
            string[] tokens =
            {
                "TranslucentImage",
                "TranslucentImageSource",
                "TranslucentImageBlurSource",
                "BlurredBackground",
                "BlurSource"
            };

            int logged = 0;
            int matchingAssemblies = 0;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                Assembly assembly = assemblies[assemblyIndex];
                if (assembly == null)
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                    continue;

                bool assemblyMatched = false;
                for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
                {
                    Type type = types[typeIndex];
                    if (type == null)
                        continue;

                    string fullName = type.FullName ?? type.Name;
                    bool matches = false;

                    for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
                    {
                        if (fullName.IndexOf(tokens[tokenIndex], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matches = true;
                            break;
                        }
                    }

                    if (!matches)
                        continue;

                    if (!assemblyMatched)
                    {
                        matchingAssemblies++;
                        assemblyMatched = true;
                    }

                    LoggerInstance.Msg("[Diagnostics] BlurRuntimeType: " + fullName +
                        " | Assembly=" + assembly.GetName().Name);
                    logged++;
                }
            }

            LoggerInstance.Msg("[Diagnostics] Relevant blur runtime types: " + logged +
                " | Assemblies containing matches: " + matchingAssemblies + ".");
        }

        private void LogComponentFields(Component component, int maxBaseTypes)
        {
            Type currentType = component.GetType();
            int depth = 0;

            while (currentType != null && currentType != typeof(MonoBehaviour) &&
                currentType != typeof(Behaviour) && currentType != typeof(Component) &&
                depth <= maxBaseTypes)
            {
                FieldInfo[] fields = currentType.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly
                );

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field == null || field.IsStatic)
                        continue;

                    string fieldName = field.Name;
                    string lowerName = fieldName.ToLowerInvariant();
                    if (lowerName.IndexOf("source") < 0 &&
                        lowerName.IndexOf("camera") < 0 &&
                        lowerName.IndexOf("texture") < 0 &&
                        lowerName.IndexOf("blur") < 0 &&
                        lowerName.IndexOf("render") < 0 &&
                        lowerName.IndexOf("update") < 0 &&
                        lowerName.IndexOf("preview") < 0 &&
                        lowerName.IndexOf("material") < 0 &&
                        lowerName.IndexOf("config") < 0)
                        continue;

                    string valueText;
                    try
                    {
                        valueText = FormatDiagnosticValue(field.GetValue(component));
                    }
                    catch (Exception ex)
                    {
                        valueText = "<unavailable:" + ex.GetType().Name + ">";
                    }

                    LoggerInstance.Msg("[Diagnostics]   Field " + currentType.Name + "." +
                        fieldName + "=" + valueText);
                }

                currentType = currentType.BaseType;
                depth++;
            }
        }

        private string FormatDiagnosticValue(object value)
        {
            if (value == null)
                return "null";

            UnityEngine.Object unityObject = value as UnityEngine.Object;
            if (unityObject != null)
                return unityObject.GetType().Name + ":" + unityObject.name;

            Type valueType = value.GetType();
            if (valueType.IsPrimitive || valueType.IsEnum ||
                value is string || value is decimal)
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

            return "<" + (valueType.FullName ?? valueType.Name) + ">";
        }

        private void LogMaterialState(string label, Material material)
        {
            try
            {
                string[] floatProperties =
                {
                    "_BACKGROUND_MODE",
                    "_REFRACTION_MODE",
                    "_RefractiveIndex",
                    "_ChromaticDispersion",
                    "_USE_EDGE_GLINT",
                    "_EdgeGlint1Strength",
                    "_EdgeGlint2Strength"
                };

                System.Text.StringBuilder values = new System.Text.StringBuilder();
                for (int i = 0; i < floatProperties.Length; i++)
                {
                    string property = floatProperties[i];
                    if (!material.HasProperty(property))
                        continue;

                    if (values.Length > 0)
                        values.Append(", ");

                    values.Append(property);
                    values.Append("=");
                    values.Append(material.GetFloat(property).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
                }

                string[] textureProperties = material.GetTexturePropertyNames();
                System.Text.StringBuilder textures = new System.Text.StringBuilder();
                if (textureProperties != null)
                {
                    for (int i = 0; i < textureProperties.Length; i++)
                    {
                        if (textures.Length > 0)
                            textures.Append(", ");

                        Texture texture = material.GetTexture(textureProperties[i]);
                        textures.Append(textureProperties[i]);
                        textures.Append("=");
                        textures.Append(texture == null ? "null" : texture.GetType().Name + ":" + texture.name);
                    }
                }

                LoggerInstance.Msg("[Diagnostics]   " + label +
                    "=" + material.name +
                    " | Queue=" + material.renderQueue +
                    " | FloatProperties={" + (values.Length == 0 ? "none" : values.ToString()) + "}" +
                    " | TextureProperties={" + (textures.Length == 0 ? "none" : textures.ToString()) + "}");
            }
            catch (Exception ex)
            {
                LoggerInstance.Msg("[Diagnostics]   " + label +
                    " inspection unavailable: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void LogShaderProperties(Shader shader)
        {
            Type shaderType = shader.GetType();
            LoggerInstance.Msg("[Diagnostics] Shader runtime type: " +
                (shaderType.FullName ?? shaderType.Name));

            try
            {
                MethodInfo countMethod = shaderType.GetMethod(
                    "GetPropertyCount",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                MethodInfo nameMethod = shaderType.GetMethod(
                    "GetPropertyName",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(int) },
                    null
                );
                MethodInfo typeMethod = shaderType.GetMethod(
                    "GetPropertyType",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(int) },
                    null
                );

                if (countMethod == null || nameMethod == null)
                {
                    LoggerInstance.Msg("[Diagnostics] Shader property enumeration API is not exposed by this runtime wrapper.");
                    return;
                }

                int count = Convert.ToInt32(countMethod.Invoke(shader, null));
                int limit = Math.Min(count, 40);
                LoggerInstance.Msg("[Diagnostics] Shader properties: " + count +
                    " (listing " + limit + ")");

                for (int i = 0; i < limit; i++)
                {
                    object name = nameMethod.Invoke(shader, new object[] { i });
                    object propertyType = typeMethod == null
                        ? null
                        : typeMethod.Invoke(shader, new object[] { i });

                    LoggerInstance.Msg("[Diagnostics]   ShaderProperty[" + i + "] " +
                        Convert.ToString(name) +
                        (propertyType == null ? "" : " | Type=" + propertyType));
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Msg("[Diagnostics] Shader property enumeration unavailable: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
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
