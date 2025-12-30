using UnityEngine;
using System.Collections.Generic;
using Curves;

/// <summary>
/// 予測された軌跡を表すクラス（クロソイド曲線を使用）
///
/// このクラスについて：
/// - ユーザーが歩くと予測される道筋（軌跡）を表現します
/// - クロソイド曲線：車の走行軌跡のような滑らかで自然な曲線です
/// - Curvesライブラリ（外部ツール）とUnityの座標系をつなぐ役割を持ちます
/// </summary>
public class Trajectory
{
    /// <summary>
    /// クロソイド曲線の本体（Curvesライブラリから取得）
    ///
    /// クロソイド曲線とは：
    /// - 曲率（曲がり具合）が一定の割合で変化する曲線のこと
    /// - 車のハンドル操作のように、徐々にカーブしていく自然な動きを表現できます
    /// </summary>
    public Clothoid clothoid;

    /// <summary>
    /// 軌跡上の点のリスト（Unity座標系でVector2として保存）
    ///
    /// これが必要な理由：
    /// - 障害物との衝突判定に使用（ぶつからないか確認）
    /// - 画面上に軌跡を描画する際に使用
    /// - 複数の点で表現することで、曲線を折れ線で近似します
    /// </summary>
    public List<Vector2> points;

    /// <summary>
    /// 軌跡の始点の情報
    ///
    /// startPosition：開始位置（X, Y座標）
    /// startDirection：開始時の向き（度数法、0度=右向き）
    /// </summary>
    public Vector2 startPosition;
    public float startDirection; // 度数法で保存

    /// <summary>
    /// 軌跡の終点の情報
    ///
    /// endPosition：終了位置（X, Y座標）
    /// endDirection：終了時の向き（度数法、0度=右向き）
    /// </summary>
    public Vector2 endPosition;
    public float endDirection; // 度数法で保存

    /// <summary>
    /// この軌跡のコスト（評価値）
    ///
    /// コストとは：
    /// - この軌跡がどれだけ「良い」かを数値化したもの
    /// - 値が小さいほど良い軌跡（壁や障害物から遠い、目標に近いなど）
    /// - PredRedLPPアルゴリズムで最適な軌跡を選ぶ際に使用します
    /// </summary>
    public float totalCost = float.MaxValue;

