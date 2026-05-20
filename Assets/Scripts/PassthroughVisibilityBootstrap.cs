using UnityEngine;

public sealed class PassthroughVisibilityBootstrap : MonoBehaviour
{
    private const string BootstrapName = "[Task 2] Passthrough Visibility Bootstrap";

    [SerializeField] private bool _forcePassthroughOn = true;
    [SerializeField] private bool _ensurePassthroughLayer = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrapExists()
    {
        if (FindFirstObjectByType<PassthroughVisibilityBootstrap>() != null)
        {
            return;
        }

        GameObject bootstrapObject = new GameObject(BootstrapName);
        bootstrapObject.AddComponent<PassthroughVisibilityBootstrap>();
        DontDestroyOnLoad(bootstrapObject);
    }

    private void Awake()
    {
        ConfigurePassthrough();
    }

    private void Start()
    {
        ConfigurePassthrough();
    }

    private void LateUpdate()
    {
        ConfigurePassthrough();
    }

    private void ConfigurePassthrough()
    {
        if (_forcePassthroughOn && OVRManager.instance != null)
        {
            OVRManager.instance.isInsightPassthroughEnabled = true;
        }

        ConfigureCameraClears();

        if (_ensurePassthroughLayer)
        {
            ConfigurePassthroughLayer();
        }
    }

    private static void ConfigureCameraClears()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null)
            {
                continue;
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
        }
    }

    private static void ConfigurePassthroughLayer()
    {
        OVRPassthroughLayer[] passthroughLayers = FindObjectsByType<OVRPassthroughLayer>(FindObjectsSortMode.None);
        if (passthroughLayers.Length == 0)
        {
            GameObject host = ResolvePassthroughHost();
            passthroughLayers = new[] { host.AddComponent<OVRPassthroughLayer>() };
        }

        for (int i = 0; i < passthroughLayers.Length; i++)
        {
            OVRPassthroughLayer passthroughLayer = passthroughLayers[i];
            if (passthroughLayer == null)
            {
                continue;
            }

            passthroughLayer.hidden = false;
            passthroughLayer.textureOpacity = 1f;
            passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
        }
    }

    private static GameObject ResolvePassthroughHost()
    {
        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig != null)
        {
            return cameraRig.gameObject;
        }

        if (OVRManager.instance != null)
        {
            return OVRManager.instance.gameObject;
        }

        GameObject host = new GameObject("[Task 2] Passthrough Layer");
        DontDestroyOnLoad(host);
        return host;
    }
}
