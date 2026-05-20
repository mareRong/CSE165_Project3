using System.Collections;
using Meta.XR.EnvironmentDepth;
using UnityEngine;

public sealed class MixedRealityPassthroughRuntimeConfig : MonoBehaviour
{
    private const string RuntimeObjectName = "[Task 2] Mixed Reality Runtime Config";
    private bool _loggedPassthroughConfigured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeConfigExists()
    {
        if (FindAnyObjectByType<MixedRealityPassthroughRuntimeConfig>() != null)
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
        ConfigureEnvironmentDepth();
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

#pragma warning disable 0618
        activeLayer.overlayType = OVROverlay.OverlayType.Underlay;
        activeLayer.projectionSurfaceType = OVRPassthroughLayer.ProjectionSurfaceType.Reconstructed;
#pragma warning restore 0618
        activeLayer.hidden = false;
        activeLayer.textureOpacity = 1f;
        activeLayer.enabled = true;

        DisableExtraPassthroughLayers(activeLayer);
        if (!_loggedPassthroughConfigured)
        {
            Debug.Log("Task 2 passthrough is forced on with a single underlay layer.", this);
            _loggedPassthroughConfigured = true;
        }
    }

    private static OVRPassthroughLayer ResolvePrimaryPassthroughLayer(OVRManager manager)
    {
        OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.TryGetComponent(out OVRPassthroughLayer rigLayer))
        {
            return rigLayer;
        }

        OVRPassthroughLayer existingLayer = FindAnyObjectByType<OVRPassthroughLayer>();
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
        OVRPassthroughLayer[] layers = FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include);

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
        OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
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
        if (!EnvironmentDepthManager.IsSupported)
        {
            return;
        }

        EnvironmentDepthManager depthManager = FindAnyObjectByType<EnvironmentDepthManager>();
        if (depthManager == null)
        {
            depthManager = gameObject.AddComponent<EnvironmentDepthManager>();
        }

        OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.trackingSpace != null)
        {
            depthManager.CustomTrackingSpace = cameraRig.trackingSpace;
        }

        depthManager.OcclusionShadersMode = OcclusionShadersMode.SoftOcclusion;
        depthManager.enabled = true;
    }
}