    /// <summary>
    /// コンストラクタ（クロソイド曲線から軌跡を作成）
    ///
    /// 処理内容：
    /// 1. クロソイド曲線を受け取る
    /// 2. 曲線を等間隔でサンプリング（点を取り出す）
    /// 3. 始点と終点の位置・向きを保存
    /// </summary>
    /// <param name="clothoid">クロソイド曲線オブジェクト</param>
    /// <param name="numSamples">曲線から取り出す点の数（デフォルト20点）</param>
    public Trajectory(Clothoid clothoid, int numSamples = 20)
    {
        this.clothoid = clothoid;
        this.points = new List<Vector2>();

        if (clothoid != null)
        {
            // クロソイド曲線から点をサンプリング（等間隔で点を取り出す）
            for (int i = 0; i <= numSamples; i++)
            {
                double t = (double)i / numSamples;
                Point2D p = clothoid.InterpolatePoint2D(t);
                points.Add(new Vector2((float)p.X, (float)p.Y));
            }

            // 始点と終点の姿勢（位置+向き）を保存
            // 0.0 = 曲線の開始地点、1.0 = 曲線の終了地点
            Pose2D startPose = clothoid.InterpolatePose2D(0.0);
            Pose2D endPose = clothoid.InterpolatePose2D(1.0);

            startPosition = new Vector2((float)startPose.X, (float)startPose.Y);
            startDirection = (float)startPose.Direction * Mathf.Rad2Deg;

            endPosition = new Vector2((float)endPose.X, (float)endPose.Y);
            endDirection = (float)endPose.Direction * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// コンストラクタ（点のリストから軌跡を作成）
    ///
    /// 用途：
    /// - テスト用にカスタムの軌跡を作成したい場合
    /// - クロソイド曲線を使わず、手動で軌跡を定義したい場合
    /// </summary>
    /// <param name="points">軌跡を構成する点のリスト</param>
    public Trajectory(List<Vector2> points)
    {
        this.points = points;
        this.clothoid = null;

        if (points != null && points.Count >= 2)
        {
            startPosition = points[0];
            endPosition = points[points.Count - 1];

            // 向きを近似計算（2点間の方向ベクトルから計算）
            if (points.Count >= 2)
            {
                Vector2 startDir = (points[1] - points[0]).normalized;
                startDirection = Mathf.Atan2(startDir.y, startDir.x) * Mathf.Rad2Deg;

                Vector2 endDir = (points[points.Count - 1] - points[points.Count - 2]).normalized;
                endDirection = Mathf.Atan2(endDir.y, endDir.x) * Mathf.Rad2Deg;
            }
        }
    }

    /// <summary>
    /// 軌跡が仮想障害物と衝突するかチェック
    ///
    /// 処理内容：
    /// - 軌跡上のすべての点について障害物との衝突を確認
    /// - 1つでも衝突していたらtrueを返す
    /// </summary>
    /// <param name="obstaclePolygons">障害物のポリゴン（多角形）リスト</param>
    /// <returns>衝突が検出された場合true</returns>
    public bool CheckCollisionWithObstacles(List<List<Vector2>> obstaclePolygons)
    {
        foreach (var point in points)
        {
            foreach (var obstacle in obstaclePolygons)
            {
                if (Utilities.IfPosInPolygon(obstacle, point))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 軌跡がトラッキング空間内に収まっているかチェック
    ///
    /// トラッキング空間とは：
    /// - ユーザーが物理的に歩ける範囲のこと
    /// - この範囲を出ると、VR体験が中断される可能性があります
    /// </summary>
    /// <param name="trackingSpaceBoundary">トラッキング空間の境界線（ポリゴン）</param>
    /// <returns>軌跡が完全に空間内にある場合true</returns>
    public bool IsWithinTrackingSpace(List<Vector2> trackingSpaceBoundary)
    {
        foreach (var point in points)
        {
            if (!Utilities.IfPosInPolygon(trackingSpaceBoundary, point))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 軌跡の長さを取得
    ///
    /// 計算方法：
    /// - 隣り合う点の間の距離を全て足し合わせる
    /// - 例：点A→点B（1m）+ 点B→点C（1.5m）= 合計2.5m
    /// </summary>
    public float GetLength()
    {
        float length = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            length += Vector2.Distance(points[i - 1], points[i]);
        }
        return length;
    }

    /// <summary>
    /// 軌跡上の特定位置の点を取得
    ///
    /// パラメータtについて：
    /// - t = 0.0：始点
    /// - t = 0.5：中間点
    /// - t = 1.0：終点
    /// - 正規化された値（0～1の範囲）で位置を指定します
    /// </summary>
    public Vector2 GetPointAt(float t)
    {
        if (clothoid != null)
        {
            Point2D p = clothoid.InterpolatePoint2D(t);
            return new Vector2((float)p.X, (float)p.Y);
        }
        else if (points != null && points.Count > 0)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(t * (points.Count - 1)), 0, points.Count - 1);
            return points[index];
        }
        return Vector2.zero;
    }

    /// <summary>
    /// 軌跡上の特定位置での向き（進行方向）を取得
    ///
    /// パラメータtについて：
    /// - t = 0.0：始点での向き
    /// - t = 0.5：中間点での向き
    /// - t = 1.0：終点での向き
    /// </summary>
    /// <returns>向き（度数法、0度=右向き）</returns>
    public float GetDirectionAt(float t)
    {
        if (clothoid != null)
        {
            Pose2D pose = clothoid.InterpolatePose2D(t);
            return (float)pose.Direction * Mathf.Rad2Deg;
        }
        else if (points != null && points.Count >= 2)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(t * (points.Count - 1)), 0, points.Count - 2);
            Vector2 dir = (points[index + 1] - points[index]).normalized;
            return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }
        return 0f;
    }

    /// <summary>
    /// HMD履歴との類似度を計算（Mean-squared error）
    ///
    /// 論文Section 3.2.2（Path similarity measure）:
    /// - MSEを使用してHMDデータバッファとの一致度を計算
    /// - 割引係数で最近の履歴を重視
    /// - この値が小さいほど、ユーザーの実際の移動パターンに近い予測
    ///
    /// 計算方法：
    /// 1. 履歴の各点について、軌跡上の最も近い点との距離を計算
    /// 2. 距離の二乗を計算（MSE）
    /// 3. 新しい履歴ほど高い重みを付ける（割引係数）
    /// 4. 重み付き平均を返す
    ///
    /// 小さいMSE = 履歴に近い軌跡 = より良い予測
    /// </summary>
    /// <param name="positionHistory">HMDの位置履歴（古い順）</param>
    /// <param name="discountFactor">割引係数（デフォルト0.8、最近の履歴を重視）</param>
    /// <returns>MSEスコア（小さいほど良い）</returns>
    public float CalculatePathSimilarity(Queue<Vector3> positionHistory, float discountFactor = 0.8f)
    {
        if (positionHistory == null || positionHistory.Count == 0)
            return float.MaxValue;

        if (points == null || points.Count == 0)
            return float.MaxValue;

        // 履歴を配列に変換（古い順: [0]=最古, [N-1]=最新）
        Vector3[] historyArray = positionHistory.ToArray();

        // MSEを計算
        float mse = 0f;
        float totalWeight = 0f;

        // 履歴の各点について、軌跡上の最も近い点との距離を計算
        for (int i = 0; i < historyArray.Length; i++)
        {
            // 割引係数：新しい履歴ほど重要
            // i=0（最古）→ discount^(N-1)
            // i=N-1（最新）→ discount^0 = 1.0
            int reverseIndex = historyArray.Length - 1 - i;
            float weight = Mathf.Pow(discountFactor, reverseIndex);

            // 履歴点を2Dに変換
            Vector2 historyPoint = Utilities.FlattenedPos2D(historyArray[i]);

            // 軌跡上の最も近い点を見つける
            float minDistance = float.MaxValue;
            foreach (var trajectoryPoint in points)
            {
                float distance = Vector2.Distance(historyPoint, trajectoryPoint);
                if (distance < minDistance)
                    minDistance = distance;
            }

            // 重み付きの二乗誤差を加算
            mse += weight * minDistance * minDistance;
            totalWeight += weight;
        }

        // 正規化
        if (totalWeight > 0)
            mse /= totalWeight;

        return mse;
    }

    /// <summary>
    /// Curvature gain を適用した新しい軌跡を生成（論文のT_red計算）
    ///
    /// 論文の意図：
    /// - Curvature gainは物理空間での軌跡を変更する唯一のゲイン
    /// - ユーザーは仮想空間で直進しているつもりだが、実際には曲線を歩く
    /// - この実際の軌跡（T_red）を計算することで、障害物回避性能を評価できる
    ///
    /// 実装方法：
    /// - 元の軌跡の各セグメント（点と点の間）に対して、curvatureによる曲がりを加える
    /// - 曲がり具合は距離に比例（長く歩くほど大きく曲がる）
    /// </summary>
    /// <param name="curvature">曲率ゲイン値（正=右曲がり、負=左曲がり、単位：1/m）</param>
    /// <returns>Curvatureを適用した新しい軌跡</returns>
    public Trajectory ApplyCurvature(float curvature)
    {
        if (points == null || points.Count < 2)
        {
            // 点が不足している場合は元の軌跡のコピーを返す
            return new Trajectory(new List<Vector2>(points));
        }

        if (Mathf.Abs(curvature) < 0.0001f)
        {
            // Curvatureがほぼ0の場合は元の軌跡のコピーを返す
            return new Trajectory(new List<Vector2>(points));
        }

        List<Vector2> newPoints = new List<Vector2>();
        newPoints.Add(points[0]); // 始点は変わらない

        // 現在の向き（ラジアン）
        Vector2 currentDir = (points[1] - points[0]).normalized;
        float currentAngle = Mathf.Atan2(currentDir.y, currentDir.x);

        // 各セグメントを処理
        for (int i = 1; i < points.Count; i++)
        {
            // 元のセグメントの長さ
            float segmentLength = Vector2.Distance(points[i - 1], points[i]);

            // Curvatureによる回転角度（ラジアン）
            // rotation = curvature * distance
            float rotationAngle = curvature * segmentLength;

            // 新しい向きを計算
            currentAngle += rotationAngle;

            // 新しい向きで次のポイントを計算
            Vector2 direction = new Vector2(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle));
            Vector2 newPoint = newPoints[newPoints.Count - 1] + direction * segmentLength;

            newPoints.Add(newPoint);
        }

        // 新しい軌跡を作成
        Trajectory redirectedTrajectory = new Trajectory(newPoints);

        // コストは未計算としてマーク
        redirectedTrajectory.totalCost = float.MaxValue;

        return redirectedTrajectory;
    }
}
