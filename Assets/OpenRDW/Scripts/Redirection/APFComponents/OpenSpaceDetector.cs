using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Detects open spaces in the tracking area using raycast-based approach.
/// This is a lightweight implementation for finding safe directions to steer users.
/// </summary>
public class OpenSpaceDetector
{
    private GlobalConfiguration globalConfiguration;
    private RedirectionManager redirectionManager;
    private MovementManager movementManager;

    // Parameters
    private int numRays = 16;  // Number of rays cast in 360 degrees
    private float maxRayDistance = 10f;  // Maximum distance to check
    private LayerMask obstacleLayer;  // Layer mask for obstacles

    public OpenSpaceDetector(
        GlobalConfiguration config,
        RedirectionManager redirection,
        MovementManager movement)
    {
        this.globalConfiguration = config;
        this.redirectionManager = redirection;
        this.movementManager = movement;

        // Setup layer mask to detect obstacles and boundaries
        // We'll raycast against the tracking space boundaries and obstacles
        obstacleLayer = ~0;  // All layers initially
    }

    /// <summary>
    /// Finds the best open direction from the user's current position.
    /// Returns a target position in that direction.
    /// </summary>
    public Vector3 FindBestOpenSpaceTarget(Vector3 userPosition)
    {
        float maxOpenness = 0;
        Vector3 bestDirection = Vector3.forward;
        float bestDistance = 0;

        // Cast rays in all directions
        for (int i = 0; i < numRays; i++)
        {
            float angle = i * 360f / numRays;
            Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            // Measure openness in this direction
            float openness = MeasureOpenness(userPosition, direction, out float distance);

            if (openness > maxOpenness)
            {
                maxOpenness = openness;
                bestDirection = direction;
                bestDistance = distance;
            }
        }

        // Return target point at a safe distance in the best direction
        // Use 50% of the available distance to avoid getting too close to boundaries
        Vector3 targetPosition = userPosition + bestDirection * (bestDistance * 0.5f);

        // Clamp to tracking space bounds
        return ClampToTrackingSpace(targetPosition);
    }

    /// <summary>
    /// Measures how "open" a direction is by calculating distance to nearest obstacle
    /// and considering the area around that direction.
    /// </summary>
    private float MeasureOpenness(Vector3 position, Vector3 direction, out float distance)
    {
        distance = 0;

        // Get distance to nearest boundary/obstacle in this direction
        float distanceToObstacle = GetDistanceToObstacle(position, direction);

        // Also check slightly angled rays to get a sense of "width" of opening
        float angleSpread = 15f;  // degrees
        float distanceLeft = GetDistanceToObstacle(
            position,
            Quaternion.Euler(0, -angleSpread, 0) * direction
        );
        float distanceRight = GetDistanceToObstacle(
            position,
            Quaternion.Euler(0, angleSpread, 0) * direction
        );

        // Openness is combination of forward distance and width
        // Use minimum to ensure we don't recommend directions that are open ahead but narrow
        distance = distanceToObstacle;
        float minDistance = Mathf.Min(distanceToObstacle, Mathf.Min(distanceLeft, distanceRight));

        // Openness score: favor both distance and width
        float openness = minDistance * 0.6f + distanceToObstacle * 0.4f;

        return openness;
    }

    /// <summary>
    /// Gets the distance to the nearest obstacle in a given direction.
    /// Uses both raycasting and geometric calculations.
    /// </summary>
    private float GetDistanceToObstacle(Vector3 position, Vector3 direction)
    {
        Vector2 pos2D = Utilities.FlattenedPos2D(position);
        Vector2 dir2D = new Vector2(direction.x, direction.z).normalized;

        int userIndex = movementManager.physicalSpaceIndex;
        SingleSpace space = globalConfiguration.physicalSpaces[userIndex];

        float minDistance = maxRayDistance;

        // Check distance to tracking space boundaries
        for (int i = 0; i < space.trackingSpace.Count; i++)
        {
            Vector2 p = space.trackingSpace[i];
            Vector2 q = space.trackingSpace[(i + 1) % space.trackingSpace.Count];

            float dist = GetDistanceToLineSegment(pos2D, dir2D, p, q);
            if (dist > 0 && dist < minDistance)
            {
                minDistance = dist;
            }
        }

        // Check distance to obstacles
        foreach (var obstacle in space.obstaclePolygons)
        {
            for (int i = 0; i < obstacle.Count; i++)
            {
                Vector2 p = obstacle[i];
                Vector2 q = obstacle[(i + 1) % obstacle.Count];

                float dist = GetDistanceToLineSegment(pos2D, dir2D, p, q);
                if (dist > 0 && dist < minDistance)
                {
                    minDistance = dist;
                }
            }
        }

        // Check distance to other users
        foreach (var user in globalConfiguration.redirectedAvatars)
        {
            if (user.GetComponent<MovementManager>().physicalSpaceIndex != userIndex)
            {
                continue;
            }

            var uId = user.GetComponent<MovementManager>().avatarId;
            if (uId == movementManager.avatarId)
                continue;

            Vector2 otherUserPos = Utilities.FlattenedPos2D(
                user.GetComponent<RedirectionManager>().currPosReal
            );

            // Treat other users as circular obstacles with 0.5m radius
            float dist = GetDistanceToPoint(pos2D, dir2D, otherUserPos, 0.5f);
            if (dist > 0 && dist < minDistance)
            {
                minDistance = dist;
            }
        }

        return minDistance;
    }

