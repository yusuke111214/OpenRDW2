// Predictive Redirected Walking using Lemniscate Path Prediction (PredRedLPP)
// 予測的リダイレクテッドウォーキング - レムニスケートパス予測を使用
// 論文: "Predictive multiuser redirected walking using artificial potential fields"
// (Hirt et al., 2024)
// Paper: https://www.frontiersin.org/articles/10.3389/frvir.2024.1365344/full

using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// PredRedLPPリダイレクター - 予測的パスプランニングと人工ポテンシャル場を組み合わせ
///
/// このアルゴリズムの特徴：
/// - レムニスケートベースのクロソイド曲線でユーザーの軌跡を予測
/// - APFベースのコスト関数でリダイレクションアクションを評価
/// - 複数の予測軌跡から最適なものを選択
///
/// 処理フロー（毎フレーム）：
/// 1. 履歴更新：位置・向きを記録
/// 2. 平滑化（オプション）：HMD用のノイズ除去
/// 3. 予測：レムニスケート形状から軌跡生成
/// 4. フィルタリング：障害物衝突チェック
/// 5. 評価：コスト関数で最適軌跡を選択
/// 6. リダイレクション適用：選択した軌跡に従ってゲイン設定
/// </summary>
public class PredRedLPP_Redirector : APF_Redirector
{
    [Header("PredRedLPPコンポーネント")]
    private LemniscatePathPredictor pathPredictor;  // パス予測器
    private TrajectoryEvaluator trajectoryEvaluator;  // 軌跡評価器
    private PathSmoother smoother;  // パス平滑化器

    [Header("移動履歴")]
    private Queue<Vector3> positionHistory;  // 位置履歴（キュー構造）
    private Queue<Vector3> directionHistory;  // 向き履歴
    private const int MAX_HISTORY_SIZE = 10;  // 履歴の最大サイズ

    [Header("現在の状態")]
    private Trajectory currentBestTrajectory;  // 現在選択されている最良の軌跡

    [Header("可視化")]
    private GameObject trajectoryVisualizer;  // 軌跡描画用オブジェクト
    private LineRenderer trajectoryLineRenderer;  // 軌跡描画用LineRenderer
    private GameObject lemniscateVisualizer;  // レムニスケート描画用オブジェクト
    private LineRenderer lemniscateLineRenderer;  // レムニスケート描画用LineRenderer

    [Header("デバッグ")]
    [Tooltip("コンソールにデバッグ情報を表示")]
    public bool showDebugInfo = true;
    [Tooltip("シーンビューで予測軌跡を可視化")]
    public bool visualizePredictions = true;

    private bool isInitialized = false;

    void Start()
    {
        EnsureInitialized();
    }

    /// <summary>
    /// コンポーネントが初期化されているか確認（遅延初期化）
    ///
    /// 遅延初期化とは：
    /// - 最初に必要になったタイミングで初期化
    /// - Awake()での初期化の順序問題を回避
    /// </summary>
    private void EnsureInitialized()
    {
        if (isInitialized)
            return;

        InitializeComponents();
        InitializeHistoryBuffers();

        isInitialized = true;
    }

    /// <summary>
    /// 全ての予測・評価コンポーネントを初期化
    ///
    /// 初期化内容：
    /// 1. パス予測器（LemniscatePathPredictor）を追加/取得
    /// 2. 軌跡評価器（TrajectoryEvaluator）を作成
    /// 3. パス平滑化器（PathSmoother）を作成（デフォルトOFF）
    /// 4. 可視化オブジェクトを作成
    /// </summary>
    private void InitializeComponents()
    {
        // パス予測器コンポーネントを追加
        pathPredictor = gameObject.GetComponent<LemniscatePathPredictor>();
        if (pathPredictor == null)
        {
            pathPredictor = gameObject.AddComponent<LemniscatePathPredictor>();
        }

        // 軌跡評価器を初期化
        trajectoryEvaluator = new TrajectoryEvaluator(globalConfiguration);

        // パス平滑化器を初期化（シミュレーション用にデフォルトで無効）
        smoother = new PathSmoother();
        smoother.SetEnabled(globalConfiguration.enablePathSmoothing);

        // 可視化GameObjectを初期化
        InitializeVisualization();
    }

