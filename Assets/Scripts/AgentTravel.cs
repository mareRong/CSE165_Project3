using System;
using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Animator))]
public class AgentTravel : MonoBehaviour
{
    [Header("Agent")]
    public Transform avatar;
    public Animator avatarAnimator;
    public NavMeshAgent navMeshAgent;

    [Header("Runtime NavMesh")]
    public bool useNavMesh = true;
    public NavMeshSurface navMeshSurface;
    public float bakeDelay = 2f;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float rotationSpeed = 5f;
    public float stopDistance = 0.3f;

    [Header("Wall Avoidance")]
    public bool avoidWalls = true;
    public LayerMask wallLayers = ~0;
    public float wallClearance = 0.12f;
    public float wallStopTolerance = 0.02f;
    public float avatarCollisionRadius = 0.18f;
    public float wallProbeHeight = 0.9f;
    public float blockedMoveTimeout = 0.2f;
    public float blockedMoveDistance = 0.01f;

    [Header("Ground / Feet")]
    public LayerMask groundLayers = ~0;
    public float groundProbeHeight = 2f;
    public float groundProbeDistance = 6f;
    public float groundOffset = 0.01f;

    [Header("Foot IK")]
    public bool useFootIK = true;
    public float footRaycastHeight = 0.5f;
    public float footRaycastDistance = 1.5f;
    public float footOffset = 0.02f;
    [Range(0f, 1f)] public float footPositionWeight = 1f;
    [Range(0f, 1f)] public float footRotationWeight = 0.6f;

    public event Action DestinationReached;

    private Vector3 targetPosition;
    private bool hasTarget;
    private Vector3 lastMovementCheckPosition;
    private float blockedMoveTimer;

    private IEnumerator Start()
    {
        if (avatar == null)
        {
            avatar = transform;
        }

        if (avatarAnimator == null)
        {
            avatarAnimator = GetComponent<Animator>();
        }

        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }

        if (navMeshAgent == null)
        {
            navMeshAgent = gameObject.AddComponent<NavMeshAgent>();
        }

        navMeshAgent.speed = moveSpeed;
        navMeshAgent.angularSpeed = rotationSpeed * 120f;
        navMeshAgent.stoppingDistance = stopDistance;
        navMeshAgent.updatePosition = true;
        navMeshAgent.updateRotation = true;

        ResetBlockedMovementTracking();
        ResumeNavMeshAgent();
        SetWalking(false);

        yield return new WaitForSeconds(bakeDelay);

