// Predictive Redirected Walking using Lemniscate Path Prediction (PredRedLPP)
// Based on: "Predictive multiuser redirected walking using artificial potential fields"
// (Hirt et al., 2024)
// Paper: https://www.frontiersin.org/articles/10.3389/frvir.2024.1365344/full

using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// PredRedLPP redirector that combines predictive path planning with artificial potential fields.
/// This algorithm predicts user trajectories using lemniscate-based clothoid curves and evaluates
/// redirection actions using APF-based cost functions.
/// </summary>
public class PredRedLPP_Redirector : APF_Redirector
{
    [Header("PredRedLPP Components")]
    private LemniscatePathPredictor pathPredictor;
    private TrajectoryEvaluator trajectoryEvaluator;
    private PathSmoother smoother;

    [Header("Movement History")]
    private Queue<Vector3> positionHistory;
    private Queue<Vector3> directionHistory;
    private const int MAX_HISTORY_SIZE = 10;

    [Header("Current State")]
    private Trajectory currentBestTrajectory;

    [Header("Visualization")]
    private GameObject trajectoryVisualizer;
    private LineRenderer trajectoryLineRenderer;
    private GameObject lemniscateVisualizer;
    private LineRenderer lemniscateLineRenderer;

    [Header("Debug")]
    [Tooltip("Show debug information in console")]
    public bool showDebugInfo = true;
    [Tooltip("Visualize predicted trajectories in scene view")]
    public bool visualizePredictions = true;

    private bool isInitialized = false;

    void Start()
    {
        EnsureInitialized();
    }

    /// <summary>
    /// Ensures components are initialized (lazy initialization).
    /// </summary>
    private void EnsureInitialized()
    {
        if (isInitialized)
            return;

        InitializeComponents();
        InitializeHistoryBuffers();

        isInitialized = true;
    }

    /// <summary>
    /// Initializes all prediction and evaluation components.
    /// </summary>
    private void InitializeComponents()
    {
        // Add path predictor component
        pathPredictor = gameObject.GetComponent<LemniscatePathPredictor>();
        if (pathPredictor == null)
        {
            pathPredictor = gameObject.AddComponent<LemniscatePathPredictor>();
        }

        // Initialize trajectory evaluator
        trajectoryEvaluator = new TrajectoryEvaluator(globalConfiguration);

        // Initialize path smoother (disabled by default for simulation)
        smoother = new PathSmoother();
        smoother.SetEnabled(globalConfiguration.enablePathSmoothing);

        // Initialize visualization GameObjects
        InitializeVisualization();

        if (showDebugInfo)
        {
            Debug.Log("PredRedLPP: Components initialized");
        }
    }

    /// <summary>
    /// Initializes visualization GameObjects (similar to APF_Redirector pattern).
    /// </summary>
    private void InitializeVisualization()
    {
        if (globalConfiguration.runInBackstage || !visualizePredictions)
            return;

        // Create trajectory visualizer
        trajectoryVisualizer = new GameObject("PredRedLPP_Trajectory");
        trajectoryVisualizer.transform.SetParent(transform);
        trajectoryVisualizer.transform.localPosition = Vector3.zero;
        trajectoryVisualizer.transform.localRotation = Quaternion.identity;

        trajectoryLineRenderer = trajectoryVisualizer.AddComponent<LineRenderer>();
        trajectoryLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        trajectoryLineRenderer.startColor = Color.green;
        trajectoryLineRenderer.endColor = Color.green;
        trajectoryLineRenderer.startWidth = 0.1f;
        trajectoryLineRenderer.endWidth = 0.1f;
        trajectoryLineRenderer.positionCount = 0;
        trajectoryLineRenderer.useWorldSpace = false; // Use local space coordinates (follows parent)
        trajectoryLineRenderer.enabled = visualizationManager.ifVisible;

        // Create lemniscate visualizer
        lemniscateVisualizer = new GameObject("PredRedLPP_Lemniscate");
        lemniscateVisualizer.transform.SetParent(transform);
        lemniscateVisualizer.transform.localPosition = Vector3.zero;
        lemniscateVisualizer.transform.localRotation = Quaternion.identity;

        lemniscateLineRenderer = lemniscateVisualizer.AddComponent<LineRenderer>();
        lemniscateLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lemniscateLineRenderer.startColor = Color.yellow;
        lemniscateLineRenderer.endColor = Color.yellow;
        lemniscateLineRenderer.startWidth = 0.05f;
        lemniscateLineRenderer.endWidth = 0.05f;
        lemniscateLineRenderer.positionCount = 0;
        lemniscateLineRenderer.useWorldSpace = false; // Use local space coordinates (follows parent)
        lemniscateLineRenderer.enabled = visualizationManager.ifVisible;
    }

