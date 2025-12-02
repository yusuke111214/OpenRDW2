using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Combines repulsive and attractive potential fields to solve local minima problem.
/// The repulsive force pushes away from obstacles, while attractive force pulls towards
/// open spaces, creating a more robust redirection strategy.
/// </summary>
public class CombinedAPF
{
    // Components
    private AttractivePotentialField attractiveField;
    private SteeringTargetSelector targetSelector;

    // Weight parameters
    private float repulsiveWeight;
    private float attractiveWeight;

    // Adaptive weighting parameters
    private bool useAdaptiveWeights;
    private float closeObstacleDistance = 1.0f;   // Distance threshold for "close" obstacle
    private float farObstacleDistance = 2.0f;     // Distance threshold for "far" obstacle

    // Adaptive weight presets
    private const float REPULSIVE_WEIGHT_CLOSE = 0.9f;
    private const float ATTRACTIVE_WEIGHT_CLOSE = 0.1f;

    private const float REPULSIVE_WEIGHT_NORMAL = 0.7f;
    private const float ATTRACTIVE_WEIGHT_NORMAL = 0.3f;

    private const float REPULSIVE_WEIGHT_FAR = 0.5f;
    private const float ATTRACTIVE_WEIGHT_FAR = 0.5f;

    public CombinedAPF(
        AttractivePotentialField attractiveField,
        SteeringTargetSelector targetSelector,
        float repulsiveWeight = 0.7f,
        float attractiveWeight = 0.3f,
        bool useAdaptiveWeights = false)
    {
        this.attractiveField = attractiveField;
        this.targetSelector = targetSelector;
        this.repulsiveWeight = repulsiveWeight;
        this.attractiveWeight = attractiveWeight;
        this.useAdaptiveWeights = useAdaptiveWeights;

        // Normalize weights to sum to 1.0
        NormalizeWeights();
    }

    /// <summary>
    /// Calculates total force by combining repulsive and attractive forces.
    /// Returns the combined force vector and the negative gradient (for compatibility).
    /// </summary>
    public Vector2 CalculateTotalForce(
        Vector3 userPosition,
        Vector3 userVelocity,
        Vector2 repulsiveForce,
        float minDistanceToObstacle)
    {
        // Get steering target from selector
        Vector3 target = targetSelector.GetSteeringTarget(userPosition);

        // Calculate attractive force
        Vector2 attractiveForce = attractiveField.CalculateAttractiveForceWithVelocity(
            userPosition,
            target,
            userVelocity
        );

        // Determine weights based on distance to obstacles
        float repWeight, attWeight;
        if (useAdaptiveWeights)
        {
            DetermineAdaptiveWeights(minDistanceToObstacle, out repWeight, out attWeight);
        }
        else
        {
            repWeight = repulsiveWeight;
            attWeight = attractiveWeight;
        }

        // Combine forces
        Vector2 totalForce = repWeight * repulsiveForce + attWeight * attractiveForce;

        return totalForce;
    }

    /// <summary>
    /// Calculates combined force with visualization data.
    /// Useful for debugging and understanding the algorithm's behavior.
    /// </summary>
    public Vector2 CalculateTotalForceWithDebugInfo(
        Vector3 userPosition,
        Vector3 userVelocity,
        Vector2 repulsiveForce,
        float minDistanceToObstacle,
        out Vector2 attractiveForceOut,
        out float repWeightOut,
        out float attWeightOut,
        out Vector3 targetOut)
    {
        // Get steering target
        targetOut = targetSelector.GetSteeringTarget(userPosition);

        // Calculate attractive force
        attractiveForceOut = attractiveField.CalculateAttractiveForceWithVelocity(
            userPosition,
            targetOut,
            userVelocity
        );

        // Determine weights
        if (useAdaptiveWeights)
        {
            DetermineAdaptiveWeights(minDistanceToObstacle, out repWeightOut, out attWeightOut);
        }
        else
        {
            repWeightOut = repulsiveWeight;
            attWeightOut = attractiveWeight;
        }

        // Combine forces
        Vector2 totalForce = repWeightOut * repulsiveForce + attWeightOut * attractiveForceOut;

        return totalForce;
    }

