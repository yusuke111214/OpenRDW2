using UnityEngine;

/// <summary>
/// Selects and updates steering targets for attractive potential field.
/// Based on Dong et al. (2020) - Dynamic Artificial Potential Fields for Multi-User RDW.
/// Periodically updates the target to reduce computational cost.
/// </summary>
public class SteeringTargetSelector
{
    private OpenSpaceDetector spaceDetector;

    // Parameters
    private float updateInterval;  // Seconds between updates

    // State
    private Vector3 currentTarget;
    private float lastUpdateTime;
    private bool isInitialized;

    public SteeringTargetSelector(
        OpenSpaceDetector detector,
        float updateInterval = 1.0f)
    {
        this.spaceDetector = detector;
        this.updateInterval = updateInterval;
        this.lastUpdateTime = -updateInterval;  // Force immediate update on first call
        this.isInitialized = false;
    }

    /// <summary>
    /// Gets the current steering target, updating it if necessary.
    /// </summary>
    public Vector3 GetSteeringTarget(Vector3 userPosition)
    {
        // Check if we need to update the target
        float currentTime = Time.time;
        bool shouldUpdate = (currentTime - lastUpdateTime) >= updateInterval;

        if (!isInitialized || shouldUpdate)
        {
            UpdateTarget(userPosition);
            lastUpdateTime = currentTime;
            isInitialized = true;
        }

        return currentTarget;
    }

    /// <summary>
    /// Forces an immediate update of the steering target.
    /// Useful when user resets or enters a new area.
    /// </summary>
    public void ForceUpdate(Vector3 userPosition)
    {
        UpdateTarget(userPosition);
        lastUpdateTime = Time.time;
    }

    /// <summary>
    /// Gets the current target without updating it.
    /// </summary>
    public Vector3 GetCurrentTarget()
    {
        return currentTarget;
    }

    /// <summary>
    /// Sets the update interval for target refresh.
    /// </summary>
    public void SetUpdateInterval(float interval)
    {
        this.updateInterval = Mathf.Max(0.1f, interval);  // Minimum 0.1s
    }

    /// <summary>
    /// Updates the steering target based on open space detection.
    /// </summary>
    private void UpdateTarget(Vector3 userPosition)
    {
        // Find the best open space target
        currentTarget = spaceDetector.FindBestOpenSpaceTarget(userPosition);

        // Debug visualization
        #if UNITY_EDITOR
        Debug.DrawLine(userPosition, currentTarget, Color.cyan, updateInterval);
        #endif
    }

    /// <summary>
    /// Calculates a score for prioritizing open spaces.
    /// Higher score means better target.
    /// Based on Dong et al.'s criteria: area size and distance to borders.
    /// </summary>
    private float CalculateOpenSpaceScore(
        float openSpaceArea,
        float distanceToBorder,
        float areaWeight = 0.6f,
        float distanceWeight = 0.4f)
    {
        // Normalize values (assuming typical tracking space is ~10m x 10m)
        float normalizedArea = Mathf.Clamp01(openSpaceArea / 100f);
        float normalizedDistance = Mathf.Clamp01(distanceToBorder / 5f);

        return areaWeight * normalizedArea + distanceWeight * normalizedDistance;
    }
}
