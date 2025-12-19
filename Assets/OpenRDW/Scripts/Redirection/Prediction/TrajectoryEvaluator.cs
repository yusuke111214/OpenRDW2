using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Evaluates trajectories using cost functions for PredRedLPP algorithm.
/// Implements the cost functions from the paper:
/// - APF cost (Eq. 16)
/// - Heading cost (Eq. 17)
/// - Reset cost (Eq. 18)
/// - Total cost with discount factor (Eq. 14-15)
/// Based on: "Predictive multiuser redirected walking using artificial potential fields" (Hirt et al., 2024)
/// </summary>
public class TrajectoryEvaluator
{
    private GlobalConfiguration globalConfiguration;

    // Cost parameters
    private float discountFactor; // α in the paper (typically 0.8)
    private float headingCostWeight; // h0 in the paper (typically 1.0)
    private const float RESET_PENALTY = 1000f; // Large penalty for going out of bounds

    /// <summary>
    /// Constructor with configuration.
    /// </summary>
    public TrajectoryEvaluator(GlobalConfiguration config)
    {
        this.globalConfiguration = config;
        this.discountFactor = config.discountFactor;
        this.headingCostWeight = config.headingCostWeight;
    }

    /// <summary>
    /// Evaluates all action-trajectory combinations and returns the best one.
    /// </summary>
    /// <param name="predictions">List of predicted trajectories</param>
    /// <param name="actions">List of redirection actions</param>
    /// <param name="physicalSpace">Current physical space</param>
    /// <param name="redirectedAvatars">List of other users (for APF calculation)</param>
    /// <param name="currentUserIndex">Index of current user's physical space</param>
    /// <returns>Tuple of best action and corresponding trajectory</returns>
    public (RedirectionAction bestAction, Trajectory bestTrajectory) EvaluateAllActions(
        List<Trajectory> predictions,
        List<RedirectionAction> actions,
        SingleSpace physicalSpace,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        float minCost = float.MaxValue;
        RedirectionAction bestAction = null;
        Trajectory bestTrajectory = null;

        // Evaluate each trajectory
        foreach (var trajectory in predictions)
        {
            // For simplicity, we assume the redirection action doesn't significantly
            // alter the predicted trajectory shape (as per paper's implementation notes)
            // Calculate cost for this trajectory
            float trajectoryCost = CalculateTotalCost(
                trajectory,
                physicalSpace,
                redirectedAvatars,
                currentUserIndex,
                currentAvatarId
            );

            trajectory.totalCost = trajectoryCost;

            // For each action, the cost is similar (simplified model)
            // In a full implementation, you'd simulate action effects on the trajectory
            foreach (var action in actions)
            {
                // In the simplified version, we use the trajectory cost directly
                // A more complex version would modify the trajectory based on the action
                float actionCost = trajectoryCost;

                // Optional: Add gain cost (currently not used in paper)
                // actionCost += CalculateGainCost(action);

                if (actionCost < minCost)
                {
                    minCost = actionCost;
                    bestAction = action;
                    bestTrajectory = trajectory;
                }
            }
        }

        // Fallback to null action if no valid trajectory found
        if (bestAction == null)
        {
            bestAction = RedirectionAction.CreateNullAction();
        }

        return (bestAction, bestTrajectory);
    }

    /// <summary>
    /// Calculates total cost for a trajectory using discount factor.
    /// Implements Eq. 14: J_total = Σ(α^i × J_i)
    /// </summary>
    public float CalculateTotalCost(
        Trajectory trajectory,
        SingleSpace space,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        float totalCost = 0f;

        for (int i = 0; i < trajectory.points.Count; i++)
        {
            Vector2 point = trajectory.points[i];

            // Calculate individual cost components (Eq. 15)
            float J_APF = CalculateAPFCost(point, space, redirectedAvatars, currentUserIndex, currentAvatarId);
            float J_Heading = CalculateHeadingCost(i, trajectory, space, redirectedAvatars, currentUserIndex, currentAvatarId);
            float J_Reset = CalculateResetCost(point, space);
            float J_Gain = 0f; // Not used in current implementation

            // Individual cost (Eq. 15)
            float J_i = J_APF + J_Heading + J_Reset + J_Gain;

            // Apply discount factor (Eq. 14)
            totalCost += Mathf.Pow(discountFactor, i) * J_i;
        }

        return totalCost;
    }

    /// <summary>
    /// Calculates APF cost at a point.
    /// Implements Eq. 16: J_APF = ||F_red||
    /// Uses ThomasAPF-style repulsive force calculation (1/d method).
    /// </summary>
    private float CalculateAPFCost(
        Vector2 point,
        SingleSpace space,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        List<Vector2> nearestPosList = new List<Vector2>();

        // Physical borders' contributions
        for (int i = 0; i < space.trackingSpace.Count; i++)
        {
            var p = space.trackingSpace[i];
            var q = space.trackingSpace[(i + 1) % space.trackingSpace.Count];
            var nearestPos = Utilities.GetNearestPos(point, new List<Vector2> { p, q });
            var n = Utilities.RotateVector(q - p, -90).normalized;
            var d = point - nearestPos;

            if (Vector2.Dot(n, d.normalized) > 0)
            {
                nearestPosList.Add(nearestPos);
            }
        }

        // Obstacle contributions
        foreach (var obstacle in space.obstaclePolygons)
        {
            var nearestPos = Utilities.GetNearestPos(point, obstacle);
            nearestPosList.Add(nearestPos);
        }

        // Other users as point obstacles
        foreach (var user in redirectedAvatars)
        {
            var userMovementManager = user.GetComponent<MovementManager>();
            if (userMovementManager.physicalSpaceIndex != currentUserIndex)
            {
                continue;
            }

            var uId = userMovementManager.avatarId;
            if (uId == currentAvatarId)
                continue;

            var userRedirectionManager = user.GetComponent<RedirectionManager>();
            var nearestPos = Utilities.FlattenedPos2D(userRedirectionManager.currPosReal);
            nearestPosList.Add(nearestPos);
        }

        // Calculate repulsive force magnitude using ThomasAPF method
        float repulsiveForce = 0f;
        foreach (var obPos in nearestPosList)
        {
            float distance = (point - obPos).magnitude;
            if (distance > 0.01f) // Avoid division by zero
            {
                repulsiveForce += 1f / distance;
            }
        }

        return repulsiveForce;
    }

