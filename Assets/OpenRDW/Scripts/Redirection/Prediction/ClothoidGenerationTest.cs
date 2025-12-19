using UnityEngine;
using Curves;

/// <summary>
/// Curvesライブラリの統合とクロソイド生成を検証するテストスクリプト
///
/// 使い方：
/// 1. シーン内のGameObjectにこのスクリプトをアタッチ
/// 2. UnityエディタでContextメニュー（右クリック）からテスト実行
/// 3. コンソールで結果を確認
///
/// 注意：
/// - 検証完了後は削除可能です
/// - デバッグ用のスクリプトなので、本番ビルドには不要
/// </summary>
public class ClothoidGenerationTest : MonoBehaviour
{
    [Header("テストパラメータ")]
    [Tooltip("Start()時にテストを自動実行")]
    public bool runTestOnStart = false;

    [Header("テスト設定")]
    public Vector2 startPosition = new Vector2(0, 0);
    public Vector2 startDirection = new Vector2(1, 0); // 右向き
    public Vector2 endPosition = new Vector2(3, 2);

    void Start()
    {
        if (runTestOnStart)
        {
            RunAllTests();
        }
    }

    [ContextMenu("全テスト実行")]
    public void RunAllTests()
    {
        Debug.Log("=== クロソイド生成テスト開始 ===");

        TestBasicClothoidCreation();
        TestCurvesWrapper();
        TestTrajectoryClass();
        TestRedirectionActionFactory();
        TestPathSmoother();

        Debug.Log("=== 全テスト完了 ===");
    }

    [ContextMenu("テスト1: 基本的なクロソイド生成")]
    void TestBasicClothoidCreation()
    {
        Debug.Log("\n--- テスト1: 基本的なクロソイド生成 ---");

        try
        {
            // Curvesライブラリを直接使用してテスト
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
                Debug.Log($"✓ クロソイド生成成功！");
                Debug.Log($"  - クロソイドパラメータA: {clothoid.A}");
                Debug.Log($"  - 開始点: ({startPosition.x}, {startPosition.y})");
                Debug.Log($"  - 終了点: ({endPosition.x}, {endPosition.y})");

                // いくつかの点をサンプリング
                Point2D midPoint = clothoid.InterpolatePoint2D(0.5);
                Debug.Log($"  - 中間点: ({midPoint.X:F3}, {midPoint.Y:F3})");
            }
            else
            {
                Debug.LogError("✗ クロソイド生成がnullを返しました！");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ クロソイド生成中に例外発生: {e.Message}");
        }
    }

    [ContextMenu("テスト2: CurvesWrapper")]
    void TestCurvesWrapper()
    {
        Debug.Log("\n--- テスト2: CurvesWrapper ---");

        try
        {
            // ラッパーメソッドをテスト
            Clothoid clothoid = CurvesWrapper.CreateClothoidFromPoseAndPoint(
                startPosition,
                startDirection,
                endPosition
            );

            if (CurvesWrapper.IsValidClothoid(clothoid))
            {
                Debug.Log("✓ CurvesWrapperが有効なクロソイドを生成！");

                // サンプリングテスト
                Vector2[] points = CurvesWrapper.SamplePoints(clothoid, 10);
                Debug.Log($"  - {points.Length}個の点をサンプリング");
                Debug.Log($"  - 最初の点: {points[0]}");
                Debug.Log($"  - 最後の点: {points[points.Length - 1]}");

                // 長さ計算テスト
                float length = CurvesWrapper.GetApproximateLength(clothoid);
                Debug.Log($"  - 近似長さ: {length:F3} メートル");

                // 曲率テスト
                float curvStart = CurvesWrapper.GetCurvatureAt(clothoid, 0f);
                float curvEnd = CurvesWrapper.GetCurvatureAt(clothoid, 1f);
                Debug.Log($"  - 開始点の曲率: {curvStart:F4}");
                Debug.Log($"  - 終了点の曲率: {curvEnd:F4}");
            }
            else
            {
                Debug.LogError("✗ CurvesWrapperが無効なクロソイドを生成！");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"✗ CurvesWrapperで例外発生: {e.Message}");
        }
    }

    [ContextMenu("テスト3: Trajectoryクラス")]
    void TestTrajectoryClass()
    {
        Debug.Log("\n--- テスト3: Trajectoryクラス ---");

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

    [ContextMenu("テスト4: RedirectionActionファクトリー")]
    void TestRedirectionActionFactory()
    {
        Debug.Log("\n--- テスト4: RedirectionActionファクトリー ---");

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

    [ContextMenu("テスト5: PathSmoother")]
    void TestPathSmoother()
    {
        Debug.Log("\n--- テスト5: PathSmoother ---");

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

    // Sceneビューでの可視化ヘルパー
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // シーンに簡単なテスト用クロソイドを描画
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

                // 開始点と終了点を描画
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(new Vector3(startPosition.x, 0.1f, startPosition.y), 0.1f);
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(new Vector3(endPosition.x, 0.1f, endPosition.y), 0.1f);
            }
        }
        catch { }
    }
}
