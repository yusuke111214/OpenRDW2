// ThomasAPF with Attractive Forces for Local Minima Avoidance
// Extends ThomasAPF_Redirector by adding attractive forces towards open spaces
// Based on:
// - Thomas & Rosenberg (2019) - A general reactive algorithm for RDW using APF
// - Dong et al. (2020) - Dynamic Artificial Potential Fields for Multi-User RDW
// - Khatib (1986) - Real-time obstacle avoidance for manipulators and mobile robots

using UnityEngine;
using System.Collections.Generic;

public class ThomasAPF_WithAttraction_Redirector : ThomasAPF_Redirector
{
    // APF Components
    private OpenSpaceDetector openSpaceDetector;
    private SteeringTargetSelector targetSelector;
    private AttractivePotentialField attractiveField;
    private CombinedAPF combinedAPF;

    // Visualization
    private GameObject attractiveForcePointer;
    private GameObject steeringTargetMarker;

    // State
    private bool isInitialized = false;

    // Initialization
    void Start()
    {
        InitializeAttractionComponents();
    }

    private void InitializeAttractionComponents()
    {
        if (isInitialized)
            return;

        // Create open space detector
        openSpaceDetector = new OpenSpaceDetector(
            globalConfiguration,
            redirectionManager,
            movementManager
        );

        // Create steering target selector
        targetSelector = new SteeringTargetSelector(
            openSpaceDetector,
            globalConfiguration.attractionTargetUpdateInterval
        );

        // Create attractive potential field
        attractiveField = new AttractivePotentialField(
            globalConfiguration.attractionStrength,
            globalConfiguration.attractionSaturationDistance,
            globalConfiguration.attractionVelocityWeight
        );

        // Create combined APF
        combinedAPF = new CombinedAPF(
            attractiveField,
            targetSelector,
            globalConfiguration.repulsiveWeight,
            globalConfiguration.attractiveWeight,
            globalConfiguration.useAdaptiveWeights
        );

        // Set adaptive thresholds if enabled
        if (globalConfiguration.useAdaptiveWeights)
        {
            combinedAPF.SetAdaptiveDistanceThresholds(
                globalConfiguration.adaptiveCloseDistance,
                globalConfiguration.adaptiveFarDistance
            );
        }

        isInitialized = true;
    }

    public override void InjectRedirection()
    {
        // Ensure initialization (in case Start() hasn't been called yet)
        if (!isInitialized)
        {
            InitializeAttractionComponents();
        }

        // Get physical spaces
        var physicalSpaces = globalConfiguration.physicalSpaces;

        // Calculate repulsive force and negative gradient (from parent class)
        GetRepulsiveForceAndNegativeGradient(physicalSpaces, out float rf, out Vector2 repulsiveGradient);

        // Get user state
        Vector3 userPosition = redirectionManager.currPosReal;
        Vector3 userVelocity = redirectionManager.deltaPos / Time.deltaTime;

        // Calculate minimum distance to obstacles (for adaptive weighting)
        float minDistToObstacle = CalculateMinDistanceToObstacle(userPosition);

        // Combine repulsive and attractive forces
        Vector2 attractiveForce;
        float repWeight, attWeight;
        Vector3 steeringTarget;

        Vector2 combinedForce = combinedAPF.CalculateTotalForceWithDebugInfo(
            userPosition,
            userVelocity,
            repulsiveGradient,
            minDistToObstacle,
            out attractiveForce,
            out repWeight,
            out attWeight,
            out steeringTarget
        );

        // Apply redirection using combined force
        ApplyRedirectionByNegativeGradient(combinedForce);

        // Update visualization
        UpdateVisualization(
            userPosition,
            repulsiveGradient,
            attractiveForce,
            combinedForce,
            steeringTarget,
            repWeight,
            attWeight
        );
    }

    /// <summary>
    /// Calculates minimum distance to any obstacle from user position.
    /// Used for adaptive weight determination.
    /// </summary>
    private float CalculateMinDistanceToObstacle(Vector3 userPosition)
    {
        Vector2 pos2D = Utilities.FlattenedPos2D(userPosition);
        int userIndex = movementManager.physicalSpaceIndex;
        SingleSpace space = globalConfiguration.physicalSpaces[userIndex];

        float minDistance = float.MaxValue;

        // Check distance to tracking space boundaries
        for (int i = 0; i < space.trackingSpace.Count; i++)
        {
            Vector2 p = space.trackingSpace[i];
            Vector2 q = space.trackingSpace[(i + 1) % space.trackingSpace.Count];
            Vector2 nearestPos = Utilities.GetNearestPos(pos2D, new List<Vector2> { p, q });

            float dist = Vector2.Distance(pos2D, nearestPos);
            if (dist < minDistance)
            {
                minDistance = dist;
            }
        }

        // Check distance to obstacles
        foreach (var obstacle in space.obstaclePolygons)
        {
            Vector2 nearestPos = Utilities.GetNearestPos(pos2D, obstacle);
            float dist = Vector2.Distance(pos2D, nearestPos);
            if (dist < minDistance)
            {
                minDistance = dist;
            }
        }

        // Check distance to other users
        foreach (var user in globalConfiguration.redirectedAvatars)
        {
            if (user.GetComponent<MovementManager>().physicalSpaceIndex != userIndex)
                continue;

            var uId = user.GetComponent<MovementManager>().avatarId;
            if (uId == movementManager.avatarId)
                continue;

            Vector2 otherUserPos = Utilities.FlattenedPos2D(
                user.GetComponent<RedirectionManager>().currPosReal
            );

            float dist = Vector2.Distance(pos2D, otherUserPos);
            if (dist < minDistance)
            {
                minDistance = dist;
            }
        }

        return minDistance;
    }

