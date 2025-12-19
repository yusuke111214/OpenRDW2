using UnityEngine;
using Curves;

/// <summary>
/// Curvesライブラリのラッパークラス（Unityで使いやすくするための変換層）
///
/// ラッパーとは：
/// - 外部ライブラリを使いやすくするための「包み込み」クラス
/// - Curvesライブラリ（double型、ラジアン）とUnity（float型、度数法）の間で
///   座標系とデータ型を自動変換します
///
/// Curvesライブラリについて：
/// - クロソイド曲線を生成する外部ライブラリ
/// - 車の走行軌跡のような滑らかな曲線を数学的に計算できます
/// </summary>
public static class CurvesWrapper
{
    /// <summary>
    /// 開始姿勢と終了地点からクロソイド曲線を作成
    ///
    /// これがPredRedLPPのメイン処理：
    /// - 「今ここにいて、この向きを向いている」状態から
    /// - 「あの地点に到達する」ための滑らかな曲線を自動計算
    ///
    /// 例：
    /// - 開始：位置(0,0)、向き(1,0)=右向き
    /// - 終了：位置(3,2)
    /// - 結果：(0,0)から(3,2)へ滑らかに曲がる軌跡
    /// </summary>
    /// <param name="startPos">開始位置（Unity座標）</param>
    /// <param name="startDir">開始方向（Unity座標のVector2）</param>
    /// <param name="endPos">終了位置（Unity座標）</param>
    /// <returns>クロソイドオブジェクト、生成失敗時はnull</returns>
    public static Clothoid CreateClothoidFromPoseAndPoint(
        Vector2 startPos,
        Vector2 startDir,
        Vector2 endPos)
    {
        // Unity のVector2方向をラジアン角度に変換
        // Atan2：ベクトルから角度を計算する数学関数
        float angleRad = Mathf.Atan2(startDir.y, startDir.x);

        return CreateClothoidFromPoseAndPoint(
            startPos.x,
            startPos.y,
            angleRad,
            endPos.x,
            endPos.y
        );
    }