    /// <summary>
    /// 可視化GameObjectを初期化（APF_Redirectorパターンに類似）
    ///
    /// 作成するオブジェクト：
    /// 1. PredRedLPP_Trajectory：選択された最適軌跡（緑色）
    /// 2. PredRedLPP_Lemniscate：レムニスケート形状（黄色）
    ///
    /// 親子関係：
    /// - これらはリダイレクターの子として作成
    /// - useWorldSpace=trueでワールド座標を使用（Arrow(Clone)と同じアプローチ）
    /// - 毎フレーム位置を更新して追従を実現
    /// </summary>
    private void InitializeVisualization()
    {
        if (globalConfiguration.runInBackstage || !visualizePredictions)
            return;

        // 軌跡可視化オブジェクトを作成
        trajectoryVisualizer = new GameObject("PredRedLPP_Trajectory");
        trajectoryVisualizer.transform.SetParent(transform);
        trajectoryVisualizer.transform.localPosition = Vector3.zero;
        trajectoryVisualizer.transform.localRotation = Quaternion.identity;

        trajectoryLineRenderer = trajectoryVisualizer.AddComponent<LineRenderer>();
        trajectoryLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        // より目立つ明るいシアン色（緑+青）に変更
        trajectoryLineRenderer.startColor = new Color(0f, 1f, 1f, 1f);  // シアン：最適軌跡
        trajectoryLineRenderer.endColor = new Color(0f, 1f, 1f, 1f);
        // 幅を大きくして見やすく
        trajectoryLineRenderer.startWidth = 0.2f;
        trajectoryLineRenderer.endWidth = 0.2f;
        trajectoryLineRenderer.positionCount = 0;
        trajectoryLineRenderer.useWorldSpace = true; // ワールド空間座標を使用（Arrow(Clone)と同じ）
        trajectoryLineRenderer.enabled = visualizationManager.ifVisible;

        // レムニスケート可視化オブジェクトを作成
        lemniscateVisualizer = new GameObject("PredRedLPP_Lemniscate");
        lemniscateVisualizer.transform.SetParent(transform);
        lemniscateVisualizer.transform.localPosition = Vector3.zero;
        lemniscateVisualizer.transform.localRotation = Quaternion.identity;

        lemniscateLineRenderer = lemniscateVisualizer.AddComponent<LineRenderer>();
        lemniscateLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        // より明るい黄色に変更
        lemniscateLineRenderer.startColor = new Color(1f, 1f, 0f, 0.6f);  // 黄色（半透明）：レムニスケート形状
        lemniscateLineRenderer.endColor = new Color(1f, 1f, 0f, 0.6f);
        // 幅を少し大きくして見やすく
        lemniscateLineRenderer.startWidth = 0.08f;
        lemniscateLineRenderer.endWidth = 0.08f;
        lemniscateLineRenderer.positionCount = 0;
        lemniscateLineRenderer.useWorldSpace = true; // ワールド空間座標を使用（Arrow(Clone)と同じ）
        lemniscateLineRenderer.enabled = visualizationManager.ifVisible;
    }

    /// <summary>
    /// 可視化GameObjectのクリーンアップ
    ///
    /// オブジェクト破棄時：
    /// - 作成した可視化オブジェクトを削除
    /// - メモリリークを防ぐ
    /// </summary>
    private void OnDestroy()
    {
        if (trajectoryVisualizer != null)
            Destroy(trajectoryVisualizer);
        if (lemniscateVisualizer != null)
            Destroy(lemniscateVisualizer);
    }

    /// <summary>
    /// 位置と向きの履歴バッファを初期化
    ///
    /// キュー（Queue）とは：
    /// - 先入れ先出し（FIFO）のデータ構造
    /// - 古いデータを自動削除して、最新N個を保持
    /// </summary>
    private void InitializeHistoryBuffers()
    {
        positionHistory = new Queue<Vector3>();
        directionHistory = new Queue<Vector3>();
    }