    /// <summary>
    /// Cleanup visualization GameObjects.
    /// </summary>
    private void OnDestroy()
    {
        if (trajectoryVisualizer != null)
            Destroy(trajectoryVisualizer);
        if (lemniscateVisualizer != null)
            Destroy(lemniscateVisualizer);
    }

    /// <summary>
    /// Initializes position and direction history buffers.
    /// </summary>
    private void InitializeHistoryBuffers()
    {
        positionHistory = new Queue<Vector3>();
        directionHistory = new Queue<Vector3>();
    }

    /// <summary>
    /// Main redirection injection method called every frame.
    /// Implements the full PredRedLPP algorithm pipeline.
    /// </summary>
    public override void InjectRedirection()
    {
        // Ensure components are initialized
        EnsureInitialized();

        // Step 1: Update movement history
        UpdateHistory();

        // Step 2: Apply smoothing if enabled (for HMD use)
        Queue<Vector3> smoothedPositions = positionHistory;
        Queue<Vector3> smoothedDirections = directionHistory;

        if (globalConfiguration.enablePathSmoothing)
        {
            var smoothedPosList = smoother.SmoothPositions(positionHistory);
            var smoothedDirList = smoother.SmoothDirections(directionHistory);

            smoothedPositions = new Queue<Vector3>(smoothedPosList);
            smoothedDirections = new Queue<Vector3>(smoothedDirList);
        }

        // Step 3: Generate predicted trajectories
        List<Trajectory> predictions = pathPredictor.GenerateTrajectories(
            smoothedPositions,
            smoothedDirections,
            redirectionManager.currPosReal,
            redirectionManager.currDirReal
        );

        if (showDebugInfo)
        {
            Debug.Log($"PredRedLPP: Generated {predictions.Count} predictions");
        }

        if (predictions.Count == 0)
        {
            // No predictions available, apply null action
            if (showDebugInfo)
            {
                Debug.LogWarning("PredRedLPP: No predictions generated, applying null action");
            }
            ApplyNullRedirection();
            return;
        }

        // Step 4: Filter feasible trajectories (scene awareness)
        SingleSpace physicalSpace = globalConfiguration.physicalSpaces[movementManager.physicalSpaceIndex];
        List<Trajectory> feasibleTrajectories = pathPredictor.FilterFeasibleTrajectories(
            predictions,
            physicalSpace
        );

        if (showDebugInfo)
        {
            Debug.Log($"PredRedLPP: {feasibleTrajectories.Count} feasible trajectories (from {predictions.Count})");
        }

        if (feasibleTrajectories.Count == 0)
        {
            // No feasible trajectories, fall back to reactive behavior
            if (showDebugInfo)
            {
                Debug.LogWarning("PredRedLPP: No feasible trajectories, falling back to reactive mode");
            }
            ApplyReactiveRedirection(physicalSpace);
            return;
        }

        // Step 5: Select best trajectory based on cost
        Trajectory bestTrajectory = SelectBestTrajectory(
            feasibleTrajectories,
            physicalSpace
        );

        if (bestTrajectory == null)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning("PredRedLPP: No valid trajectory found");
            }
            ApplyNullRedirection();
            return;
        }

        currentBestTrajectory = bestTrajectory;

        // Step 6: Apply redirection reactively based on best trajectory
        // Use the trajectory direction to guide the user (similar to ThomasAPF approach)
        ApplyRedirectionFromTrajectory(bestTrajectory, physicalSpace);

        // Step 7: Update APF visualization
        if (bestTrajectory != null && bestTrajectory.points.Count > 0)
        {
            // Calculate APF force at current position for visualization
            Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal);
            Vector2 apfForce = CalculateAPFForceAtPosition(currentPos2D, physicalSpace);
            UpdateTotalForcePointer(apfForce);
        }

        // Step 8: Update trajectory visualization
        UpdateVisualization();
    }

    /// <summary>
    /// Updates movement history buffers.
    /// </summary>
    private void UpdateHistory()
    {
        positionHistory.Enqueue(redirectionManager.currPosReal);
        directionHistory.Enqueue(redirectionManager.currDirReal);

        // Limit buffer size
        while (positionHistory.Count > MAX_HISTORY_SIZE)
        {
            positionHistory.Dequeue();
        }
        while (directionHistory.Count > MAX_HISTORY_SIZE)
        {
            directionHistory.Dequeue();
        }
    }

    /// <summary>
    /// Selects the best trajectory based on cost evaluation.
    /// </summary>
    private Trajectory SelectBestTrajectory(
        List<Trajectory> trajectories,
        SingleSpace physicalSpace)
    {
        if (trajectories.Count == 0)
            return null;

        float minCost = float.MaxValue;
        Trajectory bestTrajectory = null;

        foreach (var trajectory in trajectories)
        {
            // Calculate cost for this trajectory
            float cost = trajectoryEvaluator.CalculateTotalCost(
                trajectory,
                physicalSpace,
                globalConfiguration.redirectedAvatars,
                movementManager.physicalSpaceIndex,
                movementManager.avatarId
            );

            trajectory.totalCost = cost;

            if (cost < minCost)
            {
                minCost = cost;
                bestTrajectory = trajectory;
            }
        }

        if (showDebugInfo && bestTrajectory != null)
        {
            Debug.Log($"PredRedLPP: Selected trajectory with cost={minCost:F3}");
        }

        return bestTrajectory;
    }

    /// <summary>
    /// Applies redirection based on the selected trajectory.
    /// Uses a reactive approach similar to ThomasAPF: always set all three gains.
    /// </summary>
    private void ApplyRedirectionFromTrajectory(
        Trajectory trajectory,
        SingleSpace physicalSpace)
    {
        if (trajectory == null || trajectory.points.Count < 2)
        {
            ApplyNullRedirection();
            return;
        }

        // Get desired direction from trajectory (first few points)
        Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal);
        Vector2 currentDir2D = Utilities.FlattenedDir2D(redirectionManager.currDirReal);

        // Find the target point on trajectory (a few steps ahead)
        int targetIndex = Mathf.Min(3, trajectory.points.Count - 1);
        Vector2 targetPoint = trajectory.points[targetIndex];

        // Calculate desired direction
        Vector2 desiredDir2D = (targetPoint - currentPos2D).normalized;
        Vector3 desiredFacingDirection = Utilities.UnFlatten(desiredDir2D);

        // Calculate steering direction (similar to ThomasAPF)
        int desiredSteeringDirection = (-1) * (int)Mathf.Sign(
            Utilities.GetSignedAngle(redirectionManager.currDirReal, desiredFacingDirection)
        );

        // Set Translation Gain based on alignment with desired direction
        float alignment = Vector2.Dot(desiredDir2D, currentDir2D);
        if (alignment < 0)
        {
            // Moving away from desired direction - compress to slow down
            SetTranslationGain(globalConfiguration.MIN_TRANS_GAIN);
        }
        else if (alignment > 0.9f)
        {
            // Well aligned - use maximum translation
            SetTranslationGain(globalConfiguration.MAX_TRANS_GAIN);
        }
        else
        {
            // Partially aligned - neutral
            SetTranslationGain(1.0f);
        }

        // Set Rotation Gain based on turning direction
        if (redirectionManager.isRotating)
        {
            if (redirectionManager.deltaDir * desiredSteeringDirection < 0)
            {
                // Rotating away from desired direction
                SetRotationGain(globalConfiguration.MIN_ROT_GAIN);
            }
            else
            {
                // Rotating towards desired direction
                SetRotationGain(globalConfiguration.MAX_ROT_GAIN);
            }
        }
        else
        {
            SetRotationGain(1.0f);
        }

        // Set Curvature Gain to steer towards trajectory
        if (redirectionManager.isWalking)
        {
            SetCurvature(desiredSteeringDirection * 1f / globalConfiguration.CURVATURE_RADIUS);
        }
        else
        {
            SetCurvature(0f);
        }

        // Apply all gains
        ApplyGains();

        if (showDebugInfo)
        {
            Debug.Log($"PredRedLPP: Applied gains - T={redirectionManager.gt:F3}, " +
                     $"R={redirectionManager.gr:F3}, C={redirectionManager.curvature:F3}");
        }
    }

    /// <summary>
    /// Applies null redirection (no manipulation).
    /// </summary>
    private void ApplyNullRedirection()
    {
        // Set all gains to neutral values (like NullRedirector)
        SetTranslationGain(1.0f);
        SetRotationGain(1.0f);
        SetCurvature(0f);
        ApplyGains();
    }

    /// <summary>
    /// Fallback reactive redirection when no feasible predictions exist.
    /// Uses ThomasAPF-style reactive behavior.
    /// </summary>
    private void ApplyReactiveRedirection(SingleSpace physicalSpace)
    {
        // Calculate APF force
        Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal);
        Vector2 ng = CalculateAPFForceAtPosition(currentPos2D, physicalSpace);

        // Apply reactive redirection similar to ThomasAPF
        ApplyRedirectionByNegativeGradient(ng);

        UpdateTotalForcePointer(ng);

        if (showDebugInfo)
        {
            Debug.Log("PredRedLPP: Fallback to reactive mode");
        }
    }

    /// <summary>
    /// Calculates APF repulsive force at a position.
    /// </summary>
    private Vector2 CalculateAPFForceAtPosition(Vector2 position, SingleSpace physicalSpace)
    {
        List<Vector2> nearestPosList = new List<Vector2>();

        // Physical borders
        for (int i = 0; i < physicalSpace.trackingSpace.Count; i++)
        {
            var p = physicalSpace.trackingSpace[i];
            var q = physicalSpace.trackingSpace[(i + 1) % physicalSpace.trackingSpace.Count];
            var nearestPos = Utilities.GetNearestPos(position, new List<Vector2> { p, q });
            var n = Utilities.RotateVector(q - p, -90).normalized;
            var d = position - nearestPos;

            if (Vector2.Dot(n, d.normalized) > 0)
            {
                nearestPosList.Add(nearestPos);
            }
        }

        // Obstacles
        foreach (var obstacle in physicalSpace.obstaclePolygons)
        {
            var nearestPos = Utilities.GetNearestPos(position, obstacle);
            nearestPosList.Add(nearestPos);
        }

        // Other users
        foreach (var user in globalConfiguration.redirectedAvatars)
        {
            if (user.GetComponent<MovementManager>().physicalSpaceIndex != movementManager.physicalSpaceIndex)
                continue;

            var uId = user.GetComponent<MovementManager>().avatarId;
            if (uId == movementManager.avatarId)
                continue;

            var nearestPos = Utilities.FlattenedPos2D(user.GetComponent<RedirectionManager>().currPosReal);
            nearestPosList.Add(nearestPos);
        }

        // Calculate negative gradient
        Vector2 ng = Vector2.zero;
        foreach (var obPos in nearestPosList)
        {
            Vector2 diff = position - obPos;
            float distance = diff.magnitude;

            if (distance > 0.01f)
            {
                var gDelta = -1f / distance * diff.normalized;
                ng += -gDelta;
            }
        }

        return ng.normalized;
    }

    /// <summary>
    /// Applies redirection based on negative gradient (reactive mode).
    /// Exactly mirrors ThomasAPF behavior - sets all three gains.
    /// </summary>
    private void ApplyRedirectionByNegativeGradient(Vector2 ng)
    {
        // Calculate desired facing direction from negative gradient
        var desiredFacingDirection = Utilities.UnFlatten(ng);
        int desiredSteeringDirection = (-1) * (int)Mathf.Sign(
            Utilities.GetSignedAngle(redirectionManager.currDirReal, desiredFacingDirection)
        );

        // Translation gain (exactly like ThomasAPF)
        if (Vector2.Dot(ng, Utilities.FlattenedDir2D(redirectionManager.currDirReal)) < 0)
        {
            SetTranslationGain(globalConfiguration.MAX_TRANS_GAIN);
        }
        else
        {
            SetTranslationGain(1f);
        }

        // Rotation gain (exactly like ThomasAPF)
        if (redirectionManager.deltaDir * desiredSteeringDirection < 0)
        {
            // Rotating away from negative gradient
            SetRotationGain(globalConfiguration.MIN_ROT_GAIN);
        }
        else
        {
            // Rotating towards negative gradient
            SetRotationGain(globalConfiguration.MAX_ROT_GAIN);
        }

        // Curvature gain (exactly like ThomasAPF)
        SetCurvature(desiredSteeringDirection * 1f / globalConfiguration.CURVATURE_RADIUS);

        // Apply all gains
        ApplyGains();
    }

    /// <summary>
    /// Updates visualization using LineRenderer (follows avatar like APF arrow).
    /// </summary>
    private void UpdateVisualization()
    {
        if (!visualizePredictions || globalConfiguration.runInBackstage)
            return;

        if (trajectoryLineRenderer == null || lemniscateLineRenderer == null)
            return;

        // Update visibility
        bool isVisible = visualizationManager.ifVisible;
        trajectoryLineRenderer.enabled = isVisible;
        lemniscateLineRenderer.enabled = isVisible;

        if (!isVisible)
            return;

        // Get current position and direction in world coordinates
        Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal);
        Vector2 currentDir2D = Utilities.FlattenedDir2D(redirectionManager.currDirReal).normalized;

        // Update lemniscate visualization
        UpdateLemniscateVisualization(currentPos2D, currentDir2D);

        // Update trajectory visualization
        UpdateTrajectoryVisualization();
    }

    /// <summary>
    /// Updates lemniscate shape visualization.
    /// </summary>
    private void UpdateLemniscateVisualization(Vector2 origin, Vector2 direction)
    {
        // Generate lemniscate curve points
        var lemniscatePoints = pathPredictor.GenerateLemniscatePointsForVisualization(
            origin,
            direction,
            50  // Number of points for smooth curve
        );

        if (lemniscatePoints.Count == 0)
        {
            lemniscateLineRenderer.positionCount = 0;
            return;
        }

        // Convert to local coordinates (useWorldSpace = false, parent follows avatar)
        Vector3[] localPositions = new Vector3[lemniscatePoints.Count];
        for (int i = 0; i < lemniscatePoints.Count; i++)
        {
            // Convert 2D to 3D world position
            Vector3 worldPos = Utilities.UnFlatten(lemniscatePoints[i]);
            // Transform world position to local space of parent (avatar)
            localPositions[i] = transform.InverseTransformPoint(worldPos);
        }

        lemniscateLineRenderer.positionCount = localPositions.Length;
        lemniscateLineRenderer.SetPositions(localPositions);
    }

    /// <summary>
    /// Updates best trajectory visualization.
    /// </summary>
    private void UpdateTrajectoryVisualization()
    {
        if (currentBestTrajectory == null || currentBestTrajectory.points.Count == 0)
        {
            trajectoryLineRenderer.positionCount = 0;
            return;
        }

        // Convert trajectory points to local coordinates (useWorldSpace = false)
        Vector3[] localPositions = new Vector3[currentBestTrajectory.points.Count];
        for (int i = 0; i < currentBestTrajectory.points.Count; i++)
        {
            // Convert 2D to 3D world position
            Vector3 worldPos = Utilities.UnFlatten(currentBestTrajectory.points[i]);
            // Transform world position to local space of parent (avatar)
            localPositions[i] = transform.InverseTransformPoint(worldPos);
        }

        trajectoryLineRenderer.positionCount = localPositions.Length;
        trajectoryLineRenderer.SetPositions(localPositions);
    }
}
