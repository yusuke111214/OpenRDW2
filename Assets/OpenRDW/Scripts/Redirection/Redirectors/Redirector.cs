using UnityEngine;
using System.Collections;

public abstract class Redirector : MonoBehaviour
{
    [HideInInspector]
    public GlobalConfiguration globalConfiguration;

    [HideInInspector]
    public RedirectionManager redirectionManager;

    [HideInInspector]
    public MovementManager movementManager;
    [HideInInspector]
    public VisualizationManager visualizationManager;

    Vector3 translation;
    float rotationInDegrees;

    void Awake()
    {
        globalConfiguration = GetComponentInParent<GlobalConfiguration>();
        redirectionManager = GetComponent<RedirectionManager>();
        movementManager = GetComponent<MovementManager>();
        visualizationManager = GetComponent<VisualizationManager>();
    }

    /// <summary>
    /// Applies redirection based on the algorithm.
    /// </summary>
    public void ClearGains()
    {
        translation = Vector3.zero;
        rotationInDegrees = 0;
    }
    public void ApplyGains()
    {
        transform.Translate(translation, Space.World);
        transform.RotateAround(Utilities.FlattenedPos3D(redirectionManager.headTransform.position), Vector3.up, rotationInDegrees);
    }
    public abstract void InjectRedirection();

    protected void SetTranslationGain(float gt)
    {
        gt = Mathf.Max(gt, globalConfiguration.MIN_TRANS_GAIN);
        gt = Mathf.Min(gt, globalConfiguration.MAX_TRANS_GAIN);
        redirectionManager.gt = gt;
        translation = redirectionManager.deltaPos * (gt - 1);
        globalConfiguration.statisticsLogger.Event_Translation_Gain(movementManager.avatarId, gt, translation);
    }
    protected void SetRotationGain(float gr)
    {
        if (redirectionManager.isRotating)
        {
            gr = Mathf.Max(gr, globalConfiguration.MIN_ROT_GAIN);
            gr = Mathf.Min(gr, globalConfiguration.MAX_ROT_GAIN);
            redirectionManager.gr = gr;
            var rotationInDegreesGR = redirectionManager.deltaDir * (gr - 1);
            bool applied = Mathf.Abs(rotationInDegreesGR) > Mathf.Abs(rotationInDegrees);

            // 問題10対策C: applied状態をログ出力（Redirected Avatarのみ）
            if (Mathf.Abs(rotationInDegreesGR) > 0.01f && transform.name == "Redirected Avatar")
            {
                Debug.Log($"[Redirector.SetRotationGain] avatar={transform.name}, gr={gr:F2}, rotationGR={rotationInDegreesGR:F2}°, prevRotation={rotationInDegrees:F2}°, applied={applied}");
            }

            if (applied)
            {
                rotationInDegrees = rotationInDegreesGR;
                GetComponentInChildren<KeyboardController>().SetLastRotation(rotationInDegreesGR);
            }
            globalConfiguration.statisticsLogger.Event_Rotation_Gain(movementManager.avatarId, gr, rotationInDegreesGR);
        }
    }
    protected void SetCurvature(float curvature)// positive means turning left
    {
        if (redirectionManager.isWalking)
        {
            float originalCurvature = curvature;
            curvature = Mathf.Max(curvature, -1 / globalConfiguration.CURVATURE_RADIUS);
            curvature = Mathf.Min(curvature, 1 / globalConfiguration.CURVATURE_RADIUS);
            redirectionManager.curvature = curvature;
            var rotationInDegreesGC = Mathf.Rad2Deg * redirectionManager.deltaPos.magnitude * curvature;
            bool applied = Mathf.Abs(rotationInDegreesGC) > Mathf.Abs(rotationInDegrees);

            // 問題10対策C: deltaPos.magnitudeをログ出力（Redirected Avatarのみ）
            if (Mathf.Abs(originalCurvature) > 0.01f && transform.name == "Redirected Avatar")
            {
                Debug.Log($"[Redirector.SetCurvature] avatar={transform.name}, curvature={curvature:F3}, deltaPos={redirectionManager.deltaPos.magnitude:F4}m, rotationGC={rotationInDegreesGC:F2}°, applied={applied}");
            }

            if (applied)
            {
                rotationInDegrees = rotationInDegreesGC;
                GetComponentInChildren<KeyboardController>().SetLastRotation(rotationInDegreesGC);
            }

            globalConfiguration.statisticsLogger.Event_Curvature_Gain(movementManager.avatarId, curvature, rotationInDegreesGC);
        }
    }

    public virtual void GetPriority()
    { }
}