    /// <summary>
    /// 毎フレーム呼び出されるメインのリダイレクション適用メソッド
    ///
    /// PredRedLPPアルゴリズムの完全なパイプラインを実装（論文準拠）：
    /// Step 1: 履歴更新
    /// Step 2: 平滑化（オプション）
    /// Step 3: 予測軌跡生成（レムニスケート + クロソイド）
    /// Step 4: Scene awareness（障害物衝突検出）
    /// Step 4.5: Path similarity measure（MSEで最良軌跡T_predを選択）← 問題4の解決
    /// Step 5: アクションセット生成
    /// Step 6: アクション評価（T_predに各アクションを適用してコスト評価）
    /// Step 7: リダイレクション適用
    /// Step 8-9: 可視化更新
    /// </summary>
    public override void InjectRedirection()
    {
        // コンポーネントが初期化されているか確認
        EnsureInitialized();

        // Step 1: 移動履歴を更新
        UpdateHistory();

        // Step 2: 有効な場合は平滑化を適用（HMD使用時）
        Queue<Vector3> smoothedPositions = positionHistory;
        Queue<Vector3> smoothedDirections = directionHistory;

        if (globalConfiguration.enablePathSmoothing)
        {
            var smoothedPosList = smoother.SmoothPositions(positionHistory);
            var smoothedDirList = smoother.SmoothDirections(directionHistory);

            smoothedPositions = new Queue<Vector3>(smoothedPosList);
            smoothedDirections = new Queue<Vector3>(smoothedDirList);
        }

        // Step 3: 予測軌跡を生成
        List<Trajectory> predictions = pathPredictor.GenerateTrajectories(
            smoothedPositions,
            smoothedDirections,
            redirectionManager.currPosReal,
            redirectionManager.currDirReal
        );

        if (predictions.Count == 0)
        {
            // 予測が利用できない場合、ヌルアクションを適用
            ApplyNullRedirection();
            return;
        }

        // Step 4: 実行可能な軌跡をフィルタリング（シーン認識）
        SingleSpace physicalSpace = globalConfiguration.physicalSpaces[movementManager.physicalSpaceIndex];
        List<Trajectory> feasibleTrajectories = pathPredictor.FilterFeasibleTrajectories(
            predictions,
            physicalSpace
        );

        if (feasibleTrajectories.Count == 0)
        {
            // 実行可能な軌跡がない場合、リアクティブ動作にフォールバック
            ApplyReactiveRedirection(physicalSpace);
            return;
        }

        // Step 4.5: Path similarity measureで単一の最良軌跡T_predを選択（論文Section 3.2.2, page 5）
        // 論文より："From this generated set of trajectories, a single best prediction
        // is isolated using a simple mean-squared error enhanced by a discount factor."
        //
        // 2段階選択プロセスの第1段階（予測）- 論文準拠
        // HMD履歴との類似度（MSE）を使って、最もユーザーの移動パターンに近い軌跡を選択
        Trajectory T_pred = pathPredictor.SelectBestTrajectoryUsingSimilarityMeasure(
            feasibleTrajectories,
            positionHistory
        );

        if (T_pred == null)
        {
            if (showDebugInfo)
                Debug.LogWarning("[PredRedLPP] T_pred is null. Applying null redirection.");
            ApplyNullRedirection();
            return;
        }

        if (showDebugInfo && T_pred.points.Count == 0)
        {
            Debug.LogWarning($"[PredRedLPP] T_pred has no points. Trajectory ID issue.");
        }

        // Step 5: アクションセット U を生成（論文 Table 2, Section 3.3.1）
        // Inspector設定に基づいて、Minimalアクションセット（7個）または完全なアクションセット（19個）を使用
        List<RedirectionAction> actionSet = globalConfiguration.useMinimalActionSet
            ? RedirectionActionFactory.GenerateMinimalActionSet(globalConfiguration)
            : RedirectionActionFactory.GenerateActionSet(globalConfiguration);

        // Step 6: T_predに各アクションを適用して評価（論文Section 3.3, page 6）
        // 論文より："Conceptually, the predictive RDW entails a simple approach:
        // • it predicts a single path Tpred;
        // • Tpred is redirected based on an action set U consisting of multiple
        //   redirection techniques and different gains, resulting in Tred;
        // • a cost-based analysis of Tred is used to identify the best redirection πoptimal ∈ U"
        //
        // 2段階選択プロセスの第2段階（アクション選択）- 論文準拠
        // 単一の軌跡T_predに対して、全てのアクション∈Uを評価
        // 計算量：N_trajectories + N_actions（論文準拠の2段階プロセス）
        // 例：11軌跡の評価 + 19アクションの評価 = 30回
        (RedirectionAction bestAction, Trajectory bestTrajectory) = trajectoryEvaluator.EvaluateActionsForSingleTrajectory(
            T_pred,
            actionSet,
            physicalSpace,
            globalConfiguration.redirectedAvatars,
            movementManager.physicalSpaceIndex,
            movementManager.avatarId
        );

        if (bestAction == null || bestTrajectory == null)
        {
            if (showDebugInfo)
                Debug.LogWarning("[PredRedLPP] bestAction or bestTrajectory is null. Applying null redirection.");
            ApplyNullRedirection();
            return;
        }

        currentBestTrajectory = bestTrajectory;

        // Step 7: 選択されたアクション π_optimal を適用（論文 Section 3.3）
        ApplyRedirectionAction(bestAction);

        // Step 8: APF可視化を更新
        if (bestTrajectory != null && bestTrajectory.points.Count > 0)
        {
            // 現在位置でのAPF力を計算（可視化用）
            Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal);
            Vector2 apfForce = CalculateAPFForceAtPosition(currentPos2D, physicalSpace);
            UpdateTotalForcePointer(apfForce);
        }