    /// <summary>
    /// Determines adaptive weights based on proximity to obstacles.
    /// Closer to obstacles: prioritize repulsion
    /// In open space: balance repulsion and attraction
    /// </summary>
    private void DetermineAdaptiveWeights(
        float minDistanceToObstacle,
        out float repWeight,
        out float attWeight)
    {
        if (minDistanceToObstacle < closeObstacleDistance)
        {
            // Very close to obstacle - prioritize repulsion
            repWeight = REPULSIVE_WEIGHT_CLOSE;
            attWeight = ATTRACTIVE_WEIGHT_CLOSE;
        }
        else if (minDistanceToObstacle > farObstacleDistance)
        {
            // Far from obstacles - can prioritize attraction
            repWeight = REPULSIVE_WEIGHT_FAR;
            attWeight = ATTRACTIVE_WEIGHT_FAR;
        }
        else
        {
            // Normal range - balanced weights
            repWeight = REPULSIVE_WEIGHT_NORMAL;
            attWeight = ATTRACTIVE_WEIGHT_NORMAL;
        }
    }

    /// <summary>
    /// Normalizes weights to ensure they sum to 1.0
    /// </summary>
    private void NormalizeWeights()
    {
        float sum = repulsiveWeight + attractiveWeight;
        if (sum > 0)
        {
            repulsiveWeight /= sum;
            attractiveWeight /= sum;
        }
        else
        {
            // Default to equal weights
            repulsiveWeight = 0.5f;
            attractiveWeight = 0.5f;
        }
    }

    /// <summary>
    /// Sets repulsive and attractive weights.
    /// Automatically normalizes to sum to 1.0.
    /// </summary>
    public void SetWeights(float repulsive, float attractive)
    {
        this.repulsiveWeight = Mathf.Max(0, repulsive);
        this.attractiveWeight = Mathf.Max(0, attractive);
        NormalizeWeights();
    }

    /// <summary>
    /// Enables or disables adaptive weight adjustment.
    /// </summary>
    public void SetAdaptiveWeights(bool enabled)
    {
        this.useAdaptiveWeights = enabled;
    }

    /// <summary>
    /// Sets distance thresholds for adaptive weight calculation.
    /// </summary>
    public void SetAdaptiveDistanceThresholds(float closeDistance, float farDistance)
    {
        this.closeObstacleDistance = Mathf.Max(0.1f, closeDistance);
        this.farObstacleDistance = Mathf.Max(closeDistance + 0.5f, farDistance);
    }

    /// <summary>
    /// Gets current repulsive weight.
    /// </summary>
    public float GetRepulsiveWeight()
    {
        return repulsiveWeight;
    }

    /// <summary>
    /// Gets current attractive weight.
    /// </summary>
    public float GetAttractiveWeight()
    {
        return attractiveWeight;
    }

    /// <summary>
    /// Checks if the combined force would cause collision with walls.
    /// Returns true if force direction leads towards nearby wall.
    /// </summary>
    public bool WillForceLeadToCollision(
        Vector3 userPosition,
        Vector2 totalForce,
        List<Vector2> trackingSpaceBoundary,
        float safetyMargin = 0.3f)
    {
        if (totalForce.magnitude < 0.01f)
        {
            return false;
        }

        Vector2 pos2D = Utilities.FlattenedPos2D(userPosition);
        Vector2 forceDirection = totalForce.normalized;

        // Check if moving in force direction would hit a wall within safety margin
        for (int i = 0; i < trackingSpaceBoundary.Count; i++)
        {
            Vector2 p1 = trackingSpaceBoundary[i];
            Vector2 p2 = trackingSpaceBoundary[(i + 1) % trackingSpaceBoundary.Count];

            // Check distance to wall segment in force direction
            Vector2 wallDir = (p2 - p1).normalized;
            Vector2 toWall = p1 - pos2D;
            Vector2 wallNormal = new Vector2(-wallDir.y, wallDir.x);

            // Is user moving towards this wall?
            float dotProduct = Vector2.Dot(forceDirection, wallNormal);
            if (dotProduct > 0.5f) // Moving towards wall (angle < 60 degrees)
            {
                float distToWall = Mathf.Abs(Vector2.Dot(toWall, wallNormal));
                if (distToWall < safetyMargin)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