        BakeNavMesh();
        SnapAvatarToNavMesh();
    }

    private void Update()
    {
        if (useNavMesh && navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            HandleNavMeshMovement();
        }
        else
        {
            HandleSimpleMovement();
        }
    }

    public void BakeNavMesh()
    {
        if (navMeshSurface == null)
        {
            navMeshSurface = FindAnyObjectByType<NavMeshSurface>();
        }

        if (navMeshSurface == null)
        {
            GameObject surfaceObject = new GameObject("Runtime NavMesh Surface");
            navMeshSurface = surfaceObject.AddComponent<NavMeshSurface>();
        }

        navMeshSurface.collectObjects = CollectObjects.All;
        navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        navMeshSurface.BuildNavMesh();

        Debug.Log("Runtime NavMesh baked from spatial anchor colliders.", this);
    }

    public void SetDestination(Vector3 destination)
    {
        targetPosition = destination;
        hasTarget = true;
        ResetBlockedMovementTracking();
        ResumeNavMeshAgent();

        if (useNavMesh && navMeshAgent != null)
        {
            if (!navMeshAgent.isOnNavMesh)
            {
                SnapAvatarToNavMesh();
            }

            if (navMeshAgent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    navMeshAgent.SetDestination(hit.position);
                }
                else
                {
                    navMeshAgent.SetDestination(destination);
                }
            }
        }
    }

    private void HandleNavMeshMovement()
    {
        if (!hasTarget)
        {
            StopNavMeshAgent();
            SetWalking(false);
            return;
        }

        if (IsBlockedNearWallOrDestination())
        {
            StopBlockedMovement();
            return;
        }

        if (IsInsideWallStopThreshold())
        {
            StopBlockedMovement();
            return;
        }

        if (TryFinishReachedDestination())
        {
            return;
        }

        if (ApplyNavMeshWallClearance())
        {
            return;
        }

        SetWalking(navMeshAgent.velocity.magnitude > 0.05f);
    }

    private void HandleSimpleMovement()
    {
        if (!hasTarget || avatar == null)
        {
            SetWalking(false);
            return;
        }

        Vector3 flatTarget = new Vector3(targetPosition.x, avatar.position.y, targetPosition.z);
        Vector3 direction = flatTarget - avatar.position;

        if (direction.magnitude <= stopDistance)
        {
            FinishDestination();
            return;
        }

        Vector3 travelDirection = direction.normalized;
        float moveDistance = Mathf.Min(moveSpeed * Time.deltaTime, direction.magnitude);
        if (IsInsideWallStopThreshold())
        {
            StopBlockedMovement();
            return;
        }

        if (IsAtWallStopThreshold(travelDirection))
        {
            StopBlockedMovement();
            return;
        }

        if (TryGetWallLimitedMoveDistance(travelDirection, moveDistance, out float limitedDistance))
        {
            if (limitedDistance <= 0.001f)
            {
                StopBlockedMovement();
                return;
            }

            moveDistance = limitedDistance;
        }

        Vector3 nextPosition = avatar.position + travelDirection * moveDistance;
        avatar.position = ProjectOntoGround(nextPosition);
        if (IsInsideWallStopThreshold())
        {
            StopBlockedMovement();
            return;
        }

        SetWalking(true);

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(travelDirection, Vector3.up);
            avatar.rotation = Quaternion.Slerp(
                avatar.rotation,
                targetRotation,
                Time.deltaTime * rotationSpeed
            );
        }
    }

    private bool ApplyNavMeshWallClearance()
    {
        if (!avoidWalls || avatar == null || navMeshAgent == null)
        {
            return false;
        }

        Vector3 velocity = navMeshAgent.velocity;
        velocity.y = 0f;
        if (velocity.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector3 travelDirection = velocity.normalized;
        float stopThreshold = GetWallStopThreshold();
        if (!TryGetNearestWallHit(travelDirection, stopThreshold, out RaycastHit hit))
        {
            return false;
        }

        if (hit.distance > stopThreshold)
        {
            return false;
        }

        float correctionDistance = Mathf.Max(0f, wallClearance - hit.distance);
        Vector3 correctedPosition = ProjectOntoGround(avatar.position - travelDirection * correctionDistance);
        avatar.position = correctedPosition;
        navMeshAgent.Warp(correctedPosition);
        navMeshAgent.ResetPath();
        StopBlockedMovement();
        return true;
    }

    private bool IsBlockedNearWallOrDestination()
    {
        if (avatar == null || navMeshAgent == null || navMeshAgent.pathPending)
        {
            ResetBlockedMovementTracking();
            return false;
        }

        if (navMeshAgent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            return true;
        }

        Vector3 currentPosition = avatar.position;
        Vector3 movementDelta = currentPosition - lastMovementCheckPosition;
        movementDelta.y = 0f;

        bool tryingToMove = navMeshAgent.hasPath ||
                            navMeshAgent.desiredVelocity.sqrMagnitude > 0.0025f ||
                            navMeshAgent.velocity.sqrMagnitude > 0.0025f;

        if (!tryingToMove || movementDelta.magnitude > Mathf.Max(0.001f, blockedMoveDistance))
        {
            ResetBlockedMovementTracking();
            return false;
        }

        Vector3 targetDirection = targetPosition - currentPosition;
        targetDirection.y = 0f;
        bool nearWall = targetDirection.sqrMagnitude > 0.0001f &&
                        TryGetNearestWallHit(targetDirection.normalized, GetForwardWallProbeDistance(), out _);

        bool nearDestination = !float.IsInfinity(navMeshAgent.remainingDistance) &&
                               navMeshAgent.remainingDistance <= Mathf.Max(stopDistance, navMeshAgent.stoppingDistance) + GetWallStopThreshold();

        blockedMoveTimer += Time.deltaTime;
        float timeout = Mathf.Max(0f, blockedMoveTimeout);
        if (!nearWall && !nearDestination && navMeshAgent.pathStatus != NavMeshPathStatus.PathPartial)
        {
            timeout *= 2f;
        }

        return blockedMoveTimer >= timeout;
    }

    private bool TryGetWallLimitedMoveDistance(
        Vector3 travelDirection,
        float requestedDistance,
        out float limitedDistance)
    {
        limitedDistance = requestedDistance;
        if (!avoidWalls || avatar == null || requestedDistance <= 0f)
        {
            return false;
        }

        float probeDistance = requestedDistance + Mathf.Max(0f, wallClearance);
        if (!TryGetNearestWallHit(travelDirection, probeDistance, out RaycastHit hit))
        {
            return false;
        }

        limitedDistance = Mathf.Max(0f, hit.distance - Mathf.Max(0f, wallClearance));
        return limitedDistance < requestedDistance;
    }

    private bool IsAtWallStopThreshold(Vector3 travelDirection)
    {
        return avoidWalls &&
               TryGetNearestWallHit(travelDirection, GetWallStopThreshold(), out _);
    }

    private bool IsInsideWallStopThreshold()
    {
        if (!avoidWalls || avatar == null)
        {
            return false;
        }

        float threshold = GetWallStopThreshold();
        float overlapRadius = Mathf.Max(0.01f, avatarCollisionRadius + threshold);
        Vector3 origin = avatar.position + Vector3.up * Mathf.Max(0f, wallProbeHeight);
        Collider[] colliders = Physics.OverlapSphere(
            origin,
            overlapRadius,
            wallLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider wallCollider = colliders[i];
            if (!IsWallCollider(wallCollider))
            {
                continue;
            }

            Vector3 closestPoint = wallCollider.ClosestPoint(origin);
            float distanceToAvatarShell = Vector3.Distance(origin, closestPoint) - Mathf.Max(0.01f, avatarCollisionRadius);
            if (distanceToAvatarShell <= threshold)
            {
                return true;
            }
        }

        return false;
    }

    private float GetWallStopThreshold()
    {
        return Mathf.Max(0f, wallClearance) + Mathf.Max(0f, wallStopTolerance);
    }

    private float GetForwardWallProbeDistance()
    {
        return GetWallStopThreshold() + Mathf.Max(0f, moveSpeed * Time.deltaTime);
    }

    private bool TryGetNearestWallHit(
        Vector3 travelDirection,
        float probeDistance,
        out RaycastHit nearestHit)
    {
        nearestHit = default;
        if (avatar == null || travelDirection.sqrMagnitude < 0.0001f || probeDistance <= 0f)
        {
            return false;
        }

        Vector3 origin = avatar.position + Vector3.up * Mathf.Max(0f, wallProbeHeight);
        float radius = Mathf.Max(0.01f, avatarCollisionRadius);
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            radius,
            travelDirection.normalized,
            probeDistance,
            wallLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool foundWall = false;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (!IsWallHit(hit) || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestHit = hit;
            nearestDistance = hit.distance;
            foundWall = true;
        }

        return foundWall;
    }

    private static bool IsWallHit(RaycastHit hit)
    {
        return IsWallCollider(hit.collider);
    }

    private static bool IsWallCollider(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        SpatialSurfaceMarker marker = collider.GetComponentInParent<SpatialSurfaceMarker>();
        return marker != null &&
               marker.SurfaceKind == SpatialSurfaceAnchorManager.SurfaceKind.Wall;
    }

    private void StopBlockedMovement()
    {
        hasTarget = false;
        SetWalking(false);
        StopNavMeshAgent();
        ResetBlockedMovementTracking();
    }

    private bool TryFinishReachedDestination()
    {
        if (navMeshAgent.pathPending || float.IsInfinity(navMeshAgent.remainingDistance))
        {
            return false;
        }

        if (navMeshAgent.remainingDistance > navMeshAgent.stoppingDistance)
        {
            return false;
        }

        if (navMeshAgent.hasPath && navMeshAgent.velocity.sqrMagnitude > 0.0025f)
        {
            return false;
        }

        FinishDestination();
        return true;
    }

    private void FinishDestination()
    {
        hasTarget = false;
        StopNavMeshAgent();
        ResetBlockedMovementTracking();
        SetWalking(false);
        DestinationReached?.Invoke();
    }

    private void StopNavMeshAgent()
    {
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.ResetPath();
            navMeshAgent.velocity = Vector3.zero;
        }
    }

    private void ResumeNavMeshAgent()
    {
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = false;
        }
    }

    private void ResetBlockedMovementTracking()
    {
        lastMovementCheckPosition = avatar != null ? avatar.position : transform.position;
        blockedMoveTimer = 0f;
    }

    private void SnapAvatarToNavMesh()
    {
        if (avatar == null)
        {
            return;
        }

        if (NavMesh.SamplePosition(avatar.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            avatar.position = hit.position;

            if (navMeshAgent != null)
            {
                navMeshAgent.Warp(hit.position);
            }
        }
        else
        {
            avatar.position = ProjectOntoGround(avatar.position);
        }
    }

    private Vector3 ProjectOntoGround(Vector3 position)
    {
        if (TryFindGround(position, out RaycastHit hit))
        {
            position.y = hit.point.y + groundOffset;
        }

        return position;
    }

    private bool TryFindGround(Vector3 aroundPosition, out RaycastHit hit)
    {
        Vector3 rayOrigin = aroundPosition + Vector3.up * groundProbeHeight;
        float rayDistance = groundProbeHeight + groundProbeDistance;

        return Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out hit,
            rayDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    private void SetWalking(bool walking)
    {
        if (avatarAnimator != null)
        {
            avatarAnimator.SetBool("Walking", walking);
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!useFootIK || avatarAnimator == null)
        {
            return;
        }

        StickFootToGround(AvatarIKGoal.LeftFoot);
        StickFootToGround(AvatarIKGoal.RightFoot);
    }

    private void StickFootToGround(AvatarIKGoal foot)
    {
        avatarAnimator.SetIKPositionWeight(foot, footPositionWeight);
        avatarAnimator.SetIKRotationWeight(foot, footRotationWeight);

        Vector3 footPosition = avatarAnimator.GetIKPosition(foot);
        Vector3 rayOrigin = footPosition + Vector3.up * footRaycastHeight;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                footRaycastDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore))
        {
            avatarAnimator.SetIKPosition(foot, hit.point + Vector3.up * footOffset);

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
            if (forward.sqrMagnitude > 0.001f)
            {
                Quaternion footRotation = Quaternion.LookRotation(forward, hit.normal);
                avatarAnimator.SetIKRotation(foot, footRotation);
            }
        }
    }
}