    /// <summary>
    /// Updates visualization of forces and targets.
    /// </summary>
    private void UpdateVisualization(
        Vector3 userPosition,
        Vector2 repulsiveForce,
        Vector2 attractiveForce,
        Vector2 combinedForce,
        Vector3 steeringTarget,
        float repWeight,
        float attWeight)
    {
        if (!globalConfiguration.enableAttractionVisualization || globalConfiguration.runInBackstage)
        {
            return;
        }

        // Visualize attractive force
        if (attractiveForcePointer == null)
        {
            attractiveForcePointer = Instantiate(globalConfiguration.negArrow);
            attractiveForcePointer.transform.SetParent(transform);

            // Color it differently from repulsive force (cyan for attraction)
            foreach (var renderer in attractiveForcePointer.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.material.color = Color.cyan;
                renderer.enabled = visualizationManager.ifVisible;
            }
        }

        if (attractiveForcePointer != null && globalConfiguration.showAttractiveForce)
        {
            attractiveForcePointer.SetActive(visualizationManager.ifVisible);

            // Use currPos (world/virtual coordinates) instead of userPosition (physical local coordinates)
            // This ensures the arrow follows the avatar even when TrackingSpace moves/rotates
            attractiveForcePointer.transform.position = redirectionManager.currPos;

            if (attractiveForce.magnitude > 0.01f)
            {
                // Apply parent rotation to the force direction
                // This matches the approach used in APF_Redirector.UpdateTotalForcePointer()
                attractiveForcePointer.transform.forward = transform.rotation * Utilities.UnFlatten(attractiveForce);

                // Scale based on magnitude
                float scale = Mathf.Clamp(attractiveForce.magnitude * 0.5f, 0.5f, 2.0f);
                attractiveForcePointer.transform.localScale = Vector3.one * scale;
            }
        }

        // Visualize steering target
        if (steeringTargetMarker == null && globalConfiguration.showSteeringTarget)
        {
            steeringTargetMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            steeringTargetMarker.transform.SetParent(transform);
            steeringTargetMarker.transform.localScale = Vector3.one * 0.3f;

            var renderer = steeringTargetMarker.GetComponent<MeshRenderer>();
            renderer.material.color = Color.yellow;
            renderer.enabled = visualizationManager.ifVisible;

            // Remove collider
            Destroy(steeringTargetMarker.GetComponent<Collider>());
        }

        if (steeringTargetMarker != null && globalConfiguration.showSteeringTarget)
        {
            steeringTargetMarker.SetActive(visualizationManager.ifVisible);

            // Convert steering target from physical space (TrackingSpace local coordinates)
            // to world coordinates for proper visualization
            // steeringTarget is calculated in physical space, so we need to transform it
            Vector3 targetWorldPos = redirectionManager.trackingSpace.TransformPoint(steeringTarget);
            steeringTargetMarker.transform.position = targetWorldPos;
        }

        // Update the total force pointer (from parent class) with combined force
        UpdateTotalForcePointer(combinedForce);

        // Debug log (optional, can be disabled in production)
        if (globalConfiguration.logAttractionDebugInfo && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[APF+Attraction] Rep: {repulsiveForce.magnitude:F3} (w={repWeight:F2}), " +
                      $"Att: {attractiveForce.magnitude:F3} (w={attWeight:F2}), " +
                      $"Combined: {combinedForce.magnitude:F3}");
        }
    }

    private void OnDestroy()
    {
        // Clean up visualization objects
        if (attractiveForcePointer != null)
            Destroy(attractiveForcePointer);

        if (steeringTargetMarker != null)
            Destroy(steeringTargetMarker);
    }

    /// <summary>
    /// Forces update of steering target.
    /// Useful when user resets or enters a new area.
    /// </summary>
    public void ForceUpdateSteeringTarget()
    {
        if (isInitialized && targetSelector != null)
        {
            targetSelector.ForceUpdate(redirectionManager.currPosReal);
        }
    }
}
