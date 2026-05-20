using System.Collections;
using System;
using System.Reflection;
using UnityEngine;

public sealed class MixedRealityPassthroughRuntimeConfig : MonoBehaviour
{
    private const string RuntimeObjectName = "[Task 2] Mixed Reality Runtime Config";

    [SerializeField] private bool _enableEnvironmentDepthOcclusion = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeConfigExists()
    {
        if (FindFirstObjectByType<MixedRealityPassthroughRuntimeConfig>() != null)
        {
            return;
        }

        GameObject runtimeObject = new GameObject(RuntimeObjectName);
        runtimeObject.AddComponent<MixedRealityPassthroughRuntimeConfig>();
    }

    private IEnumerator Start()
    {
        yield return null;
        ConfigurePassthrough();
        ConfigureEnvironmentDepth();
    }

    private void LateUpdate()
    {
        ConfigurePassthrough();
    }

    private void ConfigurePassthrough()
    {
        OVRManager manager = OVRManager.instance;
        if (manager == null)
        {
            Debug.LogWarning("Task 2 passthrough config could not find OVRManager.", this);
            return;
        }

        manager.isInsightPassthroughEnabled = true;
        SetEyeCameraBackgroundTransparent();

        OVRPassthroughLayer activeLayer = ResolvePrimaryPassthroughLayer(manager);
        if (activeLayer == null)
        {
            Debug.LogWarning("Task 2 passthrough config could not create an OVRPassthroughLayer.", this);
            return;
        }

#pragma warning disable CS0618
        activeLayer.overlayType = OVROverlay.OverlayType.Underlay;
        activeLayer.projectionSurfaceType = OVRPassthroughLayer.ProjectionSurfaceType.Reconstructed;
#pragma warning restore CS0618
        activeLayer.hidden = false;
        activeLayer.textureOpacity = 1f;
        activeLayer.enabled = true;

        DisableExtraPassthroughLayers(activeLayer);
        Debug.Log("Task 2 passthrough is forced on with a single underlay layer.", this);
    }

    private static OVRPassthroughLayer ResolvePrimaryPassthroughLayer(OVRManager manager)
    {
        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.TryGetComponent(out OVRPassthroughLayer rigLayer))
        {
            return rigLayer;
        }

        OVRPassthroughLayer existingLayer = FindFirstObjectByType<OVRPassthroughLayer>();
        if (existingLayer != null)
        {
            return existingLayer;
        }

        if (cameraRig != null)
        {
            return cameraRig.gameObject.AddComponent<OVRPassthroughLayer>();
        }

        return manager.gameObject.AddComponent<OVRPassthroughLayer>();
    }

    private static void DisableExtraPassthroughLayers(OVRPassthroughLayer activeLayer)
    {
        OVRPassthroughLayer[] layers = FindObjectsByType<OVRPassthroughLayer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] != null && layers[i] != activeLayer)
            {
                layers[i].enabled = false;
            }
        }
    }

    private static void SetEyeCameraBackgroundTransparent()
    {
        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig == null)
        {
            return;
        }

        Camera[] cameras = cameraRig.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            cameras[i].clearFlags = CameraClearFlags.SolidColor;
            cameras[i].backgroundColor = Color.clear;
        }
    }

    private void ConfigureEnvironmentDepth()
    {
        if (!_enableEnvironmentDepthOcclusion)
        {
            return;
        }

        Type depthManagerType = Type.GetType("Meta.XR.EnvironmentDepth.EnvironmentDepthManager, Meta.XR.EnvironmentDepth");
        Type occlusionModeType = Type.GetType("Meta.XR.EnvironmentDepth.OcclusionShadersMode, Meta.XR.EnvironmentDepth");
        if (depthManagerType == null || occlusionModeType == null)
        {
            Debug.LogWarning(
                "Task 2 environment depth assembly is not available. Real furniture will still be passthrough, but it cannot depth-occlude the wall/floor overlays.",
                this);
            return;
        }

        PropertyInfo isSupportedProperty = depthManagerType.GetProperty(
            "IsSupported",
            BindingFlags.Public | BindingFlags.Static);
        bool isSupported = isSupportedProperty != null && (bool)isSupportedProperty.GetValue(null);
        if (!isSupported)
        {
            Debug.LogWarning(
                "Task 2 environment depth is not supported on this runtime. Real furniture will still be passthrough, but it cannot depth-occlude the wall/floor overlays.",
                this);
            return;
        }

        Component depthManager = FindFirstComponentOfType(depthManagerType);
        if (depthManager == null)
        {
            depthManager = gameObject.AddComponent(depthManagerType);
        }

        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.trackingSpace != null)
        {
            FieldInfo customTrackingSpaceField = depthManagerType.GetField(
                "CustomTrackingSpace",
                BindingFlags.Public | BindingFlags.Instance);
            customTrackingSpaceField?.SetValue(depthManager, cameraRig.trackingSpace);
        }

        PropertyInfo occlusionModeProperty = depthManagerType.GetProperty(
            "OcclusionShadersMode",
            BindingFlags.Public | BindingFlags.Instance);
        object softOcclusion = Enum.Parse(occlusionModeType, "SoftOcclusion");
        occlusionModeProperty?.SetValue(depthManager, softOcclusion);
        depthManager.enabled = true;
        Debug.Log("Task 2 environment depth occlusion is enabled for the wall/floor overlays.", this);
    }

    private static Component FindFirstComponentOfType(Type componentType)
    {
        Component[] components = FindObjectsByType<Component>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component != null && componentType.IsInstanceOfType(component))
            {
                return component;
            }
        }

        return null;
    }
}
