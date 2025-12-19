using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 二重指数平滑化とダンピングを使ったパス平滑化機能
///
/// 平滑化とは：
/// - ノイズ（ランダムなブレ）を除去して、データを滑らかにすること
/// - HMDトラッキングの揺れを軽減するために使用
///
/// シミュレーション環境では：
/// - SimulatedWalkerのデータは既にクリーン（ノイズがない）
/// - この平滑化は無効にできます（デフォルトOFF）
/// </summary>
public class PathSmoother
{
    /// <summary>
    /// Holt-Winters二重指数平滑化のパラメータ
    ///
    /// 専門用語の説明：
    /// - 二重指数平滑化：トレンド（変化の傾向）を考慮した平滑化手法
    /// - Holt-Winters法：時系列データの予測によく使われる統計手法
    /// </summary>
    private float alpha = 0.3f;  // レベル平滑化パラメータ（0～1、大きいほど元データに近い）
    private float beta = 0.1f;   // トレンド平滑化パラメータ（0～1、変化の追従度）
    private float phi = 0.8f;    // ダンピング係数（0～1、予測の減衰率）

    /// <summary>
    /// trueの場合、平滑化をスキップ（パススルーモード）
    ///
    /// 使い分け：
    /// - シミュレーション環境：false（データが既にクリーン）
    /// - HMD使用時：true（トラッキングノイズを除去）
    /// </summary>
    public bool enableSmoothing = false;

    /// <summary>
    /// 二重指数平滑化の内部状態
    ///
    /// これらの変数について：
    /// - level：現在の値（平滑化済み）
    /// - trend：変化の傾向（速度のようなもの）
    /// - 位置と向きでそれぞれ別々に管理
    /// </summary>
    private Vector3 levelPosition;
    private Vector3 trendPosition;
    private Vector3 levelDirection;
    private Vector3 trendDirection;

    private bool initialized = false;

    /// <summary>
    /// コンストラクタ（デフォルトパラメータ、シミュレーション用）
    ///
    /// デフォルト設定：
    /// - 平滑化は無効（シミュレーション環境向け）
    /// </summary>
    public PathSmoother()
    {
        this.enableSmoothing = false;
    }

    /// <summary>
    /// コンストラクタ（カスタムパラメータ指定）
    ///
    /// パラメータ調整：
    /// - alpha：大きい→元データに忠実、小さい→より滑らか
    /// - beta：トレンドの追従度
    /// - phi：予測の減衰率
    /// </summary>
    /// <param name="alpha">レベル平滑化（0～1、大きいほど平滑化が弱い）</param>
    /// <param name="beta">トレンド平滑化（0～1）</param>
    /// <param name="phi">ダンピング係数（0～1）</param>
    /// <param name="enabled">平滑化を有効/無効</param>
    public PathSmoother(float alpha, float beta, float phi, bool enabled = true)
    {
        this.alpha = Mathf.Clamp01(alpha);
        this.beta = Mathf.Clamp01(beta);
        this.phi = Mathf.Clamp01(phi);
        this.enableSmoothing = enabled;
    }

    /// <summary>
    /// 平滑化の状態をリセット
    ///
    /// 使用タイミング：
    /// - 新しいデータ系列を処理する前
    /// - パラメータを変更した後
    /// </summary>
    public void Reset()
    {
        initialized = false;
        levelPosition = Vector3.zero;
        trendPosition = Vector3.zero;
        levelDirection = Vector3.zero;
        trendDirection = Vector3.zero;
    }

    /// <summary>
    /// 位置のキューを平滑化
    ///
    /// 処理内容：
    /// - 生データの履歴を1つずつ処理
    /// - 平滑化されたデータのリストを返す
    /// - 平滑化無効の場合はそのまま返す（パススルー）
    /// </summary>
    /// <param name="rawPositions">生の位置履歴（古い→新しい順）</param>
    /// <returns>平滑化された位置のリスト</returns>
    public List<Vector3> SmoothPositions(Queue<Vector3> rawPositions)
    {
        if (!enableSmoothing || rawPositions == null || rawPositions.Count == 0)
        {
            // パススルーモード（平滑化しない）
            return rawPositions.ToList();
        }

        var smoothed = new List<Vector3>();
        Reset(); // 新しいシーケンスのために状態をリセット

        foreach (var pos in rawPositions)
        {
            smoothed.Add(SmoothPosition(pos));
        }

        return smoothed;
    }

    /// <summary>
    /// 向きのキューを平滑化
    ///
    /// 位置の平滑化と同様の処理：
    /// - 方向ベクトルも同じアルゴリズムで平滑化
    /// - ただし最後に正規化（長さを1に）
    /// </summary>
    /// <param name="rawDirections">生の向き履歴（古い→新しい順）</param>
    /// <returns>平滑化された向きのリスト</returns>
    public List<Vector3> SmoothDirections(Queue<Vector3> rawDirections)
    {
        if (!enableSmoothing || rawDirections == null || rawDirections.Count == 0)
        {
            // パススルーモード（平滑化しない）
            return rawDirections.ToList();
        }

        var smoothed = new List<Vector3>();
        Reset(); // 新しいシーケンスのために状態をリセット

        foreach (var dir in rawDirections)
        {
            smoothed.Add(SmoothDirection(dir));
        }

        return smoothed;
    }

