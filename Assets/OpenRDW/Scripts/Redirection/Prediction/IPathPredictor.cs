using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Interface for path prediction algorithms used in predictive RDW.
/// Implementing classes: LemniscatePathPredictor (LPP), ForwardPathPredictor (FPP).
/// </summary>
public interface IPathPredictor
{
    /// <summary>
    /// Generates predicted trajectories based on movement history.
    /// </summary>
    /// <param name="positionHistory">Historical positions (most recent first)</param>
    /// <param name="directionHistory">Historical directions (most recent first)</param>
    /// <param name="currentPosition">Current user position in physical space</param>
    /// <param name="currentDirection">Current user heading in physical space</param>
    /// <returns>List of predicted trajectories</returns>
    List<Trajectory> GenerateTrajectories(
        Queue<Vector3> positionHistory,
        Queue<Vector3> directionHistory,
        Vector3 currentPosition,
        Vector3 currentDirection
    );

    /// <summary>
    /// Filters out trajectories that collide with virtual obstacles (scene awareness).
    /// </summary>
    /// <param name="trajectories">List of candidate trajectories</param>
    /// <param name="space">Physical space containing obstacles</param>
    /// <returns>List of feasible (collision-free) trajectories</returns>
    List<Trajectory> FilterFeasibleTrajectories(
        List<Trajectory> trajectories,
        SingleSpace space
    );

    /// <summary>
    /// Gets the prediction horizon (distance ahead to predict).
    /// </summary>
    float GetPredictionHorizon();

    /// <summary>
    /// Sets the prediction horizon.
    /// </summary>
    void SetPredictionHorizon(float horizon);
}
