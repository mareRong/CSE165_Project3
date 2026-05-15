using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class HandPinningTravel : MonoBehaviour
{
    [Header("Agent")]
    public Transform agent;
    public Animator agentAnimator;
    public float moveSpeed = 3f;
    public float stopDistance = 0.3f;

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

    [Header("Preview Animation")]
    public float previewBobSpeed = 3f;
    public float previewBobHeight = 0.08f;

    private XRHandSubsystem handSubsystem;

    private bool isPinningMode = false;
    private bool wasPinching = false;

    private Vector3 currentRayEndPoint;
    private bool hasValidRayHit = false;

    private Vector3 targetPosition;
    private bool hasTarget = false;

    private GameObject currentPin;

    void Start()
    {
        TryInitializeHands();

        if (agentAnimator == null && agent != null)
            agentAnimator = agent.GetComponent<Animator>();

        HidePinningVisuals();
        SetWalking(false);
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
            MoveAgentToTarget();
            return;
        }

        bool thumbsUp = IsRightThumbsUp(rightHand);
        bool thumbsDown = IsRightThumbsDown(rightHand);
        bool isPinching = IsRightPinching(rightHand);

        if (thumbsUp)
        {
            isPinningMode = true;
        }

        if (thumbsDown)
        {
            isPinningMode = false;
            HidePinningVisuals();
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

        MoveAgentToTarget();
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

        // This is the important change:
        // ray direction is now wrist -> index fingertip
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

            Vector3 bobOffset =
                Vector3.up * Mathf.Sin(Time.time * previewBobSpeed) * previewBobHeight;

            pinPreviewCircle.transform.position = currentRayEndPoint + bobOffset;
        }
    }

    private void PlacePin()
    {
        if (!hasValidRayHit)
            return;

        targetPosition = currentRayEndPoint;
        hasTarget = true;

        if (currentPin != null)
            Destroy(currentPin);

        if (pinPrefab != null)
            currentPin = Instantiate(pinPrefab, targetPosition, Quaternion.identity);

        isPinningMode = false;
        HidePinningVisuals();
    }

    private void MoveAgentToTarget()
    {
        if (!hasTarget || agent == null)
        {
            SetWalking(false);
            return;
        }

        Vector3 flatTarget = new Vector3(targetPosition.x, agent.position.y, targetPosition.z);
        Vector3 direction = flatTarget - agent.position;

        if (direction.magnitude <= stopDistance)
        {
            hasTarget = false;
            SetWalking(false);
            return;
        }

        SetWalking(true);

        agent.position += direction.normalized * moveSpeed * Time.deltaTime;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        agent.rotation = Quaternion.Slerp(agent.rotation, targetRotation, Time.deltaTime * 5f);
    }

    private void SetWalking(bool walking)
    {
        if (agentAnimator != null)
        {
            agentAnimator.SetBool("Walking", walking);
        }
    }

    private void HidePinningVisuals()
    {
        if (curvedRay != null)
            curvedRay.enabled = false;

        if (pinPreviewCircle != null)
            pinPreviewCircle.SetActive(false);

        hasValidRayHit = false;
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