    /// <summary>
    /// 1つの位置観測値に二重指数平滑化を適用（内部処理）
    ///
    /// アルゴリズム：
    /// 1. レベルを更新（現在値の平滑化）
    /// 2. トレンドを更新（変化の傾向）
    /// 3. 予測値を計算（レベル + ダンピング済みトレンド）
    /// </summary>
    private Vector3 SmoothPosition(Vector3 observation)
    {
        if (!initialized)
        {
            // 最初の観測値で初期化
            levelPosition = observation;
            trendPosition = Vector3.zero;
            initialized = true;
            return observation;
        }

        // レベルを更新（現在の値）
        Vector3 prevLevel = levelPosition;
        levelPosition = alpha * observation + (1f - alpha) * (levelPosition + trendPosition);

        // トレンドを更新（変化の傾向）
        trendPosition = beta * (levelPosition - prevLevel) + (1f - beta) * trendPosition;

        // 予測値を計算（ダンピング付き）
        Vector3 smoothed = levelPosition + phi * trendPosition;

        return smoothed;
    }

    /// <summary>
    /// 1つの向き観測値に二重指数平滑化を適用（内部処理）
    ///
    /// 向きの場合：
    /// - 平滑化後に正規化が必要（向きベクトルは長さ1）
    /// </summary>
    private Vector3 SmoothDirection(Vector3 observation)
    {
        if (!initialized)
        {
            // 最初の観測値で初期化（正規化して保存）
            levelDirection = observation.normalized;
            trendDirection = Vector3.zero;
            initialized = true;
            return observation.normalized;
        }

        // 観測値を正規化（向きは長さ1）
        Vector3 normObservation = observation.normalized;

        // レベルを更新
        Vector3 prevLevel = levelDirection;
        levelDirection = alpha * normObservation + (1f - alpha) * (levelDirection + trendDirection);

        // トレンドを更新
        trendDirection = beta * (levelDirection - prevLevel) + (1f - beta) * trendDirection;

        // 予測値を計算（ダンピング付き）
        Vector3 smoothed = levelDirection + phi * trendDirection;

        // 向きなので正規化して返す
        return smoothed.normalized;
    }

    /// <summary>
    /// 1つの位置を平滑化（状態保持版）
    ///
    /// 状態保持とは：
    /// - 前回の処理結果を記憶している
    /// - 連続してこのメソッドを呼ぶことで、滑らかな結果が得られる
    /// </summary>
    public Vector3 SmoothPositionStateful(Vector3 position)
    {
        if (!enableSmoothing)
        {
            return position;
        }
        return SmoothPosition(position);
    }

    /// <summary>
    /// 1つの向きを平滑化（状態保持版）
    ///
    /// 位置の平滑化と同様：
    /// - 連続して呼び出すことで滑らかな結果
    /// </summary>
    public Vector3 SmoothDirectionStateful(Vector3 direction)
    {
        if (!enableSmoothing)
        {
            return direction.normalized;
        }
        return SmoothDirection(direction);
    }

    /// <summary>
    /// 単純移動平均による平滑化（代替手法、状態なし）
    ///
    /// 移動平均とは：
    /// - 直近N個のデータの平均を取る、シンプルな平滑化手法
    /// - 二重指数平滑化より単純だが、トレンドは考慮しない
    /// </summary>
    public static List<Vector3> SimpleMovingAverage(Queue<Vector3> data, int windowSize)
    {
        if (data == null || data.Count == 0)
        {
            return new List<Vector3>();
        }

        windowSize = Mathf.Clamp(windowSize, 1, data.Count);
        var smoothed = new List<Vector3>();
        var dataList = data.ToList();

        for (int i = 0; i < dataList.Count; i++)
        {
            // 移動平均のウィンドウ範囲を計算
            int start = Mathf.Max(0, i - windowSize + 1);
            int count = i - start + 1;

            // ウィンドウ内のデータを合計
            Vector3 sum = Vector3.zero;
            for (int j = start; j <= i; j++)
            {
                sum += dataList[j];
            }

            // 平均を計算
            smoothed.Add(sum / count);
        }

        return smoothed;
    }

    /// <summary>
    /// 実行時に平滑化パラメータを設定
    ///
    /// 使用例：
    /// - HMDのトラッキング品質に応じて調整
    /// - リセットも自動で行われる
    /// </summary>
    public void SetParameters(float alpha, float beta, float phi)
    {
        this.alpha = Mathf.Clamp01(alpha);
        this.beta = Mathf.Clamp01(beta);
        this.phi = Mathf.Clamp01(phi);
        Reset();
    }

    /// <summary>
    /// 平滑化の有効/無効を切り替え
    ///
    /// 有効にした場合：
    /// - 自動的にリセットされる
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        this.enableSmoothing = enabled;
        if (enabled)
        {
            Reset();
        }
    }
}