    /// <summary>
    /// Calculates intersection distance from position to a line segment in 2D.
    /// </summary>
    private float GetDistanceToLineSegment(Vector2 pos, Vector2 dir, Vector2 lineStart, Vector2 lineEnd)
    {
        // Ray-line segment intersection
        Vector2 lineDir = lineEnd - lineStart;
        Vector2 lineNormal = new Vector2(-lineDir.y, lineDir.x).normalized;

        // Check if ray is pointing towards the line
        if (Vector2.Dot(dir, lineNormal) >= 0)
        {
            // Ray is pointing away from or parallel to line
            return -1;
        }

        // Solve for intersection
        // pos + t * dir = lineStart + s * lineDir
        float denom = dir.x * lineDir.y - dir.y * lineDir.x;

        if (Mathf.Abs(denom) < 0.0001f)
        {
            // Parallel
            return -1;
        }

        Vector2 diff = lineStart - pos;
        float t = (diff.x * lineDir.y - diff.y * lineDir.x) / denom;
        float s = (diff.x * dir.y - diff.y * dir.x) / denom;

        // Check if intersection is valid
        if (t > 0 && s >= 0 && s <= 1)
        {
            return t;
        }

        return -1;
    }

    /// <summary>
    /// Calculates distance from position to a circular obstacle.
    /// </summary>
    private float GetDistanceToPoint(Vector2 pos, Vector2 dir, Vector2 point, float radius)
    {
        Vector2 toPoint = point - pos;

        // Project point onto ray direction
        float projection = Vector2.Dot(toPoint, dir);

        if (projection < 0)
        {
            // Point is behind the ray
            return -1;
        }

        // Get closest point on ray to the obstacle
        Vector2 closestPoint = pos + dir * projection;
        float distanceToCenter = Vector2.Distance(closestPoint, point);

        if (distanceToCenter < radius)
        {
            // Ray intersects the circle
            // Calculate distance to intersection point
            float distanceAlongRay = projection - Mathf.Sqrt(radius * radius - distanceToCenter * distanceToCenter);
            return Mathf.Max(0, distanceAlongRay);
        }

        return -1;
    }

    /// <summary>
    /// Clamps a target position to be within the tracking space.
    /// </summary>
    private Vector3 ClampToTrackingSpace(Vector3 targetPosition)
    {
        int userIndex = movementManager.physicalSpaceIndex;
        SingleSpace space = globalConfiguration.physicalSpaces[userIndex];

        Vector2 target2D = Utilities.FlattenedPos2D(targetPosition);

        // Simple clamping: find centroid of tracking space and ensure target is inside
        Vector2 centroid = Vector2.zero;
        foreach (var point in space.trackingSpace)
        {
            centroid += point;
        }
        centroid /= space.trackingSpace.Count;

        // If target is outside, move it towards centroid
        // This is a simplified approach; for complex polygons, use proper point-in-polygon test
        if (!IsPointInPolygon(target2D, space.trackingSpace))
        {
            // Move target towards centroid
            target2D = centroid + (target2D - centroid) * 0.5f;
        }

        return new Vector3(target2D.x, targetPosition.y, target2D.y);
    }

    /// <summary>
    /// Simple point-in-polygon test using ray casting algorithm.
    /// </summary>
    private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        int intersections = 0;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 p1 = polygon[i];
            Vector2 p2 = polygon[(i + 1) % polygon.Count];

            if ((p1.y > point.y) != (p2.y > point.y))
            {
                float xIntersection = (p2.x - p1.x) * (point.y - p1.y) / (p2.y - p1.y) + p1.x;
                if (point.x < xIntersection)
                {
                    intersections++;
                }
            }
        }

        return (intersections % 2) == 1;
    }
}
