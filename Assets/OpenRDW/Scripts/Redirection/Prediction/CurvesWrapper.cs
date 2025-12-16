using UnityEngine;
using Curves;

/// <summary>
/// Wrapper class for the Curves library, providing Unity-friendly interfaces.
/// Handles coordinate system conversions between Curves (double, radians) and Unity (float, degrees).
/// </summary>
public static class CurvesWrapper
{
    /// <summary>
    /// Creates a clothoid curve from a starting pose and ending point.
    /// This is the primary method used in PredRedLPP for trajectory generation.
    /// </summary>
    /// <param name="startPos">Starting position in Unity coordinates</param>
    /// <param name="startDir">Starting direction in Unity coordinates (Vector2)</param>
    /// <param name="endPos">Ending position in Unity coordinates</param>
    /// <returns>Clothoid object, or null if generation failed</returns>
    public static Clothoid CreateClothoidFromPoseAndPoint(
        Vector2 startPos,
        Vector2 startDir,
        Vector2 endPos)
    {
        // Convert Unity Vector2 direction to angle in radians
        float angleRad = Mathf.Atan2(startDir.y, startDir.x);

        return CreateClothoidFromPoseAndPoint(
            startPos.x,
            startPos.y,
            angleRad,
            endPos.x,
            endPos.y
        );
    }

    /// <summary>
    /// Creates a clothoid curve from a starting pose (position + angle) and ending point.
    /// </summary>
    /// <param name="startX">Starting X coordinate</param>
    /// <param name="startY">Starting Y coordinate</param>
    /// <param name="startAngleRadians">Starting direction in radians</param>
    /// <param name="endX">Ending X coordinate</param>
    /// <param name="endY">Ending Y coordinate</param>
    /// <returns>Clothoid object, or null if generation failed</returns>
    public static Clothoid CreateClothoidFromPoseAndPoint(
        float startX,
        float startY,
        float startAngleRadians,
        float endX,
        float endY)
    {
        try
        {
            // Call Curves library's static method
            Clothoid clothoid = Clothoid.FromPoseAndPoint(
                (double)startX,
                (double)startY,
                (double)startAngleRadians,
                (double)endX,
                (double)endY
            );

            return clothoid;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to create clothoid: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a clothoid with explicit parameters.
    /// </summary>
    /// <param name="startX">Starting X position</param>
    /// <param name="startY">Starting Y position</param>
    /// <param name="startDirection">Starting direction in radians</param>
    /// <param name="startCurvature">Starting curvature</param>
    /// <param name="clothoidParameter">Clothoid parameter A</param>
    /// <param name="length">Length of the clothoid</param>
    /// <returns>Clothoid object</returns>
    public static Clothoid CreateClothoid(
        float startX,
        float startY,
        float startDirection,
        float startCurvature,
        float clothoidParameter,
        float length)
    {
        return new Clothoid(
            (double)startX,
            (double)startY,
            (double)startDirection,
            (double)startCurvature,
            (double)clothoidParameter,
            (double)length
        );
    }

    /// <summary>
    /// Converts a Curves Point2D to Unity Vector2.
    /// </summary>
    public static Vector2 ToVector2(Point2D point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    /// <summary>
    /// Converts a Unity Vector2 to Curves Point2D.
    /// </summary>
    public static Point2D ToPoint2D(Vector2 vector)
    {
        return new Point2D((double)vector.x, (double)vector.y);
    }

    /// <summary>
    /// Converts a Curves Pose2D to Unity position and direction.
    /// </summary>
    public static void ToUnityPose(Pose2D pose, out Vector2 position, out float directionDegrees)
    {
        position = new Vector2((float)pose.X, (float)pose.Y);
        directionDegrees = (float)pose.Direction * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Samples points along a clothoid curve.
    /// </summary>
    /// <param name="clothoid">The clothoid to sample</param>
    /// <param name="numPoints">Number of points to sample</param>
    /// <returns>Array of Unity Vector2 points</returns>
    public static Vector2[] SamplePoints(Clothoid clothoid, int numPoints)
    {
        if (clothoid == null)
        {
            return new Vector2[0];
        }

        Vector2[] points = new Vector2[numPoints];
        for (int i = 0; i < numPoints; i++)
        {
            double t = (double)i / (numPoints - 1);
            Point2D p = clothoid.InterpolatePoint2D(t);
            points[i] = ToVector2(p);
        }
        return points;
    }

    /// <summary>
    /// Gets the curvature at a specific point along the clothoid.
    /// </summary>
    /// <param name="clothoid">The clothoid curve</param>
    /// <param name="t">Parameter (0 to 1) along the curve</param>
    /// <returns>Curvature value</returns>
    public static float GetCurvatureAt(Clothoid clothoid, float t)
    {
        if (clothoid == null) return 0f;
        return (float)clothoid.InterpolateCurvature((double)t);
    }

    /// <summary>
    /// Gets the direction (heading) at a specific point along the clothoid.
    /// </summary>
    /// <param name="clothoid">The clothoid curve</param>
    /// <param name="t">Parameter (0 to 1) along the curve</param>
    /// <returns>Direction in degrees</returns>
    public static float GetDirectionDegreesAt(Clothoid clothoid, float t)
    {
        if (clothoid == null) return 0f;
        double dirRadians = clothoid.InterpolateDirection((double)t);
        return (float)dirRadians * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Gets the direction (heading) at a specific point along the clothoid in radians.
    /// </summary>
    public static float GetDirectionRadiansAt(Clothoid clothoid, float t)
    {
        if (clothoid == null) return 0f;
        return (float)clothoid.InterpolateDirection((double)t);
    }

    /// <summary>
    /// Validates if a clothoid was successfully created.
    /// </summary>
    public static bool IsValidClothoid(Clothoid clothoid)
    {
        return clothoid != null && !double.IsNaN(clothoid.A) && !double.IsInfinity(clothoid.A);
    }

    /// <summary>
    /// Gets the approximate length of a clothoid by sampling.
    /// </summary>
    public static float GetApproximateLength(Clothoid clothoid, int numSamples = 20)
    {
        if (clothoid == null) return 0f;

        float length = 0f;
        Vector2 prevPoint = ToVector2(clothoid.InterpolatePoint2D(0.0));

        for (int i = 1; i <= numSamples; i++)
        {
            double t = (double)i / numSamples;
            Vector2 currentPoint = ToVector2(clothoid.InterpolatePoint2D(t));
            length += Vector2.Distance(prevPoint, currentPoint);
            prevPoint = currentPoint;
        }

        return length;
    }

    /// <summary>
    /// Checks if the generated clothoid is approximately straight.
    /// Useful for filtering out degenerate cases.
    /// </summary>
    /// <param name="clothoid">The clothoid to check</param>
    /// <param name="curvatureThreshold">Maximum curvature to consider "straight"</param>
    /// <returns>True if clothoid is approximately straight</returns>
    public static bool IsApproximatelyStraight(Clothoid clothoid, float curvatureThreshold = 0.01f)
    {
        if (clothoid == null) return false;

        // Sample curvature at start, middle, and end
        float curvStart = GetCurvatureAt(clothoid, 0f);
        float curvMid = GetCurvatureAt(clothoid, 0.5f);
        float curvEnd = GetCurvatureAt(clothoid, 1f);

        return Mathf.Abs(curvStart) < curvatureThreshold &&
               Mathf.Abs(curvMid) < curvatureThreshold &&
               Mathf.Abs(curvEnd) < curvatureThreshold;
    }
}
