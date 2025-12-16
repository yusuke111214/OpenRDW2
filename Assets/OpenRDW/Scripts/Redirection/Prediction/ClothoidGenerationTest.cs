using UnityEngine;
using Curves;

/// <summary>
/// Simple test script to verify Curves library integration and clothoid generation.
/// Attach this to a GameObject in the scene to run basic tests in the console.
/// This can be removed after verification.
/// </summary>
public class ClothoidGenerationTest : MonoBehaviour
{
    [Header("Test Parameters")]
    [Tooltip("Enable to run test on Start()")]
    public bool runTestOnStart = false;

    [Header("Test Configuration")]
    public Vector2 startPosition = new Vector2(0, 0);
    public Vector2 startDirection = new Vector2(1, 0); // Facing right
    public Vector2 endPosition = new Vector2(3, 2);

    void Start()
    {
        if (runTestOnStart)
        {
            RunAllTests();
        }
    }

    [ContextMenu("Run All Tests")]
    public void RunAllTests()
    {
        Debug.Log("=== Starting Clothoid Generation Tests ===");

        TestBasicClothoidCreation();
        TestCurvesWrapper();
        TestTrajectoryClass();
        TestRedirectionActionFactory();
        TestPathSmoother();

        Debug.Log("=== All Tests Completed ===");
    }

