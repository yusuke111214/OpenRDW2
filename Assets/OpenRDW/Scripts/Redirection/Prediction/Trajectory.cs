using UnityEngine;
using System.Collections.Generic;
using Curves;

/// <summary>
/// Represents a predicted trajectory using a clothoid curve.
/// This class bridges the Curves library with Unity's coordinate system.
/// </summary>
public class Trajectory
{
    /// <summary>
    /// The underlying clothoid curve from the Curves library.
    /// </summary>
    public Clothoid clothoid;

    /// <summary>
    /// List of points along the trajectory in Unity coordinates (Vector2).
    /// Used for collision detection and visualization.
    /// </summary>
    public List<Vector2> points;

    /// <summary>
    /// Starting pose of the trajectory.
    /// </summary>
    public Vector2 startPosition;
    public float startDirection; // in degrees

    /// <summary>
    /// Ending pose of the trajectory.
    /// </summary>
    public Vector2 endPosition;
    public float endDirection; // in degrees

    /// <summary>
    /// Total cost calculated for this trajectory (used in PredRedLPP).
    /// </summary>
    public float totalCost = float.MaxValue;

    /// <summary>
    /// Constructor from a Curves.Clothoid object.
    /// </summary>
    /// <param name="clothoid">The clothoid curve</param>
    /// <param name="numSamples">Number of sample points to generate along the curve</param>
    public Trajectory(Clothoid clothoid, int numSamples = 20)
    {
        this.clothoid = clothoid;
        this.points = new List<Vector2>();

        if (clothoid != null)
        {
            // Sample points along the clothoid
            for (int i = 0; i <= numSamples; i++)
            {
                double t = (double)i / numSamples;
                Point2D p = clothoid.InterpolatePoint2D(t);
                points.Add(new Vector2((float)p.X, (float)p.Y));
            }

            // Store start and end poses
            Pose2D startPose = clothoid.InterpolatePose2D(0.0);
            Pose2D endPose = clothoid.InterpolatePose2D(1.0);

            startPosition = new Vector2((float)startPose.X, (float)startPose.Y);
            startDirection = (float)startPose.Direction * Mathf.Rad2Deg;

            endPosition = new Vector2((float)endPose.X, (float)endPose.Y);
            endDirection = (float)endPose.Direction * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// Constructor from explicit point list (for testing or custom trajectories).
    /// </summary>
    /// <param name="points">List of points defining the trajectory</param>
    public Trajectory(List<Vector2> points)
    {
        this.points = points;
        this.clothoid = null;

        if (points != null && points.Count >= 2)
        {
            startPosition = points[0];
            endPosition = points[points.Count - 1];

            // Calculate approximate directions
            if (points.Count >= 2)
            {
                Vector2 startDir = (points[1] - points[0]).normalized;
                startDirection = Mathf.Atan2(startDir.y, startDir.x) * Mathf.Rad2Deg;

                Vector2 endDir = (points[points.Count - 1] - points[points.Count - 2]).normalized;
                endDirection = Mathf.Atan2(endDir.y, endDir.x) * Mathf.Rad2Deg;
            }
        }
    }

    /// <summary>
    /// Checks if any point on the trajectory collides with virtual obstacles.
    /// </summary>
    /// <param name="obstaclePolygons">List of obstacle polygons</param>
    /// <returns>True if collision detected</returns>
    public bool CheckCollisionWithObstacles(List<List<Vector2>> obstaclePolygons)
    {
        foreach (var point in points)
        {
            foreach (var obstacle in obstaclePolygons)
            {
                if (Utilities.IfPosInPolygon(obstacle, point))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the trajectory stays within the tracking space boundaries.
    /// </summary>
    /// <param name="trackingSpaceBoundary">Boundary polygon of the tracking space</param>
    /// <returns>True if trajectory is fully inside tracking space</returns>
    public bool IsWithinTrackingSpace(List<Vector2> trackingSpaceBoundary)
    {
        foreach (var point in points)
        {
            if (!Utilities.IfPosInPolygon(trackingSpaceBoundary, point))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Gets the length of the trajectory by summing distances between consecutive points.
    /// </summary>
    public float GetLength()
    {
        float length = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            length += Vector2.Distance(points[i - 1], points[i]);
        }
        return length;
    }

    /// <summary>
    /// Gets a point along the trajectory at normalized parameter t (0 to 1).
    /// </summary>
    public Vector2 GetPointAt(float t)
    {
        if (clothoid != null)
        {
            Point2D p = clothoid.InterpolatePoint2D(t);
            return new Vector2((float)p.X, (float)p.Y);
        }
        else if (points != null && points.Count > 0)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(t * (points.Count - 1)), 0, points.Count - 1);
            return points[index];
        }
        return Vector2.zero;
    }

    /// <summary>
    /// Gets the direction (heading) at normalized parameter t (0 to 1).
    /// </summary>
    /// <returns>Direction in degrees</returns>
    public float GetDirectionAt(float t)
    {
        if (clothoid != null)
        {
            Pose2D pose = clothoid.InterpolatePose2D(t);
            return (float)pose.Direction * Mathf.Rad2Deg;
        }
        else if (points != null && points.Count >= 2)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(t * (points.Count - 1)), 0, points.Count - 2);
            Vector2 dir = (points[index + 1] - points[index]).normalized;
            return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }
        return 0f;
    }
}