    /// <summary>
    /// 開始姿勢（位置+角度）と終了地点からクロソイド曲線を作成
    ///
    /// オーバーロード版：
    /// - 上のメソッドから呼び出される内部実装
    /// - 角度を直接ラジアンで指定する場合に使用
    /// </summary>
    /// <param name="startX">開始X座標</param>
    /// <param name="startY">開始Y座標</param>
    /// <param name="startAngleRadians">開始方向（ラジアン）</param>
    /// <param name="endX">終了X座標</param>
    /// <param name="endY">終了Y座標</param>
    /// <returns>クロソイドオブジェクト、生成失敗時はnull</returns>
    public static Clothoid CreateClothoidFromPoseAndPoint(
        float startX,
        float startY,
        float startAngleRadians,
        float endX,
        float endY)
    {
        try
        {
            // Curvesライブラリの静的メソッドを呼び出し
            // float→doubleへの変換が必要（Curvesはdouble型を使用）
            Clothoid clothoid = Clothoid.FromPoseAndPoint(
                (double)startX,
                (double)startY,
                (double)startAngleRadians,
                (double)endX,
                (double)endY
            );

            return clothoid;
        }
        catch (System.Exception e)
        {
            // エラー発生時（到達不可能な点など）
            Debug.LogWarning($"クロソイド生成失敗: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 明示的なパラメータでクロソイドを作成
    ///
    /// 上級者向け：
    /// - 数学的パラメータを直接指定したい場合に使用
    /// - 通常はCreateClothoidFromPoseAndPointを使用してください
    /// </summary>
    /// <param name="startX">開始X位置</param>
    /// <param name="startY">開始Y位置</param>
    /// <param name="startDirection">開始方向（ラジアン）</param>
    /// <param name="startCurvature">開始曲率</param>
    /// <param name="clothoidParameter">クロソイドパラメータA</param>
    /// <param name="length">クロソイドの長さ</param>
    /// <returns>クロソイドオブジェクト</returns>
    public static Clothoid CreateClothoid(
        float startX,
        float startY,
        float startDirection,
        float startCurvature,
        float clothoidParameter,
        float length)
    {
        return new Clothoid(
            (double)startX,
            (double)startY,
            (double)startDirection,
            (double)startCurvature,
            (double)clothoidParameter,
            (double)length
        );
    }

    /// <summary>
    /// CurvesのPoint2D型をUnityのVector2型に変換
    ///
    /// 型変換の必要性：
    /// - Curvesライブラリ：Point2D（double型）
    /// - Unity：Vector2（float型）
    /// - 計算精度は少し下がりますが、Unityで扱いやすくなります
    /// </summary>
    public static Vector2 ToVector2(Point2D point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    /// <summary>
    /// UnityのVector2型をCurvesのPoint2D型に変換
    ///
    /// 逆方向の変換：
    /// - Unity→Curvesライブラリへデータを渡す際に使用
    /// </summary>
    public static Point2D ToPoint2D(Vector2 vector)
    {
        return new Point2D((double)vector.x, (double)vector.y);
    }

    /// <summary>
    /// CurvesのPose2D型をUnityの位置と向きに変換
    ///
    /// Pose（姿勢）とは：
    /// - 位置（どこにいるか）+ 向き（どちらを向いているか）の組み合わせ
    /// - ラジアンを度数法に変換して返します
    /// </summary>
    public static void ToUnityPose(Pose2D pose, out Vector2 position, out float directionDegrees)
    {
        position = new Vector2((float)pose.X, (float)pose.Y);
        directionDegrees = (float)pose.Direction * Mathf.Rad2Deg;
    }

    /// <summary>
    /// クロソイド曲線上の点をサンプリング（複数の点を取得）
    ///
    /// サンプリングとは：
    /// - 滑らかな曲線から、等間隔で点を取り出すこと
    /// - 取り出した点を繋げることで、曲線を折れ線で近似できます
    /// </summary>
    /// <param name="clothoid">サンプリングするクロソイド曲線</param>
    /// <param name="numPoints">取得する点の数</param>
    /// <returns>Unity Vector2形式の点の配列</returns>
    public static Vector2[] SamplePoints(Clothoid clothoid, int numPoints)
    {
        if (clothoid == null)
        {
            return new Vector2[0];
        }

        Vector2[] points = new Vector2[numPoints];
        for (int i = 0; i < numPoints; i++)
        {
            double t = (double)i / (numPoints - 1);
            Point2D p = clothoid.InterpolatePoint2D(t);
            points[i] = ToVector2(p);
        }
        return points;
    }

    /// <summary>
    /// クロソイド曲線上の特定位置での曲率を取得
    ///
    /// 曲率とは：
    /// - 曲がり具合を表す数値
    /// - 0に近いほど直線、大きいほど急カーブ
    /// - 正の値=右カーブ、負の値=左カーブ
    /// </summary>
    /// <param name="clothoid">クロソイド曲線</param>
    /// <param name="t">曲線上の位置（0～1、0=開始、1=終了）</param>
    /// <returns>曲率の値</returns>
    public static float GetCurvatureAt(Clothoid clothoid, float t)
    {
        if (clothoid == null) return 0f;
        return (float)clothoid.InterpolateCurvature((double)t);
    }

    /// <summary>
    /// クロソイド曲線上の特定位置での向き（進行方向）を度数法で取得
    ///
    /// 用途：
    /// - その位置で「どちらを向いているか」を知りたい場合
    /// - 0度=右、90度=上、180度=左、270度=下
    /// </summary>
    /// <param name="clothoid">クロソイド曲線</param>
    /// <param name="t">曲線上の位置（0～1）</param>
    /// <returns>向き（度数法）</returns>
    public static float GetDirectionDegreesAt(Clothoid clothoid, float t)
    {
        if (clothoid == null) return 0f;
        double dirRadians = clothoid.InterpolateDirection((double)t);
        return (float)dirRadians * Mathf.Rad2Deg;
    }

    /// <summary>
    /// クロソイド曲線上の特定位置での向きをラジアンで取得
    ///
    /// ラジアン版：
    /// - 数学計算で使う場合はこちらを使用
    /// </summary>
    public static float GetDirectionRadiansAt(Clothoid clothoid, float t)
    {
        if (clothoid == null) return 0f;
        return (float)clothoid.InterpolateDirection((double)t);
    }

    /// <summary>
    /// クロソイドが正常に生成されたか検証
    ///
    /// 検証内容：
    /// - nullでないか
    /// - パラメータAがNaN（非数）でないか
    /// - パラメータAが無限大でないか
    /// </summary>
    public static bool IsValidClothoid(Clothoid clothoid)
    {
        return clothoid != null && !double.IsNaN(clothoid.A) && !double.IsInfinity(clothoid.A);
    }

    /// <summary>
    /// クロソイドの近似的な長さを取得
    ///
    /// 計算方法：
    /// - 曲線を複数の点でサンプリング
    /// - 隣接する点間の直線距離を合計
    /// - 点が多いほど正確だが、計算量も増加
    /// </summary>
    public static float GetApproximateLength(Clothoid clothoid, int numSamples = 20)
    {
        if (clothoid == null) return 0f;

        float length = 0f;
        Vector2 prevPoint = ToVector2(clothoid.InterpolatePoint2D(0.0));

        for (int i = 1; i <= numSamples; i++)
        {
            double t = (double)i / numSamples;
            Vector2 currentPoint = ToVector2(clothoid.InterpolatePoint2D(t));
            length += Vector2.Distance(prevPoint, currentPoint);
            prevPoint = currentPoint;
        }

        return length;
    }

    /// <summary>
    /// 生成されたクロソイドがほぼ直線かチェック
    ///
    /// 用途：
    /// - 退化したケース（ほぼ真っすぐな曲線）を除外したい場合
    /// - 直線に近い軌跡は予測として意味がないため
    ///
    /// 判定方法：
    /// - 始点、中点、終点の曲率を確認
    /// - 全て閾値以下なら「ほぼ直線」と判定
    /// </summary>
    /// <param name="clothoid">チェックするクロソイド</param>
    /// <param name="curvatureThreshold">「直線」とみなす最大曲率（デフォルト0.01）</param>
    /// <returns>ほぼ直線ならtrue</returns>
    public static bool IsApproximatelyStraight(Clothoid clothoid, float curvatureThreshold = 0.01f)
    {
        if (clothoid == null) return false;

        // 始点、中点、終点の曲率をサンプリング
        float curvStart = GetCurvatureAt(clothoid, 0f);
        float curvMid = GetCurvatureAt(clothoid, 0.5f);
        float curvEnd = GetCurvatureAt(clothoid, 1f);

        return Mathf.Abs(curvStart) < curvatureThreshold &&
               Mathf.Abs(curvMid) < curvatureThreshold &&
               Mathf.Abs(curvEnd) < curvatureThreshold;
    }
}
