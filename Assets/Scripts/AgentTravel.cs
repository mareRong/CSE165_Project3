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
    public float wallClearance = 0.24f;
    public float wallStopTolerance = 0.02f;
    public float avatarCollisionRadius = 0.18f;
    public float wallProbeHeight = 0.9f;
    public float blockedMoveTimeout = 0.2f;
    public float blockedMoveDistance = 0.01f;
    public bool treatTallCollidersAsWalls = true;
    public float minimumWallHeight = 0.5f;

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

    [Header("Animation")]
    public string walkingParameter = "Walking";
    public string idleStateName = "idle";
    public float minimumWalkingMotion = 0.002f;

    public event Action DestinationReached;
    public bool HasActiveDestination => hasTarget;

    private Vector3 targetPosition;
    private bool hasTarget;
    private bool isWalking;
    private Vector3 lastAnimationPosition;
    private Vector3 lastMovementCheckPosition;
    private float blockedMoveTimer;
    private bool waitingForNewDestinationAfterWallStop;

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
        ResetAnimationMovementTracking();
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
        MoveAvatarToWallThreshold(destination - GetAvatarPosition());
        targetPosition = destination;
        hasTarget = true;
        waitingForNewDestinationAfterWallStop = false;
        ResetBlockedMovementTracking();
        ResetAnimationMovementTracking();
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

    public void CompleteDestinationIfActive()
    {
        if (!hasTarget)
        {
            return;
        }

        waitingForNewDestinationAfterWallStop = false;
        FinishDestination();
    }

    public bool IsWithinWallThresholdForPoint(Vector3 point)
    {
        if (!avoidWalls || avatar == null)
        {
            return false;
        }

        Vector3 travelDirection = point - avatar.position;
        travelDirection.y = 0f;
        if (travelDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return IsAtWallStopThreshold(travelDirection) ||
               IsMovingTowardWallWithinStopThreshold(travelDirection);
    }

    public Vector3 ResolvePinnedDestination(Vector3 pinPosition)
    {
        Vector3 resolvedDestination = pinPosition;
        if (!avoidWalls || avatar == null)
        {
            return resolvedDestination;
        }

        float threshold = GetWallStopThreshold();
        if (threshold <= 0f)
        {
            return resolvedDestination;
        }

        Vector3 probeOrigin = pinPosition + Vector3.up * Mathf.Max(0f, wallProbeHeight);
        if (!TryGetClosestWallPoint(probeOrigin, threshold, out Vector3 closestWallPoint, out float wallDistance, out _))
        {
            return resolvedDestination;
        }

        Vector3 awayFromWall = probeOrigin - closestWallPoint;
        awayFromWall.y = 0f;
        if (awayFromWall.sqrMagnitude < 0.0001f)
        {
            awayFromWall = pinPosition - avatar.position;
            awayFromWall.y = 0f;
        }

        if (awayFromWall.sqrMagnitude < 0.0001f)
        {
            return resolvedDestination;
        }

        awayFromWall.Normalize();
        float correctionDistance = threshold - wallDistance;
        if (correctionDistance <= 0f)
        {
            return resolvedDestination;
        }

        resolvedDestination += awayFromWall * correctionDistance;
        return ProjectOntoGround(resolvedDestination);
    }

    private Vector3 GetAvatarPosition()
    {
        return avatar != null ? avatar.position : transform.position;
    }

    private void HandleNavMeshMovement()
    {
        if (waitingForNewDestinationAfterWallStop)
        {
            StopNavMeshAgent();
            SetWalking(false);
            return;
        }

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

        Vector3 navMoveDirection = navMeshAgent.desiredVelocity;
        navMoveDirection.y = 0f;
        if (navMoveDirection.sqrMagnitude < 0.0001f)
        {
            navMoveDirection = targetPosition - avatar.position;
            navMoveDirection.y = 0f;
        }

        if (MoveAvatarToWallThreshold(navMoveDirection))
        {
            ResetBlockedMovementTracking();
            navMoveDirection = targetPosition - avatar.position;
            navMoveDirection.y = 0f;
        }

        if (IsMovingTowardWallWithinStopThreshold(navMoveDirection))
        {
            FinishDestinationAtWallThreshold();
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

        SetWalkingFromActualMotion();
    }

    private void HandleSimpleMovement()
    {
        if (waitingForNewDestinationAfterWallStop)
        {
            SetWalking(false);
            return;
        }

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
        bool wallLimitedMove = false;
        if (MoveAvatarToWallThreshold(travelDirection))
        {
            ResetBlockedMovementTracking();
            flatTarget = new Vector3(targetPosition.x, avatar.position.y, targetPosition.z);
            direction = flatTarget - avatar.position;
            if (direction.magnitude <= stopDistance)
            {
                FinishDestination();
                return;
            }

            travelDirection = direction.normalized;
            moveDistance = Mathf.Min(moveSpeed * Time.deltaTime, direction.magnitude);
        }

        if (IsMovingTowardWallWithinStopThreshold(travelDirection))
        {
            FinishDestinationAtWallThreshold();
            return;
        }

        if (IsAtWallStopThreshold(travelDirection))
        {
            FinishDestinationAtWallThreshold();
            return;
        }

        if (TryGetWallLimitedMoveDistance(travelDirection, moveDistance, out float limitedDistance))
        {
            if (limitedDistance <= 0.001f)
            {
                FinishDestinationAtWallThreshold();
                return;
            }

            moveDistance = limitedDistance;
            wallLimitedMove = true;
        }

        Vector3 previousPosition = avatar.position;
        Vector3 nextPosition = avatar.position + travelDirection * moveDistance;
        avatar.position = ProjectOntoGround(nextPosition);
        bool actuallyMoved = DidMoveEnoughForWalking(previousPosition, avatar.position);
        if (wallLimitedMove || IsMovingTowardWallWithinStopThreshold(travelDirection))
        {
            FinishDestinationAtWallThreshold();
            return;
        }

        if (!actuallyMoved)
        {
            StopBlockedMovement();
            return;
        }

        SetWalking(actuallyMoved);
        lastAnimationPosition = avatar.position;

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

    private bool IsMovingTowardWallWithinStopThreshold(Vector3 travelDirection)
    {
        travelDirection.y = 0f;
        if (!avoidWalls || avatar == null || travelDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        travelDirection.Normalize();
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
            if (distanceToAvatarShell > threshold)
            {
                continue;
            }

            Vector3 awayFromWall = origin - closestPoint;
            awayFromWall.y = 0f;
            if (awayFromWall.sqrMagnitude < 0.0001f)
            {
                awayFromWall = travelDirection;
            }

            if (awayFromWall.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            awayFromWall.Normalize();
            if (Vector3.Dot(travelDirection, awayFromWall) <= 0f)
            {
                return true;
            }
        }

        return false;
    }

    private bool DidNotMoveEnough(Vector3 previousPosition, Vector3 currentPosition)
    {
        Vector3 movementDelta = currentPosition - previousPosition;
        movementDelta.y = 0f;
        return movementDelta.magnitude <= Mathf.Max(0.001f, blockedMoveDistance);
    }

    private bool DidMoveEnoughForWalking(Vector3 previousPosition, Vector3 currentPosition)
    {
        Vector3 movementDelta = currentPosition - previousPosition;
        movementDelta.y = 0f;
        return movementDelta.magnitude > Mathf.Max(0.0001f, minimumWalkingMotion);
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
        Vector3 normalizedTravelDirection = travelDirection.normalized;
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            radius,
            normalizedTravelDirection,
            probeDistance,
            wallLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool foundWall = false;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (!IsWallHit(hit) ||
                !IsWallBlockingTravel(hit.collider, origin, normalizedTravelDirection) ||
                hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestHit = hit;
            nearestDistance = hit.distance;
            foundWall = true;
        }

        return foundWall;
    }

    private bool TryGetClosestWallPoint(
        Vector3 origin,
        float searchRadius,
        out Vector3 closestWallPoint,
        out float closestWallDistance,
        out Collider closestWallCollider)
    {
        closestWallPoint = Vector3.zero;
        closestWallDistance = float.PositiveInfinity;
        closestWallCollider = null;
        if (searchRadius <= 0f)
        {
            return false;
        }

        Collider[] colliders = Physics.OverlapSphere(
            origin,
            searchRadius,
            wallLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider wallCollider = colliders[i];
            if (!IsWallCollider(wallCollider))
            {
                continue;
            }

            Vector3 wallPoint = wallCollider.ClosestPoint(origin);
            Vector3 flatOffset = origin - wallPoint;
            flatOffset.y = 0f;
            float wallDistance = flatOffset.magnitude;
            if (wallDistance >= closestWallDistance)
            {
                continue;
            }

            closestWallPoint = wallPoint;
            closestWallDistance = wallDistance;
            closestWallCollider = wallCollider;
        }

        return closestWallDistance < searchRadius;
    }

    private bool MoveAvatarToWallThreshold(Vector3 preferredAwayDirection)
    {
        if (!avoidWalls || avatar == null)
        {
            return false;
        }

        float minimumCenterDistance = Mathf.Max(0.01f, avatarCollisionRadius) + GetWallStopThreshold();
        Vector3 probeOrigin = avatar.position + Vector3.up * Mathf.Max(0f, wallProbeHeight);
        if (!TryGetClosestWallPoint(
                probeOrigin,
                minimumCenterDistance,
                out Vector3 closestWallPoint,
                out float wallDistance,
                out Collider closestWallCollider))
        {
            return false;
        }

        Vector3 awayFromWall = probeOrigin - closestWallPoint;
        awayFromWall.y = 0f;
        if (awayFromWall.sqrMagnitude < 0.0001f)
        {
            awayFromWall = ChooseWallEscapeDirection(closestWallCollider, probeOrigin, preferredAwayDirection);
        }

        if (awayFromWall.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        awayFromWall.Normalize();
        float correctionDistance = minimumCenterDistance - wallDistance;
        if (correctionDistance <= 0f)
        {
            return false;
        }

        Vector3 correctedPosition = ProjectOntoGround(avatar.position + awayFromWall * correctionDistance);
        avatar.position = correctedPosition;
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.Warp(correctedPosition);
        }

        return true;
    }

    private Vector3 ChooseWallEscapeDirection(Collider wallCollider, Vector3 probeOrigin, Vector3 preferredDirection)
    {
        Vector3 bestDirection = Vector3.zero;
        float bestDistance = -1f;

        TestWallEscapeDirection(wallCollider, probeOrigin, preferredDirection, ref bestDirection, ref bestDistance);
        TestWallEscapeDirection(wallCollider, probeOrigin, -preferredDirection, ref bestDirection, ref bestDistance);

        if (wallCollider != null)
        {
            TestWallEscapeDirection(wallCollider, probeOrigin, wallCollider.transform.forward, ref bestDirection, ref bestDistance);
            TestWallEscapeDirection(wallCollider, probeOrigin, -wallCollider.transform.forward, ref bestDirection, ref bestDistance);
            TestWallEscapeDirection(wallCollider, probeOrigin, wallCollider.transform.right, ref bestDirection, ref bestDistance);
            TestWallEscapeDirection(wallCollider, probeOrigin, -wallCollider.transform.right, ref bestDirection, ref bestDistance);
        }

        return bestDirection;
    }

    private void TestWallEscapeDirection(
        Collider wallCollider,
        Vector3 probeOrigin,
        Vector3 candidateDirection,
        ref Vector3 bestDirection,
        ref float bestDistance)
    {
        candidateDirection.y = 0f;
        if (candidateDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        candidateDirection.Normalize();
        Vector3 candidateOrigin = probeOrigin + candidateDirection * Mathf.Max(0.05f, GetWallStopThreshold());
        float candidateDistance = GetFlatDistanceFromWall(wallCollider, candidateOrigin);
        if (candidateDistance <= bestDistance)
        {
            return;
        }

        bestDistance = candidateDistance;
        bestDirection = candidateDirection;
    }

    private static float GetFlatDistanceFromWall(Collider wallCollider, Vector3 origin)
    {
        if (wallCollider == null)
        {
            return -1f;
        }

        Vector3 closestPoint = wallCollider.ClosestPoint(origin);
        Vector3 flatOffset = origin - closestPoint;
        flatOffset.y = 0f;
        return flatOffset.magnitude;
    }

    private bool IsWallHit(RaycastHit hit)
    {
        return IsWallCollider(hit.collider);
    }

    private bool IsWallBlockingTravel(Collider wallCollider, Vector3 origin, Vector3 travelDirection)
    {
        if (wallCollider == null)
        {
            return false;
        }

        Vector3 awayFromWall = origin - wallCollider.ClosestPoint(origin);
        awayFromWall.y = 0f;
        if (awayFromWall.sqrMagnitude < 0.0001f)
        {
            awayFromWall = -wallCollider.transform.forward;
            awayFromWall.y = 0f;
        }

        if (awayFromWall.sqrMagnitude < 0.0001f)
        {
            return true;
        }

        awayFromWall.Normalize();
        return Vector3.Dot(travelDirection, awayFromWall) <= 0f;
    }

    private bool IsWallCollider(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        SpatialSurfaceMarker marker = collider.GetComponentInParent<SpatialSurfaceMarker>();
        if (marker != null)
        {
            return marker.SurfaceKind == SpatialSurfaceAnchorManager.SurfaceKind.Wall;
        }

        AgentTravel agentTravel = collider.GetComponentInParent<AgentTravel>();
        if (agentTravel != null)
        {
            return false;
        }

        Bounds bounds = collider.bounds;
        return treatTallCollidersAsWalls &&
               bounds.size.y >= Mathf.Max(0.01f, minimumWallHeight) &&
               bounds.size.y > Mathf.Min(bounds.size.x, bounds.size.z);
    }

    private void StopBlockedMovement()
    {
        hasTarget = false;
        waitingForNewDestinationAfterWallStop = true;
        SetWalking(false);
        StopNavMeshAgent();
        ResetBlockedMovementTracking();
        ResetAnimationMovementTracking();
    }

    private void FinishDestinationAtWallThreshold()
    {
        waitingForNewDestinationAfterWallStop = false;
        FinishDestination();
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
        ResetAnimationMovementTracking();
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

    private void ResetAnimationMovementTracking()
    {
        lastAnimationPosition = avatar != null ? avatar.position : transform.position;
    }

    private void SetWalkingFromActualMotion()
    {
        if (!hasTarget || avatar == null)
        {
            SetWalking(false);
            ResetAnimationMovementTracking();
            return;
        }

        bool actuallyMoved = DidMoveEnoughForWalking(lastAnimationPosition, avatar.position);
        SetWalking(actuallyMoved);
        lastAnimationPosition = avatar.position;
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
        bool walkingChanged = isWalking != walking;
        isWalking = walking;

        if (avatarAnimator != null)
        {
            avatarAnimator.SetBool(walkingParameter, walking);

            if (walkingChanged && !walking && !string.IsNullOrEmpty(idleStateName))
            {
                avatarAnimator.CrossFade(idleStateName, 0.05f);
            }
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