    /// <summary>
    /// Calculates heading cost at a point.
    /// Implements Eq. 17: J_Heading = h0 × 0.5 × (1 - (F_red · θ) / (||F_red|| ||θ||))
    /// This encourages the trajectory to align with the repulsive force direction.
    /// </summary>
    private float CalculateHeadingCost(
        int pointIndex,
        Trajectory trajectory,
        SingleSpace space,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        Vector2 point = trajectory.points[pointIndex];

        // Calculate repulsive force direction
        Vector2 F_red = CalculateRepulsiveForceVector(point, space, redirectedAvatars, currentUserIndex, currentAvatarId);

        // Get trajectory heading at this point
        Vector2 heading;
        if (pointIndex < trajectory.points.Count - 1)
        {
            heading = (trajectory.points[pointIndex + 1] - trajectory.points[pointIndex]).normalized;
        }
        else if (pointIndex > 0)
        {
            heading = (trajectory.points[pointIndex] - trajectory.points[pointIndex - 1]).normalized;
        }
        else
        {
            return 0f; // Single point trajectory
        }

        // Calculate dot product
        float F_red_mag = F_red.magnitude;
        float heading_mag = heading.magnitude;

        if (F_red_mag < 0.001f || heading_mag < 0.001f)
        {
            return 0f; // Avoid division by zero
        }

        float dotProduct = Vector2.Dot(F_red, heading);
        float normalizedDot = dotProduct / (F_red_mag * heading_mag);

        // Eq. 17
        float J_Heading = headingCostWeight * 0.5f * (1f - normalizedDot);

        return Mathf.Max(0f, J_Heading); // Ensure non-negative
    }

    /// <summary>
    /// Calculates repulsive force vector at a point (for heading cost).
    /// Similar to CalculateAPFCost but returns the vector instead of magnitude.
    /// </summary>
    private Vector2 CalculateRepulsiveForceVector(
        Vector2 point,
        SingleSpace space,
        List<GameObject> redirectedAvatars,
        int currentUserIndex,
        int currentAvatarId)
    {
        List<Vector2> nearestPosList = new List<Vector2>();

        // Physical borders' contributions
        for (int i = 0; i < space.trackingSpace.Count; i++)
        {
            var p = space.trackingSpace[i];
            var q = space.trackingSpace[(i + 1) % space.trackingSpace.Count];
            var nearestPos = Utilities.GetNearestPos(point, new List<Vector2> { p, q });
            var n = Utilities.RotateVector(q - p, -90).normalized;
            var d = point - nearestPos;

            if (Vector2.Dot(n, d.normalized) > 0)
            {
                nearestPosList.Add(nearestPos);
            }
        }

        // Obstacle contributions
        foreach (var obstacle in space.obstaclePolygons)
        {
            var nearestPos = Utilities.GetNearestPos(point, obstacle);
            nearestPosList.Add(nearestPos);
        }

        // Other users as point obstacles
        foreach (var user in redirectedAvatars)
        {
            var userMovementManager = user.GetComponent<MovementManager>();
            if (userMovementManager.physicalSpaceIndex != currentUserIndex)
            {
                continue;
            }

            var uId = userMovementManager.avatarId;
            if (uId == currentAvatarId)
                continue;

            var userRedirectionManager = user.GetComponent<RedirectionManager>();
            var nearestPos = Utilities.FlattenedPos2D(userRedirectionManager.currPosReal);
            nearestPosList.Add(nearestPos);
        }

        // Calculate negative gradient (repulsive force direction)
        Vector2 ng = Vector2.zero;
        foreach (var obPos in nearestPosList)
        {
            Vector2 diff = point - obPos;
            float distance = diff.magnitude;

            if (distance > 0.01f) // Avoid division by zero
            {
                // Gradient contribution: g = -1/d^2 * (p - o) / ||p - o||
                Vector2 gDelta = -1f / distance * diff.normalized;
                ng += -gDelta; // Negative gradient
            }
        }

        return ng;
    }

    /// <summary>
    /// Calculates reset cost at a point.
    /// Implements Eq. 18: J_Reset = 1000 if outside tracking space, 0 otherwise.
    /// </summary>
    private float CalculateResetCost(Vector2 point, SingleSpace space)
    {
        bool insideTrackingSpace = Utilities.IfPosInPolygon(space.trackingSpace, point);

        return insideTrackingSpace ? 0f : RESET_PENALTY;
    }

    /// <summary>
    /// Optional: Calculates gain cost for a redirection action.
    /// Currently not used in the paper's implementation.
    /// </summary>
    private float CalculateGainCost(RedirectionAction action)
    {
        // Could penalize extreme gains to encourage subtle redirection
        // For now, returns 0 (not used)
        return 0f;
    }

    /// <summary>
    /// Updates cost parameters from configuration.
    /// </summary>
    public void UpdateParameters(GlobalConfiguration config)
    {
        this.discountFactor = config.discountFactor;
        this.headingCostWeight = config.headingCostWeight;
    }
}
