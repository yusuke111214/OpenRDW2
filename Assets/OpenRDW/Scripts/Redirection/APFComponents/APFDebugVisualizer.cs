using UnityEngine;

/// <summary>
/// Debug visualization utilities for APF with attraction.
/// Provides visual feedback for understanding force combinations and algorithm behavior.
/// </summary>
public class APFDebugVisualizer : MonoBehaviour
{
    // Visualization settings
    private bool showGizmos = true;
    private Color repulsiveColor = Color.red;
    private Color attractiveColor = Color.cyan;
    private Color combinedColor = Color.green;
    private Color targetColor = Color.yellow;

    // Force scale for visualization
    private float forceScale = 2.0f;

    // Data to visualize
    private Vector3 userPosition;
    private Vector2 repulsiveForce;
    private Vector2 attractiveForce;
    private Vector2 combinedForce;
    private Vector3 steeringTarget;
    private float repulsiveWeight;
    private float attractiveWeight;
    private float minDistanceToObstacle;

    /// <summary>
    /// Updates the visualization data.
    /// </summary>
    public void UpdateVisualization(
        Vector3 userPos,
        Vector2 repForce,
        Vector2 attForce,
        Vector2 combForce,
        Vector3 target,
        float repWeight,
        float attWeight,
        float minDist)
    {
        this.userPosition = userPos;
        this.repulsiveForce = repForce;
        this.attractiveForce = attForce;
        this.combinedForce = combForce;
        this.steeringTarget = target;
        this.repulsiveWeight = repWeight;
        this.attractiveWeight = attWeight;
        this.minDistanceToObstacle = minDist;
    }

    /// <summary>
    /// Draws force vectors and debug information in scene view.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!showGizmos || !Application.isPlaying)
            return;

        // Draw repulsive force (red)
        if (repulsiveForce.magnitude > 0.01f)
        {
            Gizmos.color = repulsiveColor;
            Vector3 repForce3D = Utilities.UnFlatten(repulsiveForce * forceScale);
            Gizmos.DrawRay(userPosition, repForce3D);
            DrawArrowHead(userPosition + repForce3D, repForce3D.normalized, 0.2f);
        }

        // Draw attractive force (cyan)
        if (attractiveForce.magnitude > 0.01f)
        {
            Gizmos.color = attractiveColor;
            Vector3 attForce3D = Utilities.UnFlatten(attractiveForce * forceScale);
            Gizmos.DrawRay(userPosition, attForce3D);
            DrawArrowHead(userPosition + attForce3D, attForce3D.normalized, 0.2f);
        }

        // Draw combined force (green, thicker)
        if (combinedForce.magnitude > 0.01f)
        {
            Gizmos.color = combinedColor;
            Vector3 combForce3D = Utilities.UnFlatten(combinedForce * forceScale);

            // Draw thicker line by drawing multiple offset lines
            for (int i = -2; i <= 2; i++)
            {
                Vector3 offset = Vector3.right * i * 0.02f;
                Gizmos.DrawRay(userPosition + offset, combForce3D);
            }
            DrawArrowHead(userPosition + combForce3D, combForce3D.normalized, 0.3f);
        }

        // Draw line to steering target
        Gizmos.color = targetColor;
        Gizmos.DrawLine(userPosition, steeringTarget);

        // Draw steering target sphere
        Gizmos.color = targetColor;
        Gizmos.DrawWireSphere(steeringTarget, 0.3f);

        // Draw user position indicator
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(userPosition, 0.2f);

        // Draw distance indicator to nearest obstacle
        Gizmos.color = Color.Lerp(Color.red, Color.green, Mathf.Clamp01(minDistanceToObstacle / 2.0f));
        Gizmos.DrawWireSphere(userPosition, Mathf.Min(minDistanceToObstacle, 3.0f));
    }

    /// <summary>
    /// Draws an arrow head at the end of a force vector.
    /// </summary>
    private void DrawArrowHead(Vector3 position, Vector3 direction, float size)
    {
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized * size;
        Vector3 back = -direction * size;

        Gizmos.DrawRay(position, back + right);
        Gizmos.DrawRay(position, back - right);
    }

    /// <summary>
    /// Draws debug text in scene view using Handles (Editor only).
    /// </summary>
    #if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        // Draw text labels for forces
        UnityEditor.Handles.color = Color.white;

        string debugText = $"Repulsive: {repulsiveForce.magnitude:F3} (w={repulsiveWeight:F2})\n" +
                          $"Attractive: {attractiveForce.magnitude:F3} (w={attractiveWeight:F2})\n" +
                          $"Combined: {combinedForce.magnitude:F3}\n" +
                          $"Min Dist to Obstacle: {minDistanceToObstacle:F2}m";

        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 12;

        UnityEditor.Handles.Label(userPosition + Vector3.up * 2.0f, debugText, style);
    }
    #endif

    /// <summary>
    /// Enables or disables gizmo visualization.
    /// </summary>
    public void SetShowGizmos(bool show)
    {
        this.showGizmos = show;
    }

    /// <summary>
    /// Sets the scale factor for force vectors.
    /// </summary>
    public void SetForceScale(float scale)
    {
        this.forceScale = Mathf.Max(0.1f, scale);
    }

    /// <summary>
    /// Sets custom colors for visualization.
    /// </summary>
    public void SetColors(Color repulsive, Color attractive, Color combined, Color target)
    {
        this.repulsiveColor = repulsive;
        this.attractiveColor = attractive;
        this.combinedColor = combined;
        this.targetColor = target;
    }
}
