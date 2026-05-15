using System.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using TMPro;

public class HandTrackingPinning : MonoBehaviour
{
    [Header("Travel Script")]
    public AgentTravel agentTravel;

    [Header("Pinning Ray")]
    public LayerMask groundLayer;
    public LineRenderer curvedRay;
    public GameObject pinPreviewCircle;
    public GameObject pinPrefab;

    [Header("Gesture Settings")]
    public float fistThreshold = 0.075f;
    public float thumbHeightThreshold = 0.08f;
    public float pinchThreshold = 0.035f;

    [Header("Ray Settings")]
    public float rayDistance = 8f;
    public float rayCurveHeight = 1.5f;
    public int raySegments = 24;

    [Header("Preview Pulse Animation")]
    public float previewPulseSpeed = 3f;
    public float minPreviewAlpha = 0.25f;
    public float maxPreviewAlpha = 1f;

    [Header("UI Message")]
    public TextMeshProUGUI statusText;
    public float statusMessageDuration = 3f;

    private XRHandSubsystem handSubsystem;

    private bool isPinningMode = false;
    private bool wasPinching = false;

    private Vector3 currentRayEndPoint;
    private bool hasValidRayHit = false;

    private GameObject currentPin;
    private Coroutine statusCoroutine;

    void Start()
    {
        TryInitializeHands();
        HidePinningVisuals();

        if (statusText != null)
            statusText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (handSubsystem == null)
        {
            TryInitializeHands();
            return;
        }

        XRHand rightHand = handSubsystem.rightHand;

        if (!rightHand.isTracked)
        {
            HidePinningVisuals();
            return;
        }

        bool thumbsUp = IsRightThumbsUp(rightHand);
        bool thumbsDown = IsRightThumbsDown(rightHand);
        bool isPinching = IsRightPinching(rightHand);

        if (thumbsUp && !isPinningMode)
        {
            isPinningMode = true;
            ShowStatusMessage("Pinning Mode Activated");
        }

        if (thumbsDown && isPinningMode)
        {
            isPinningMode = false;
            HidePinningVisuals();
            ShowStatusMessage("Pinning Mode Cancelled");
        }

        if (isPinningMode)
        {
            if (isPinching)
            {
                UpdateCurvedRay(rightHand);
            }
            else
            {
                HidePinningVisuals();
            }

            if (wasPinching && !isPinching)
            {
                PlacePin();
            }
        }

        wasPinching = isPinching;
    }

    private bool IsRightThumbsUp(XRHand hand)
    {
        if (!TryGetPalmPose(hand, out Pose palmPose))
            return false;

        if (!IsFistWithoutThumb(hand, palmPose.position))
            return false;

        float thumbOffset = GetThumbVerticalOffset(hand, palmPose.position);

        return thumbOffset > thumbHeightThreshold;
    }

    private bool IsRightThumbsDown(XRHand hand)
    {
        if (!TryGetPalmPose(hand, out Pose palmPose))
            return false;

        if (!IsFistWithoutThumb(hand, palmPose.position))
            return false;

        float thumbOffset = GetThumbVerticalOffset(hand, palmPose.position);

        return thumbOffset < -thumbHeightThreshold;
    }