    [ContextMenu("Test 1: Basic Clothoid Creation")]
    void TestBasicClothoidCreation()
    {
        Debug.Log("\n--- Test 1: Basic Clothoid Creation ---");

        try
        {
            // Test direct Curves library usage
            float angleRad = Mathf.Atan2(startDirection.y, startDirection.x);
            Clothoid clothoid = Clothoid.FromPoseAndPoint(
                (double)startPosition.x,
                (double)startPosition.y,
                (double)angleRad,
                (double)endPosition.x,
                (double)endPosition.y
            );

            if (clothoid != null)
            {
                Debug.Log($"✓ Clothoid created successfully!");
                Debug.Log($"  - Clothoid parameter A: {clothoid.A}");
                Debug.Log($"  - Start: ({startPosition.x}, {startPosition.y})");
                Debug.Log($"  - End: ({endPosition.x}, {endPosition.y})");

                // Sample some points
                Point2D midPoint = clothoid.InterpolatePoint2D(0.5);
                Debug.Log($"  - Midpoint: ({midPoint.X:F3}, {midPoint.Y:F3})");
            }
            else
            {
                Debug.LogError("✗ Clothoid creation returned null!");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ Exception during clothoid creation: {e.Message}");
        }
    }

    [ContextMenu("Test 2: CurvesWrapper")]
    void TestCurvesWrapper()
    {
        Debug.Log("\n--- Test 2: CurvesWrapper ---");

        try
        {
            // Test wrapper method
            Clothoid clothoid = CurvesWrapper.CreateClothoidFromPoseAndPoint(
                startPosition,
                startDirection,
                endPosition
            );

            if (CurvesWrapper.IsValidClothoid(clothoid))
            {
                Debug.Log("✓ CurvesWrapper created valid clothoid!");

                // Test sampling
                Vector2[] points = CurvesWrapper.SamplePoints(clothoid, 10);
                Debug.Log($"  - Sampled {points.Length} points");
                Debug.Log($"  - First point: {points[0]}");
                Debug.Log($"  - Last point: {points[points.Length - 1]}");

                // Test length calculation
                float length = CurvesWrapper.GetApproximateLength(clothoid);
                Debug.Log($"  - Approximate length: {length:F3} meters");

                // Test curvature
                float curvStart = CurvesWrapper.GetCurvatureAt(clothoid, 0f);
                float curvEnd = CurvesWrapper.GetCurvatureAt(clothoid, 1f);
                Debug.Log($"  - Curvature at start: {curvStart:F4}");
                Debug.Log($"  - Curvature at end: {curvEnd:F4}");
            }
            else
            {
                Debug.LogError("✗ CurvesWrapper created invalid clothoid!");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ Exception in CurvesWrapper: {e.Message}");
        }
    }

    [ContextMenu("Test 3: Trajectory Class")]
    void TestTrajectoryClass()
    {
        Debug.Log("\n--- Test 3: Trajectory Class ---");

        try
        {
            // Create a clothoid
            Clothoid clothoid = CurvesWrapper.CreateClothoidFromPoseAndPoint(
                startPosition,
                startDirection,
                endPosition
            );

            if (clothoid != null)
            {
                // Create trajectory wrapper
                Trajectory trajectory = new Trajectory(clothoid, 20);

                Debug.Log("✓ Trajectory created successfully!");
                Debug.Log($"  - Number of points: {trajectory.points.Count}");
                Debug.Log($"  - Start position: {trajectory.startPosition}");
                Debug.Log($"  - Start direction: {trajectory.startDirection:F2}°");
                Debug.Log($"  - End position: {trajectory.endPosition}");
                Debug.Log($"  - End direction: {trajectory.endDirection:F2}°");
                Debug.Log($"  - Length: {trajectory.GetLength():F3} meters");

                // Test point retrieval
                Vector2 midPoint = trajectory.GetPointAt(0.5f);
                float midDirection = trajectory.GetDirectionAt(0.5f);
                Debug.Log($"  - Midpoint: {midPoint}, Direction: {midDirection:F2}°");
            }
            else
            {
                Debug.LogError("✗ Failed to create clothoid for trajectory!");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ Exception in Trajectory test: {e.Message}");
        }
    }

    [ContextMenu("Test 4: RedirectionAction Factory")]
    void TestRedirectionActionFactory()
    {
        Debug.Log("\n--- Test 4: RedirectionAction Factory ---");

        try
        {
            // We need a GlobalConfiguration, so try to find one
            GlobalConfiguration config = FindObjectOfType<GlobalConfiguration>();

            if (config != null)
            {
                var actions = RedirectionActionFactory.GenerateActionSet(config);
                Debug.Log($"✓ Generated {actions.Count} redirection actions");
                Debug.Log($"  - Translation gains: {actions.FindAll(a => a.gainType == RedirectionGainType.Translation).Count}");
                Debug.Log($"  - Rotation gains: {actions.FindAll(a => a.gainType == RedirectionGainType.Rotation).Count}");
                Debug.Log($"  - Curvature gains: {actions.FindAll(a => a.gainType == RedirectionGainType.Curvature).Count}");
                Debug.Log($"  - Null actions: {actions.FindAll(a => a.gainType == RedirectionGainType.Null).Count}");

                // Show some examples
                Debug.Log("\n  Example actions:");
                for (int i = 0; i < Mathf.Min(3, actions.Count); i++)
                {
                    Debug.Log($"    {i + 1}. {actions[i]}");
                }
            }
            else
            {
                Debug.LogWarning("⚠ GlobalConfiguration not found in scene. Creating test config...");

                // Create a temporary test configuration
                GameObject tempObj = new GameObject("TempConfig");
                GlobalConfiguration testConfig = tempObj.AddComponent<GlobalConfiguration>();

                var minimalActions = RedirectionActionFactory.GenerateMinimalActionSet(testConfig);
                Debug.Log($"✓ Generated {minimalActions.Count} minimal actions (test mode)");

                DestroyImmediate(tempObj);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ Exception in RedirectionAction test: {e.Message}");
        }
    }

    [ContextMenu("Test 5: PathSmoother")]
    void TestPathSmoother()
    {
        Debug.Log("\n--- Test 5: PathSmoother ---");

        try
        {
            // Create test data with noise
            var rawPositions = new System.Collections.Generic.Queue<Vector3>();
            for (int i = 0; i < 10; i++)
            {
                float noise = Random.Range(-0.1f, 0.1f);
                rawPositions.Enqueue(new Vector3(i, i + noise, 0));
            }

            // Test with smoothing disabled (simulation mode)
            PathSmoother smootherDisabled = new PathSmoother();
            var resultDisabled = smootherDisabled.SmoothPositions(rawPositions);
            Debug.Log($"✓ PathSmoother (disabled): {resultDisabled.Count} positions (passthrough)");

            // Test with smoothing enabled
            PathSmoother smootherEnabled = new PathSmoother(0.3f, 0.1f, 0.8f, true);
            var resultEnabled = smootherEnabled.SmoothPositions(new System.Collections.Generic.Queue<Vector3>(rawPositions));
            Debug.Log($"✓ PathSmoother (enabled): {resultEnabled.Count} positions (smoothed)");

            // Compare first and last
            Debug.Log($"  - Raw first: {rawPositions.ToArray()[0]}");
            Debug.Log($"  - Smoothed first: {resultEnabled[0]}");
            Debug.Log($"  - Raw last: {rawPositions.ToArray()[9]}");
            Debug.Log($"  - Smoothed last: {resultEnabled[9]}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ Exception in PathSmoother test: {e.Message}");
        }
    }

    // Visualization helper for Scene view
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // Draw a simple test clothoid in the scene
        try
        {
            Clothoid clothoid = CurvesWrapper.CreateClothoidFromPoseAndPoint(
                startPosition,
                startDirection,
                endPosition
            );

            if (clothoid != null)
            {
                Gizmos.color = Color.green;
                Vector2[] points = CurvesWrapper.SamplePoints(clothoid, 30);

                for (int i = 1; i < points.Length; i++)
                {
                    Vector3 p1 = new Vector3(points[i - 1].x, 0.1f, points[i - 1].y);
                    Vector3 p2 = new Vector3(points[i].x, 0.1f, points[i].y);
                    Gizmos.DrawLine(p1, p2);
                }

                // Draw start and end points
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(new Vector3(startPosition.x, 0.1f, startPosition.y), 0.1f);
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(new Vector3(endPosition.x, 0.1f, endPosition.y), 0.1f);
            }
        }
        catch { }
    }
}
