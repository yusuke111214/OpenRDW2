using UnityEngine;

/// <summary>
/// Defines the type of redirection gain to apply.
/// </summary>
public enum RedirectionGainType
{
    Translation,        // Translational gain (scaling forward movement)
    Rotation,          // Rotational gain (scaling head rotation)
    Curvature,         // Curvature gain (injecting path curvature)
    Combined,          // Combined translation and curvature (as per paper)
    Null              // No redirection applied
}

/// <summary>
/// Represents a single redirection action with a specific gain type and value.
/// Used in predictive RDW algorithms to evaluate different redirection strategies.
/// </summary>
public class RedirectionAction
{
    /// <summary>
    /// The type of gain to apply.
    /// </summary>
    public RedirectionGainType gainType;

    /// <summary>
    /// Primary gain value.
    /// - For Translation: gain multiplier (e.g., 0.86 to 1.26)
    /// - For Rotation: gain multiplier (e.g., 0.8 to 1.49)
    /// - For Curvature: curvature value in 1/radius (e.g., -7.5 to 7.5)
    /// </summary>
    public float primaryValue;

    /// <summary>
    /// Secondary gain value (used for Combined type).
    /// When gainType is Combined, this holds the curvature value while primaryValue holds translation.
    /// </summary>
    public float secondaryValue;

    /// <summary>
    /// Constructor for single-value actions.
    /// </summary>
    public RedirectionAction(RedirectionGainType type, float value)
    {
        this.gainType = type;
        this.primaryValue = value;
        this.secondaryValue = 0f;
    }

    /// <summary>
    /// Constructor for combined actions (translation + curvature).
    /// </summary>
    public RedirectionAction(RedirectionGainType type, float translationGain, float curvatureGain)
    {
        this.gainType = type;
        this.primaryValue = translationGain;
        this.secondaryValue = curvatureGain;
    }

    /// <summary>
    /// Constructor for null action.
    /// </summary>
    public static RedirectionAction CreateNullAction()
    {
        return new RedirectionAction(RedirectionGainType.Null, 1f);
    }

    /// <summary>
    /// Note: Action application is handled by the redirector itself (e.g., PredRedLPP_Redirector)
    /// because SetTranslationGain/SetRotationGain/SetCurvature are protected methods.
    /// This class only stores the action data.
    /// </summary>

    /// <summary>
    /// Creates a copy of this action.
    /// </summary>
    public RedirectionAction Clone()
    {
        return new RedirectionAction(gainType, primaryValue, secondaryValue);
    }

    /// <summary>
    /// String representation for debugging.
    /// </summary>
    public override string ToString()
    {
        if (gainType == RedirectionGainType.Combined)
        {
            return $"{gainType}: T={primaryValue:F3}, C={secondaryValue:F3}";
        }
        else if (gainType == RedirectionGainType.Null)
        {
            return "Null Action";
        }
        else
        {
            return $"{gainType}: {primaryValue:F3}";
        }
    }

    /// <summary>
    /// Checks if two actions are equivalent.
    /// </summary>
    public bool IsEquivalentTo(RedirectionAction other)
    {
        if (other == null) return false;
        if (gainType != other.gainType) return false;

        const float epsilon = 0.0001f;
        bool primaryMatch = Mathf.Abs(primaryValue - other.primaryValue) < epsilon;
        bool secondaryMatch = Mathf.Abs(secondaryValue - other.secondaryValue) < epsilon;

        return primaryMatch && secondaryMatch;
    }
}

/// <summary>
/// Factory class for generating action sets used in predictive RDW.
/// </summary>
public static class RedirectionActionFactory
{
    /// <summary>
    /// Generates the action set U as described in the PredRedLPP paper.
    /// Creates 6 uniformly distributed values for each gain type within the noticeability thresholds.
    /// </summary>
    /// <param name="config">Global configuration containing gain thresholds</param>
    /// <returns>List of redirection actions</returns>
    public static System.Collections.Generic.List<RedirectionAction> GenerateActionSet(GlobalConfiguration config)
    {
        var actions = new System.Collections.Generic.List<RedirectionAction>();

        // Number of discrete values per gain type (as per paper)
        const int numSteps = 6;

        // Translation gains (6 values from MIN to MAX)
        for (int i = 0; i < numSteps; i++)
        {
            float t = (float)i / (numSteps - 1);
            float gt = Mathf.Lerp(config.MIN_TRANS_GAIN, config.MAX_TRANS_GAIN, t);
            actions.Add(new RedirectionAction(RedirectionGainType.Translation, gt));
        }

        // Rotation gains (6 values from MIN to MAX)
        for (int i = 0; i < numSteps; i++)
        {
            float t = (float)i / (numSteps - 1);
            float gr = Mathf.Lerp(config.MIN_ROT_GAIN, config.MAX_ROT_GAIN, t);
            actions.Add(new RedirectionAction(RedirectionGainType.Rotation, gr));
        }

        // Curvature gains (6 values from -1/R to +1/R)
        for (int i = 0; i < numSteps; i++)
        {
            float t = (float)i / (numSteps - 1);
            float gc = Mathf.Lerp(-1f / config.CURVATURE_RADIUS, 1f / config.CURVATURE_RADIUS, t);
            actions.Add(new RedirectionAction(RedirectionGainType.Curvature, gc));
        }

        // Optional: Combined gains (translation + curvature)
        // This can be enabled based on configuration
        // For now, commented out to keep action set size manageable
        /*
        for (int i = 0; i < 3; i++) // Fewer combined actions to reduce computation
        {
            float t = (float)i / 2f;
            float gt = Mathf.Lerp(config.MIN_TRANS_GAIN, config.MAX_TRANS_GAIN, t);
            float gc = Mathf.Lerp(-1f / config.CURVATURE_RADIUS, 1f / config.CURVATURE_RADIUS, t);
            actions.Add(new RedirectionAction(RedirectionGainType.Combined, gt, gc));
        }
        */

        // Null redirection (always included)
        actions.Add(RedirectionAction.CreateNullAction());

        return actions;
    }

    /// <summary>
    /// Generates a minimal action set for testing purposes.
    /// </summary>
    public static System.Collections.Generic.List<RedirectionAction> GenerateMinimalActionSet(GlobalConfiguration config)
    {
        var actions = new System.Collections.Generic.List<RedirectionAction>();

        // Only max/min values for each gain type
        actions.Add(new RedirectionAction(RedirectionGainType.Translation, config.MIN_TRANS_GAIN));
        actions.Add(new RedirectionAction(RedirectionGainType.Translation, config.MAX_TRANS_GAIN));

        actions.Add(new RedirectionAction(RedirectionGainType.Rotation, config.MIN_ROT_GAIN));
        actions.Add(new RedirectionAction(RedirectionGainType.Rotation, config.MAX_ROT_GAIN));

        actions.Add(new RedirectionAction(RedirectionGainType.Curvature, -1f / config.CURVATURE_RADIUS));
        actions.Add(new RedirectionAction(RedirectionGainType.Curvature, 1f / config.CURVATURE_RADIUS));

        actions.Add(RedirectionAction.CreateNullAction());

        return actions;
    }
}