        // Step 9: 軌跡可視化を更新
        UpdateVisualization();
    }

    /// <summary>
    /// 移動履歴バッファを更新
    ///
    /// 処理内容：
    /// 1. 現在の位置と向きをキューに追加
    /// 2. サイズ制限（MAX_HISTORY_SIZE=10）を超えたら古いデータを削除
    ///
    /// キューの動作：
    /// - Enqueue（追加）：最新データを末尾に追加
    /// - Dequeue（削除）：最も古いデータを先頭から削除
    /// </summary>
    private void UpdateHistory()
    {
        positionHistory.Enqueue(redirectionManager.currPosReal);
        directionHistory.Enqueue(redirectionManager.currDirReal);

        // バッファサイズを制限
        while (positionHistory.Count > MAX_HISTORY_SIZE)
        {
            positionHistory.Dequeue();
        }
        while (directionHistory.Count > MAX_HISTORY_SIZE)
        {
            directionHistory.Dequeue();
        }
    }

    /// <summary>
    /// コスト評価に基づいて最良の軌跡を選択
    ///
    /// 選択プロセス：
    /// 1. 各軌跡のコストを計算（TrajectoryEvaluatorを使用）
    /// 2. 最小コストの軌跡を選択
    /// 3. 選択した軌跡を返す
    /// </summary>
    private Trajectory SelectBestTrajectory(
        List<Trajectory> trajectories,
        SingleSpace physicalSpace)
    {
        if (trajectories.Count == 0)
            return null;

        float minCost = float.MaxValue;
        Trajectory bestTrajectory = null;

        foreach (var trajectory in trajectories)
        {
            // この軌跡のコストを計算
            float cost = trajectoryEvaluator.CalculateTotalCost(
                trajectory,
                physicalSpace,
                globalConfiguration.redirectedAvatars,
                movementManager.physicalSpaceIndex,
                movementManager.avatarId
            );

            trajectory.totalCost = cost;

            // より小さいコストなら更新
            if (cost < minCost)
            {
                minCost = cost;
                bestTrajectory = trajectory;
            }
        }

        return bestTrajectory;
    }

    /// <summary>
    /// 選択された軌跡に基づいてリダイレクションを適用
    ///
    /// ThomasAPFに類似したリアクティブアプローチ：
    /// - 常に3つのゲイン全てを設定（Translation, Rotation, Curvature）
    /// - 軌跡のターゲットポイントに向かうようにゲインを決定
    ///
    /// ゲイン設定の戦略：
    /// - Translation：軌跡との整合性で決定
    /// - Rotation：回転方向で決定
    /// - Curvature：操舵方向で決定
    /// </summary>
    private void ApplyRedirectionFromTrajectory(
        Trajectory trajectory,
        SingleSpace physicalSpace)
    {
        if (trajectory == null || trajectory.points.Count < 2)
        {
            ApplyNullRedirection();
            return;
        }

        // 軌跡から目標方向を取得（最初の数ポイントを使用）
        Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal);
        Vector2 currentDir2D = Utilities.FlattenedDir2D(redirectionManager.currDirReal);

        // 軌跡上のターゲットポイントを見つける（数ステップ先）
        int targetIndex = Mathf.Min(3, trajectory.points.Count - 1);
        Vector2 targetPoint = trajectory.points[targetIndex];

        // 目標方向を計算
        Vector2 desiredDir2D = (targetPoint - currentPos2D).normalized;
        Vector3 desiredFacingDirection = Utilities.UnFlatten(desiredDir2D);

        // 操舵方向を計算（ThomasAPFと同様）
        int desiredSteeringDirection = (-1) * (int)Mathf.Sign(
            Utilities.GetSignedAngle(redirectionManager.currDirReal, desiredFacingDirection)
        );

        // 目標方向との整合性に基づいてTranslation Gainを設定
        float alignment = Vector2.Dot(desiredDir2D, currentDir2D);
        if (alignment < 0)
        {
            // 目標方向から離れている - 圧縮して減速
            SetTranslationGain(globalConfiguration.MIN_TRANS_GAIN);
        }
        else if (alignment > 0.9f)
        {
            // よく整列している - 最大移動量を使用
            SetTranslationGain(globalConfiguration.MAX_TRANS_GAIN);
        }
        else
        {
            // 部分的に整列 - ニュートラル
            SetTranslationGain(1.0f);
        }

        // 回転方向に基づいてRotation Gainを設定
        if (redirectionManager.isRotating)
        {
            if (redirectionManager.deltaDir * desiredSteeringDirection < 0)
            {
                // 目標方向から離れるように回転している
                SetRotationGain(globalConfiguration.MIN_ROT_GAIN);
            }
            else
            {
                // 目標方向に向かって回転している
                SetRotationGain(globalConfiguration.MAX_ROT_GAIN);
            }
        }
        else
        {
            SetRotationGain(1.0f);
        }

        // 軌跡に向かって操舵するためにCurvature Gainを設定
        if (redirectionManager.isWalking)
        {
            SetCurvature(desiredSteeringDirection * 1f / globalConfiguration.CURVATURE_RADIUS);
        }
        else
        {
            SetCurvature(0f);
        }

        // すべてのゲインを適用
        ApplyGains();
    }

    /// <summary>
    /// ヌルリダイレクションを適用（操作なし）
    ///
    /// 処理内容：
    /// - すべてのゲインをニュートラル値に設定
    /// - NullRedirectorと同じ動作
    /// - ユーザーの動きをそのまま仮想空間に反映
    /// </summary>
    private void ApplyNullRedirection()
    {
        // すべてのゲインをニュートラル値に設定（NullRedirectorと同様）
        SetTranslationGain(1.0f);
        SetRotationGain(1.0f);
        SetCurvature(0f);
        ApplyGains();
    }

    /// <summary>
    /// 選択されたリダイレクションアクションを適用（論文準拠）
    ///
    /// 処理内容：
    /// - アクションのゲインタイプに応じて適切なゲイン値を設定
    /// - 使用しないゲインは中立値（Translation/Rotation=1.0, Curvature=0）に設定
    ///
    /// ゲインタイプ別の動作：
    /// - Translation: 並進ゲインのみ適用、他は中立
    /// - Rotation: 回転ゲインのみ適用、他は中立
    /// - Curvature: 曲率ゲインのみ適用、他は中立
    /// - Combined: 並進＋曲率を同時適用、回転は中立
    /// - Null: すべて中立（リダイレクションなし）
    /// </summary>
    /// <param name="action">適用するリダイレクションアクション</param>
    private void ApplyRedirectionAction(RedirectionAction action)
    {
        if (action == null)
        {
            ApplyNullRedirection();
            return;
        }

        switch (action.gainType)
        {
            case RedirectionGainType.Translation:
                // 並進ゲインのみ適用
                SetTranslationGain(action.primaryValue);
                SetRotationGain(1.0f);  // 中立
                SetCurvature(0f);       // 中立
                break;

            case RedirectionGainType.Rotation:
                // 回転ゲインのみ適用
                SetTranslationGain(1.0f);       // 中立
                SetRotationGain(action.primaryValue);
                SetCurvature(0f);       // 中立
                break;

            case RedirectionGainType.Curvature:
                // 曲率ゲインのみ適用
                SetTranslationGain(1.0f);  // 中立
                SetRotationGain(1.0f);     // 中立
                SetCurvature(action.primaryValue);
                break;

            case RedirectionGainType.Combined:
                // 並進＋曲率を同時適用（論文 Table 2）
                SetTranslationGain(action.primaryValue);
                SetRotationGain(1.0f);  // 中立
                SetCurvature(action.secondaryValue);
                break;

            case RedirectionGainType.Null:
            default:
                // 中立値を設定（リダイレクションなし）
                SetTranslationGain(1.0f);
                SetRotationGain(1.0f);
                SetCurvature(0f);
                break;
        }

        // ゲインを適用
        ApplyGains();
    }

    /// <summary>
    /// 実行可能な予測が存在しない場合のフォールバックリアクティブリダイレクション
    ///
    /// 使用タイミング：
    /// - すべての予測軌跡が障害物と衝突する場合
    /// - 予測が生成できない緊急時
    ///
    /// 動作：
    /// - ThomasAPFスタイルのリアクティブ動作を使用
    /// - APFの力の場に基づいてリアルタイムでゲインを設定
    /// </summary>
    private void ApplyReactiveRedirection(SingleSpace physicalSpace)
    {
        // APFの力を計算
        Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal);
        Vector2 ng = CalculateAPFForceAtPosition(currentPos2D, physicalSpace);

        // ThomasAPFと同様のリアクティブリダイレクションを適用
        ApplyRedirectionByNegativeGradient(ng);

        UpdateTotalForcePointer(ng);
    }

    /// <summary>
    /// 特定位置でのAPF反発力を計算
    ///
    /// APF（人工ポテンシャル場）とは：
    /// - 障害物が反発力を生み出す仮想的な力の場
    /// - 距離が近いほど強い力で押し返す
    ///
    /// 計算対象：
    /// 1. 物理空間の境界壁
    /// 2. 障害物
    /// 3. 他のユーザー
    ///
    /// 戻り値：
    /// - 障害物から離れる方向のベクトル（正規化済み）
    /// </summary>
    private Vector2 CalculateAPFForceAtPosition(Vector2 position, SingleSpace physicalSpace)
    {
        List<Vector2> nearestPosList = new List<Vector2>();

        // 物理空間の境界
        for (int i = 0; i < physicalSpace.trackingSpace.Count; i++)
        {
            var p = physicalSpace.trackingSpace[i];
            var q = physicalSpace.trackingSpace[(i + 1) % physicalSpace.trackingSpace.Count];
            var nearestPos = Utilities.GetNearestPos(position, new List<Vector2> { p, q });
            var n = Utilities.RotateVector(q - p, -90).normalized;
            var d = position - nearestPos;

            if (Vector2.Dot(n, d.normalized) > 0)
            {
                nearestPosList.Add(nearestPos);
            }
        }

        // 障害物
        foreach (var obstacle in physicalSpace.obstaclePolygons)
        {
            var nearestPos = Utilities.GetNearestPos(position, obstacle);
            nearestPosList.Add(nearestPos);
        }

        // 他のユーザー
        foreach (var user in globalConfiguration.redirectedAvatars)
        {
            if (user.GetComponent<MovementManager>().physicalSpaceIndex != movementManager.physicalSpaceIndex)
                continue;

            var uId = user.GetComponent<MovementManager>().avatarId;
            if (uId == movementManager.avatarId)
                continue;

            var nearestPos = Utilities.FlattenedPos2D(user.GetComponent<RedirectionManager>().currPosReal);
            nearestPosList.Add(nearestPos);
        }

        // 負の勾配を計算（障害物から離れる方向）
        Vector2 ng = Vector2.zero;
        foreach (var obPos in nearestPosList)
        {
            Vector2 diff = position - obPos;
            float distance = diff.magnitude;

            if (distance > 0.01f)
            {
                var gDelta = -1f / distance * diff.normalized;
                ng += -gDelta;
            }
        }

        return ng.normalized;
    }

    /// <summary>
    /// 負の勾配に基づいてリダイレクションを適用（リアクティブモード）
    ///
    /// ThomasAPFの動作を完全に模倣：
    /// - 3つのゲインすべてを設定
    /// - APFの力の方向に基づいてゲインを決定
    ///
    /// 負の勾配とは：
    /// - 障害物から離れる方向を示すベクトル
    /// - この方向にユーザーを誘導することで衝突を回避
    /// </summary>
    private void ApplyRedirectionByNegativeGradient(Vector2 ng)
    {
        // 負の勾配から目標向き方向を計算
        var desiredFacingDirection = Utilities.UnFlatten(ng);
        int desiredSteeringDirection = (-1) * (int)Mathf.Sign(
            Utilities.GetSignedAngle(redirectionManager.currDirReal, desiredFacingDirection)
        );

        // Translation gain（ThomasAPFと完全同一）
        if (Vector2.Dot(ng, Utilities.FlattenedDir2D(redirectionManager.currDirReal)) < 0)
        {
            SetTranslationGain(globalConfiguration.MAX_TRANS_GAIN);
        }
        else
        {
            SetTranslationGain(1f);
        }

        // Rotation gain（ThomasAPFと完全同一）
        if (redirectionManager.deltaDir * desiredSteeringDirection < 0)
        {
            // 負の勾配から離れるように回転している
            SetRotationGain(globalConfiguration.MIN_ROT_GAIN);
        }
        else
        {
            // 負の勾配に向かって回転している
            SetRotationGain(globalConfiguration.MAX_ROT_GAIN);
        }

        // Curvature gain（ThomasAPFと完全同一）
        SetCurvature(desiredSteeringDirection * 1f / globalConfiguration.CURVATURE_RADIUS);

        // すべてのゲインを適用
        ApplyGains();
    }

    /// <summary>
    /// LineRendererを使用して可視化を更新
    ///
    /// 可視化対象：
    /// 1. レムニスケート形状（黄色）
    /// 2. 選択された最適軌跡（緑色）
    ///
    /// 動作：
    /// - APFの矢印のようにアバターに追従
    /// - useWorldSpace=trueでワールド座標系を使用（Arrow(Clone)と同じ）
    /// </summary>
    private void UpdateVisualization()
    {
        if (!visualizePredictions || globalConfiguration.runInBackstage)
            return;

        if (trajectoryLineRenderer == null || lemniscateLineRenderer == null)
            return;

        // 可視性を更新
        bool isVisible = visualizationManager.ifVisible;
        trajectoryLineRenderer.enabled = isVisible;
        lemniscateLineRenderer.enabled = isVisible;

        if (!isVisible)
            return;

        // ワールド座標での現在位置と方向を取得
        Vector2 currentPos2D = Utilities.FlattenedPos2D(redirectionManager.currPosReal);
        Vector2 currentDir2D = Utilities.FlattenedDir2D(redirectionManager.currDirReal).normalized;

        // レムニスケート可視化を更新
        UpdateLemniscateVisualization(currentPos2D, currentDir2D);

        // 軌跡可視化を更新
        UpdateTrajectoryVisualization();
    }

    /// <summary>
    /// レムニスケート形状の可視化を更新
    ///
    /// レムニスケートとは：
    /// - ∞（無限大）記号のような8の字曲線
    /// - ユーザーの歩行パターンのモデルとして使用
    ///
    /// 処理：
    /// 1. レムニスケート曲線のポイントを生成（物理空間、50ポイント）
    /// 2. 物理空間→仮想空間に座標変換（trackingSpace.TransformPoint）
    /// 3. LineRendererで描画（useWorldSpace = true）
    ///
    /// 座標系の理解：
    /// - origin/directionは物理空間の座標（currPosReal/currDirReal）
    /// - 予測は物理空間で行われる（障害物回避のため）
    /// - 描画は仮想空間で行う（カメラが仮想空間にいるため）
    /// - trackingSpace.TransformPointで物理→仮想変換
    /// </summary>
    private void UpdateLemniscateVisualization(Vector2 origin, Vector2 direction)
    {
        // レムニスケート曲線のポイントを生成（物理空間の2D座標）
        var lemniscatePoints = pathPredictor.GenerateLemniscatePointsForVisualization(
            origin,
            direction,
            50  // 滑らかな曲線のためのポイント数
        );

        if (lemniscatePoints.Count == 0)
        {
            lemniscateLineRenderer.positionCount = 0;
            return;
        }

        // 物理空間から仮想空間に変換
        Vector3[] virtualPositions = new Vector3[lemniscatePoints.Count];
        for (int i = 0; i < lemniscatePoints.Count; i++)
        {
            // 2D（物理空間）→ 3D（物理空間）
            Vector3 physicalPos3D = Utilities.UnFlatten(lemniscatePoints[i]);

            // 3D（物理空間）→ 3D（仮想空間）
            // trackingSpace.TransformPointは物理空間のローカル座標を仮想空間のワールド座標に変換
            virtualPositions[i] = redirectionManager.trackingSpace.TransformPoint(physicalPos3D);
        }

        lemniscateLineRenderer.positionCount = virtualPositions.Length;
        lemniscateLineRenderer.SetPositions(virtualPositions);
    }

    /// <summary>
    /// 最良軌跡の可視化を更新
    ///
    /// 処理：
    /// 1. 現在選択されている最良軌跡のポイントを取得（物理空間の2D座標）
    /// 2. 物理空間→仮想空間に座標変換（trackingSpace.TransformPoint）
    /// 3. LineRendererで緑色の線として描画（useWorldSpace = true）
    ///
    /// 座標系の理解：
    /// - currentBestTrajectory.pointsは物理空間の座標
    /// - 予測軌跡は物理空間で生成される（障害物回避のため）
    /// - 描画は仮想空間で行う（カメラが仮想空間にいるため）
    /// - trackingSpace.TransformPointで物理→仮想変換
    ///
    /// 注意：
    /// - currentBestTrajectoryはInjectRedirection()で更新される
    /// - 軌跡がない場合は何も描画しない
    /// </summary>
    private void UpdateTrajectoryVisualization()
    {
        if (currentBestTrajectory == null || currentBestTrajectory.points.Count == 0)
        {
            trajectoryLineRenderer.positionCount = 0;
            if (showDebugInfo && currentBestTrajectory == null)
            {
                Debug.LogWarning("[PredRedLPP] UpdateTrajectoryVisualization: currentBestTrajectory is null");
            }
            else if (showDebugInfo && currentBestTrajectory != null && currentBestTrajectory.points.Count == 0)
            {
                Debug.LogWarning("[PredRedLPP] UpdateTrajectoryVisualization: currentBestTrajectory has no points");
            }
            return;
        }

        // 物理空間から仮想空間に変換
        Vector3[] virtualPositions = new Vector3[currentBestTrajectory.points.Count];
        for (int i = 0; i < currentBestTrajectory.points.Count; i++)
        {
            // 2D（物理空間）→ 3D（物理空間）
            Vector3 physicalPos3D = Utilities.UnFlatten(currentBestTrajectory.points[i]);

            // 3D（物理空間）→ 3D（仮想空間）
            // trackingSpace.TransformPointは物理空間のローカル座標を仮想空間のワールド座標に変換
            virtualPositions[i] = redirectionManager.trackingSpace.TransformPoint(physicalPos3D);
        }

        trajectoryLineRenderer.positionCount = virtualPositions.Length;
        trajectoryLineRenderer.SetPositions(virtualPositions);
    }
}
