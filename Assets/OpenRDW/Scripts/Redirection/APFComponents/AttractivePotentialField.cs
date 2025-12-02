using UnityEngine;

/// <summary>
/// Calculates attractive force towards open spaces or steering targets.
/// Implements conic potential field (saturates at distance) to avoid excessive attraction
/// when target is far away.
/// Based on Khatib (1986) and adapted for RDW by Dong et al. (2020).
/// </summary>
public class AttractivePotentialField
{
    // Parameters
    private float k_att;              // Attraction strength coefficient
    private float saturationDistance; // Distance beyond which attraction saturates
    private float velocityWeight;     // Weight for velocity-based modulation

    public AttractivePotentialField(
        float attractionStrength = 1.0f,
        float saturationDistance = 3.0f,
        float velocityWeight = 0.3f)
    {
        this.k_att = attractionStrength;
        this.saturationDistance = saturationDistance;
        this.velocityWeight = velocityWeight;
    }

    /// <summary>
    /// Calculates attractive force from current position towards target.
    /// Uses conic potential to avoid excessive attraction when far away.
    /// </summary>
    public Vector2 CalculateAttractiveForce(Vector3 position, Vector3 target)
    {
        Vector2 pos2D = Utilities.FlattenedPos2D(position);
        Vector2 target2D = Utilities.FlattenedPos2D(target);

        Vector2 toTarget = target2D - pos2D;
        float distance = toTarget.magnitude;

        if (distance < 0.01f)
        {
            // Already at target
            return Vector2.zero;
        }

        // Conic potential field
        // Close: F = k_att * distance * direction (linear increase)
        // Far: F = k_att * d_sat * direction (constant saturation)
        float forceMagnitude;
        if (distance <= saturationDistance)
        {
            // Linear increase when close
            forceMagnitude = k_att * distance;
        }
        else
        {
            // Saturate when far
            forceMagnitude = k_att * saturationDistance;
        }

        return forceMagnitude * toTarget.normalized;
    }

    /// <summary>
    /// Calculates attractive force with velocity consideration.
    /// Reduces attraction if user is already moving towards target.
    /// This creates more natural redirection behavior.
    /// </summary>
    public Vector2 CalculateAttractiveForceWithVelocity(
        Vector3 position,
        Vector3 target,
        Vector3 userVelocity)
    {
        // Get base attractive force
        Vector2 baseForce = CalculateAttractiveForce(position, target);

        if (baseForce.magnitude < 0.01f)
        {
            return baseForce;
        }

        // Calculate velocity alignment with target direction
        Vector2 velocity2D = new Vector2(userVelocity.x, userVelocity.z);

        if (velocity2D.magnitude < 0.01f)
        {
            // User not moving, return full force
            return baseForce;
        }

        // Alignment: 1.0 if moving directly towards target, -1.0 if away, 0 if perpendicular
        float alignment = Vector2.Dot(velocity2D.normalized, baseForce.normalized);

        // Modulate force based on alignment
        // If already moving towards target, reduce attractive force
        // If moving away, apply full force
        float velocityModifier = 1.0f - velocityWeight * Mathf.Clamp01(alignment);

        return baseForce * velocityModifier;
    }

    /// <summary>
    /// Sets the attraction strength parameter.
    /// </summary>
    public void SetAttractionStrength(float strength)
    {
        this.k_att = Mathf.Max(0, strength);
    }

    /// <summary>
    /// Sets the saturation distance parameter.
    /// </summary>
    public void SetSaturationDistance(float distance)
    {
        this.saturationDistance = Mathf.Max(0.1f, distance);
    }

    /// <summary>
    /// Sets the velocity weight parameter.
    /// </summary>
    public void SetVelocityWeight(float weight)
    {
        this.velocityWeight = Mathf.Clamp01(weight);
    }

    /// <summary>
    /// Gets current attraction strength.
    /// </summary>
    public float GetAttractionStrength()
    {
        return k_att;
    }

    /// <summary>
    /// Calculates the potential energy at a given position (for visualization/debugging).
    /// U_att = 0.5 * k_att * distance^2 (if distance <= d_sat)
    /// U_att = k_att * d_sat * distance - 0.5 * k_att * d_sat^2 (if distance > d_sat)
    /// </summary>
    public float CalculatePotential(Vector3 position, Vector3 target)
    {
        Vector2 pos2D = Utilities.FlattenedPos2D(position);
        Vector2 target2D = Utilities.FlattenedPos2D(target);

        float distance = Vector2.Distance(pos2D, target2D);

        if (distance <= saturationDistance)
        {
            // Parabolic potential
            return 0.5f * k_att * distance * distance;
        }
        else
        {
            // Conic potential
            return k_att * saturationDistance * distance -
                   0.5f * k_att * saturationDistance * saturationDistance;
        }
    }
}
