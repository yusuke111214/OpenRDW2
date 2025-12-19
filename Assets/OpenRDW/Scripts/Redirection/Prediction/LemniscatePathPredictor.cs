using UnityEngine;
using System.Collections.Generic;
using Curves;

/// <summary>
/// Lemniscate-based Path Predictor (LPP) for PredRedLPP algorithm.
/// Generates predicted trajectories by creating clothoid curves to endpoints
/// distributed on a lemniscate shape ahead of the user.
/// Based on: "Predictive multiuser redirected walking using artificial potential fields" (Hirt et al., 2024)
/// </summary>
public class LemniscatePathPredictor : MonoBehaviour, IPathPredictor
{
    [Header("Prediction Parameters")]
    [Tooltip("Prediction horizon in meters (A_Lem in the paper, typically 3m)")]
    public float predictionHorizon = 3f;

    [Tooltip("Number of endpoints on the lemniscate (odd number recommended for symmetry)")]
    public int lemniscateEndpoints = 11;

    [Tooltip("Number of sample points per trajectory")]
    public int trajectorySamplePoints = 20;

    [Tooltip("Lookback steps for prediction start point (t-T in the paper, 0 = use current position)")]
    public int lookbackSteps = 0; // Use current position for simulation environments

    private GlobalConfiguration globalConfiguration;

    void Awake()
    {
        globalConfiguration = GetComponentInParent<GlobalConfiguration>();
    }

    void Start()
    {
        // Load parameters from GlobalConfiguration if available
        if (globalConfiguration != null)
        {
            predictionHorizon = globalConfiguration.predictionHorizon;
            lemniscateEndpoints = globalConfiguration.lemniscateEndpoints;
        }
    }

    /// <summary>
    /// Generates predicted trajectories using the Lemniscate-based approach.
    /// </summary>
    public List<Trajectory> GenerateTrajectories(
        Queue<Vector3> positionHistory,
        Queue<Vector3> directionHistory,
        Vector3 currentPosition,
        Vector3 currentDirection)
    {
        List<Trajectory> trajectories = new List<Trajectory>();

        // Determine prediction starting point (t-T from the paper)
        // Note: Queue.ToArray() returns oldest first, newest last
        Vector3 predictionStartPos;
        Vector3 predictionStartDir;

        if (positionHistory.Count >= lookbackSteps && lookbackSteps > 0)
        {
            // Use historical position from T steps ago
            // Queue order: [oldest, ..., newest]
            Vector3[] posArray = positionHistory.ToArray();
            Vector3[] dirArray = directionHistory.ToArray();

            // Get position T steps back from the end (most recent)
            int lookbackIndex = posArray.Length - lookbackSteps;
            if (lookbackIndex < 0) lookbackIndex = 0;

            predictionStartPos = posArray[lookbackIndex];
            predictionStartDir = dirArray[lookbackIndex];
        }
        else
        {
            // Not enough history, use current pose
            predictionStartPos = currentPosition;
            predictionStartDir = currentDirection;
        }

        // Convert to 2D (XZ plane)
        Vector2 startPos2D = Utilities.FlattenedPos2D(predictionStartPos);
        Vector2 startDir2D = Utilities.FlattenedDir2D(predictionStartDir).normalized;

        // Generate lemniscate endpoints
        List<Vector2> endpoints = GenerateLemniscatePoints(
            startPos2D,
            startDir2D,
            predictionHorizon,
            lemniscateEndpoints
        );

        // Generate clothoid trajectories to each endpoint
        int validCount = 0;
        foreach (var endpoint in endpoints)
        {
            Clothoid clothoid = CurvesWrapper.CreateClothoidFromPoseAndPoint(
                startPos2D,
                startDir2D,
                endpoint
            );

            if (CurvesWrapper.IsValidClothoid(clothoid))
            {
                Trajectory trajectory = new Trajectory(clothoid, trajectorySamplePoints);
                trajectories.Add(trajectory);
                validCount++;
            }
        }

        // Debug: Log trajectory generation (can be commented out for performance)
        // Debug.Log($"LemniscatePredictor: Generated {validCount}/{endpoints.Count} valid clothoid trajectories");

        return trajectories;
    }

    /// <summary>
    /// Generates endpoints distributed on a lemniscate curve.
    /// Lemniscate equation (Eq. 1 in the paper):
    /// x(t) = A_Lem * sin(t) / (1 + cos^2(t))
    /// y(t) = A_Lem * sin(t)cos(t) / (1 + cos^2(t))
    /// where t ∈ (0, π)
    /// </summary>
    /// <param name="origin">Origin point of the lemniscate</param>
    /// <param name="direction">Forward direction (determines lemniscate orientation)</param>
    /// <param name="size">Lemniscate size parameter (A_Lem)</param>
    /// <param name="numPoints">Number of endpoint samples</param>
    /// <returns>List of endpoint positions in world coordinates</returns>
    private List<Vector2> GenerateLemniscatePoints(
        Vector2 origin,
        Vector2 direction,
        float size,
        int numPoints)
    {
        List<Vector2> endpoints = new List<Vector2>();

        // Sample parameter t over (0, π)
        // Skip t=0 and t=π to avoid singularities
        float tMin = 0.05f;
        float tMax = Mathf.PI - 0.05f;

        for (int i = 0; i < numPoints; i++)
        {
            // Uniformly distribute t over (0, π)
            float t = tMin + (tMax - tMin) * (float)i / (numPoints - 1);

            // Compute lemniscate point in local coordinates
            float sinT = Mathf.Sin(t);
            float cosT = Mathf.Cos(t);
            float denom = 1f + cosT * cosT;

            float x_local = size * sinT / denom;
            float y_local = size * sinT * cosT / denom;

            // Transform to world coordinates
            // Rotate by the forward direction angle
            float angle = Mathf.Atan2(direction.y, direction.x);
            float cosAngle = Mathf.Cos(angle);
            float sinAngle = Mathf.Sin(angle);

            float x_world = origin.x + (x_local * cosAngle - y_local * sinAngle);
            float y_world = origin.y + (x_local * sinAngle + y_local * cosAngle);

            endpoints.Add(new Vector2(x_world, y_world));
        }

        return endpoints;
    }

    /// <summary>
    /// Filters trajectories based on scene awareness (obstacle collision detection).
    /// </summary>
    public List<Trajectory> FilterFeasibleTrajectories(
        List<Trajectory> trajectories,
        SingleSpace space)
    {
        List<Trajectory> feasible = new List<Trajectory>();

        foreach (var trajectory in trajectories)
        {
            // Check collision with virtual obstacles
            bool hasCollision = trajectory.CheckCollisionWithObstacles(space.obstaclePolygons);

            // Check if trajectory stays within tracking space
            bool withinBounds = trajectory.IsWithinTrackingSpace(space.trackingSpace);

            if (!hasCollision && withinBounds)
            {
                feasible.Add(trajectory);
            }
        }

        return feasible;
    }

    /// <summary>
    /// Gets the current prediction horizon.
    /// </summary>
    public float GetPredictionHorizon()
    {
        return predictionHorizon;
    }

    /// <summary>
    /// Sets the prediction horizon.
    /// </summary>
    public void SetPredictionHorizon(float horizon)
    {
        predictionHorizon = horizon;
    }

    /// <summary>
    /// Generates lemniscate points for visualization purposes.
    /// Called by the redirector to draw the lemniscate curve.
    /// </summary>
    public List<Vector2> GenerateLemniscatePointsForVisualization(Vector2 origin, Vector2 direction, int numPoints)
    {
        return GenerateLemniscatePoints(origin, direction, predictionHorizon, numPoints);
    }
}