    private bool IsRightPinching(XRHand hand)
    {
        XRHandJoint indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!indexTip.TryGetPose(out Pose indexPose))
            return false;

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return Vector3.Distance(indexPose.position, thumbPose.position) < pinchThreshold;
    }

    private bool IsFistWithoutThumb(XRHand hand, Vector3 palmPosition)
    {
        XRHandJointID[] fingerTips =
        {
            XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip,
            XRHandJointID.RingTip,
            XRHandJointID.LittleTip
        };

        int curledCount = 0;

        foreach (XRHandJointID jointID in fingerTips)
        {
            XRHandJoint joint = hand.GetJoint(jointID);

            if (joint.TryGetPose(out Pose tipPose))
            {
                float distance = Vector3.Distance(tipPose.position, palmPosition);

                if (distance < fistThreshold)
                    curledCount++;
            }
        }

        return curledCount >= 4;
    }

    private float GetThumbVerticalOffset(XRHand hand, Vector3 palmPosition)
    {
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return 0f;

        return thumbPose.position.y - palmPosition.y;
    }

    private void UpdateCurvedRay(XRHand hand)
    {
        if (!TryGetJointPose(hand, XRHandJointID.Wrist, out Pose wristPose))
            return;

        if (!TryGetJointPose(hand, XRHandJointID.IndexTip, out Pose indexPose))
            return;

        Vector3 startPoint = wristPose.position;
        Vector3 forward = indexPose.position - wristPose.position;

        if (forward.sqrMagnitude < 0.0001f)
            return;

        forward.Normalize();

        Vector3 endPoint = startPoint + forward * rayDistance;
        endPoint.y -= rayCurveHeight;

        Vector3[] points = new Vector3[raySegments];
        hasValidRayHit = false;

        for (int i = 0; i < raySegments; i++)
        {
            float t = i / (float)(raySegments - 1);

            Vector3 point = Vector3.Lerp(startPoint, endPoint, t);
            point.y += Mathf.Sin(t * Mathf.PI) * rayCurveHeight;

            points[i] = point;

            if (i > 0)
            {
                Vector3 previousPoint = points[i - 1];
                Vector3 direction = point - previousPoint;
                float distance = direction.magnitude;

                if (Physics.Raycast(previousPoint, direction.normalized, out RaycastHit hit, distance, groundLayer))
                {
                    currentRayEndPoint = hit.point;
                    hasValidRayHit = true;

                    for (int j = i; j < raySegments; j++)
                    {
                        points[j] = hit.point;
                    }

                    break;
                }
            }
        }

        if (curvedRay != null)
        {
            curvedRay.enabled = true;
            curvedRay.positionCount = raySegments;
            curvedRay.SetPositions(points);
        }

        if (pinPreviewCircle != null && hasValidRayHit)
        {
            pinPreviewCircle.SetActive(true);
            pinPreviewCircle.transform.position = currentRayEndPoint;

            PulsePreviewCircle();
        }
    }

    private void PulsePreviewCircle()
    {
        Renderer circleRenderer = pinPreviewCircle.GetComponent<Renderer>();

        if (circleRenderer == null)
            return;

        Color color = circleRenderer.material.color;

        float alpha = Mathf.Lerp(
            minPreviewAlpha,
            maxPreviewAlpha,
            (Mathf.Sin(Time.time * previewPulseSpeed) + 1f) * 0.5f
        );

        color.a = alpha;
        circleRenderer.material.color = color;
    }

    private void PlacePin()
    {
        if (!hasValidRayHit)
            return;

        if (currentPin != null)
            Destroy(currentPin);

        if (pinPrefab != null)
            currentPin = Instantiate(pinPrefab, currentRayEndPoint, Quaternion.identity);

        if (agentTravel != null)
            agentTravel.SetDestination(currentRayEndPoint);

        isPinningMode = false;
        HidePinningVisuals();
        ShowStatusMessage("Pin Placed");
    }

    private void HidePinningVisuals()
    {
        if (curvedRay != null)
            curvedRay.enabled = false;

        if (pinPreviewCircle != null)
            pinPreviewCircle.SetActive(false);

        hasValidRayHit = false;
    }

    private void ShowStatusMessage(string message)
    {
        if (statusText == null)
            return;

        if (statusCoroutine != null)
            StopCoroutine(statusCoroutine);

        statusCoroutine = StartCoroutine(StatusMessageRoutine(message));
    }

    private IEnumerator StatusMessageRoutine(string message)
    {
        statusText.gameObject.SetActive(true);
        statusText.text = message;

        yield return new WaitForSeconds(statusMessageDuration);

        statusText.gameObject.SetActive(false);
    }

    private void TryInitializeHands()
    {
        var manager = XRGeneralSettings.Instance?.Manager;

        if (manager?.activeLoader == null)
            return;

        handSubsystem = manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
    }

    private bool TryGetPalmPose(XRHand hand, out Pose pose)
    {
        XRHandJoint palm = hand.GetJoint(XRHandJointID.Palm);
        return palm.TryGetPose(out pose);
    }

    private bool TryGetJointPose(XRHand hand, XRHandJointID jointID, out Pose pose)
    {
        XRHandJoint joint = hand.GetJoint(jointID);
        return joint.TryGetPose(out pose);
    }
}