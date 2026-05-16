using UnityEngine;

public sealed class WorldSpacePoseDebug : MonoBehaviour
{
    [SerializeField] private Transform observedCamera;
    [SerializeField] private float logIntervalSeconds = 1f;

    private Vector3 initialObjectPosition;
    private Vector3 initialCameraPosition;
    private float nextLogTime;

    private void Start()
    {
        if (observedCamera == null && Camera.main != null)
        {
            observedCamera = Camera.main.transform;
        }

        initialObjectPosition = transform.position;
        initialCameraPosition = observedCamera != null ? observedCamera.position : Vector3.zero;
    }

    private void LateUpdate()
    {
        if (Time.time < nextLogTime)
        {
            return;
        }

        nextLogTime = Time.time + logIntervalSeconds;

        Vector3 cameraPosition = observedCamera != null ? observedCamera.position : Vector3.zero;
        Debug.Log(
            $"{name} world debug | object {transform.position} delta {transform.position - initialObjectPosition} | "
            + $"camera {cameraPosition} delta {cameraPosition - initialCameraPosition} | "
            + $"OVR positionTracked={IsOvrPositionTracked()} origin={GetOvrTrackingOrigin()}",
            this);
    }

    private static bool IsOvrPositionTracked()
    {
        try
        {
            return OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;
        }
        catch
        {
            return false;
        }
    }

    private static string GetOvrTrackingOrigin()
    {
        try
        {
            return OVRManager.instance != null
                ? OVRManager.instance.trackingOriginType.ToString()
                : "No OVRManager";
        }
        catch
        {
            return "Unavailable";
        }
    }
}
