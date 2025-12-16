using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Provides path smoothing functionality using double exponential smoothing with damping.
/// Used to reduce noise in trajectory prediction (mainly for HMD tracking).
/// For simulated environments, this can be disabled as SimulatedWalker data is already clean.
/// </summary>
public class PathSmoother
{
    /// <summary>
    /// Smoothing parameters based on Holt-Winters double exponential smoothing.
    /// </summary>
    private float alpha = 0.3f;  // Level smoothing parameter (0 < alpha < 1)
    private float beta = 0.1f;   // Trend smoothing parameter (0 < beta < 1)
    private float phi = 0.8f;    // Damping factor (0 < phi < 1)

    /// <summary>
    /// If true, smoothing is bypassed (passthrough mode).
    /// Useful for simulation environments where data is already clean.
    /// </summary>
    public bool enableSmoothing = false;

    /// <summary>
    /// Internal state for double exponential smoothing.
    /// </summary>
    private Vector3 levelPosition;
    private Vector3 trendPosition;
    private Vector3 levelDirection;
    private Vector3 trendDirection;

    private bool initialized = false;

    /// <summary>
    /// Constructor with default parameters (smoothing disabled for simulation).
    /// </summary>
    public PathSmoother()
    {
        this.enableSmoothing = false;
    }

    /// <summary>
    /// Constructor with custom parameters.
    /// </summary>
    /// <param name="alpha">Level smoothing (0 to 1, higher = less smoothing)</param>
    /// <param name="beta">Trend smoothing (0 to 1)</param>
    /// <param name="phi">Damping factor (0 to 1)</param>
    /// <param name="enabled">Enable or disable smoothing</param>
    public PathSmoother(float alpha, float beta, float phi, bool enabled = true)
    {
        this.alpha = Mathf.Clamp01(alpha);
        this.beta = Mathf.Clamp01(beta);
        this.phi = Mathf.Clamp01(phi);
        this.enableSmoothing = enabled;
    }

    /// <summary>
    /// Resets the smoother state.
    /// </summary>
    public void Reset()
    {
        initialized = false;
        levelPosition = Vector3.zero;
        trendPosition = Vector3.zero;
        levelDirection = Vector3.zero;
        trendDirection = Vector3.zero;
    }

    /// <summary>
    /// Smooths a queue of positions.
    /// </summary>
    /// <param name="rawPositions">Raw position history (oldest to newest)</param>
    /// <returns>Smoothed positions</returns>
    public List<Vector3> SmoothPositions(Queue<Vector3> rawPositions)
    {
        if (!enableSmoothing || rawPositions == null || rawPositions.Count == 0)
        {
            // Passthrough mode
            return rawPositions.ToList();
        }

        var smoothed = new List<Vector3>();
        Reset(); // Reset state for new sequence

        foreach (var pos in rawPositions)
        {
            smoothed.Add(SmoothPosition(pos));
        }

        return smoothed;
    }

    /// <summary>
    /// Smooths a queue of directions.
    /// </summary>
    /// <param name="rawDirections">Raw direction history (oldest to newest)</param>
    /// <returns>Smoothed directions</returns>
    public List<Vector3> SmoothDirections(Queue<Vector3> rawDirections)
    {
        if (!enableSmoothing || rawDirections == null || rawDirections.Count == 0)
        {
            // Passthrough mode
            return rawDirections.ToList();
        }

        var smoothed = new List<Vector3>();
        Reset(); // Reset state for new sequence

        foreach (var dir in rawDirections)
        {
            smoothed.Add(SmoothDirection(dir));
        }

        return smoothed;
    }

    /// <summary>
    /// Applies double exponential smoothing to a single position observation.
    /// </summary>
    private Vector3 SmoothPosition(Vector3 observation)
    {
        if (!initialized)
        {
            // Initialize with first observation
            levelPosition = observation;
            trendPosition = Vector3.zero;
            initialized = true;
            return observation;
        }

        // Update level
        Vector3 prevLevel = levelPosition;
        levelPosition = alpha * observation + (1f - alpha) * (levelPosition + trendPosition);

        // Update trend
        trendPosition = beta * (levelPosition - prevLevel) + (1f - beta) * trendPosition;

        // Forecast (with damping)
        Vector3 smoothed = levelPosition + phi * trendPosition;

        return smoothed;
    }

    /// <summary>
    /// Applies double exponential smoothing to a single direction observation.
    /// Directions are normalized after smoothing.
    /// </summary>
    private Vector3 SmoothDirection(Vector3 observation)
    {
        if (!initialized)
        {
            // Initialize with first observation
            levelDirection = observation.normalized;
            trendDirection = Vector3.zero;
            initialized = true;
            return observation.normalized;
        }

        // Ensure observation is normalized
        Vector3 normObservation = observation.normalized;

        // Update level
        Vector3 prevLevel = levelDirection;
        levelDirection = alpha * normObservation + (1f - alpha) * (levelDirection + trendDirection);

        // Update trend
        trendDirection = beta * (levelDirection - prevLevel) + (1f - beta) * trendDirection;

        // Forecast (with damping)
        Vector3 smoothed = levelDirection + phi * trendDirection;

        // Normalize the result since it's a direction
        return smoothed.normalized;
    }

    /// <summary>
    /// Smooths a single position value (stateful - maintains internal state).
    /// </summary>
    public Vector3 SmoothPositionStateful(Vector3 position)
    {
        if (!enableSmoothing)
        {
            return position;
        }
        return SmoothPosition(position);
    }

    /// <summary>
    /// Smooths a single direction value (stateful - maintains internal state).
    /// </summary>
    public Vector3 SmoothDirectionStateful(Vector3 direction)
    {
        if (!enableSmoothing)
        {
            return direction.normalized;
        }
        return SmoothDirection(direction);
    }

    /// <summary>
    /// Simple moving average smoothing (alternative method, stateless).
    /// </summary>
    public static List<Vector3> SimpleMovingAverage(Queue<Vector3> data, int windowSize)
    {
        if (data == null || data.Count == 0)
        {
            return new List<Vector3>();
        }

        windowSize = Mathf.Clamp(windowSize, 1, data.Count);
        var smoothed = new List<Vector3>();
        var dataList = data.ToList();

        for (int i = 0; i < dataList.Count; i++)
        {
            int start = Mathf.Max(0, i - windowSize + 1);
            int count = i - start + 1;

            Vector3 sum = Vector3.zero;
            for (int j = start; j <= i; j++)
            {
                sum += dataList[j];
            }

            smoothed.Add(sum / count);
        }

        return smoothed;
    }

    /// <summary>
    /// Sets smoothing parameters at runtime.
    /// </summary>
    public void SetParameters(float alpha, float beta, float phi)
    {
        this.alpha = Mathf.Clamp01(alpha);
        this.beta = Mathf.Clamp01(beta);
        this.phi = Mathf.Clamp01(phi);
        Reset();
    }

    /// <summary>
    /// Enables or disables smoothing.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        this.enableSmoothing = enabled;
        if (enabled)
        {
            Reset();
        }
    }
}
