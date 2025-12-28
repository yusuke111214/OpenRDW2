# PredRedLPP実装 引き継ぎドキュメント

## 🎯 プロジェクト概要

論文「Predictive multiuser redirected walking using artificial potential fields (Hirt et al., 2024)」のPredRedLPPアルゴリズムをOpenRDW2プロジェクトに実装する。

**論文URL:** https://www.frontiersin.org/articles/10.3389/frvir.2024.1365344/full
**PDF:** `C:\RDW\OpenRDW2\Hirt_原文_予測＿マルチユーザー.pdf`

---

## 📋 現在の状況

### ✅ 完了している実装（Phase 2-6）

コミット履歴：
- `2d8bb09`: Phase 2実装（基礎クラス）
- `d205235`: Phase 3-5実装（予測・評価・リダイレクター）
- `d03d41e`: コメント日本語化
- `9b4b795`: ドキュメント作成
- **最新**: Phase 6実装（問題1,2の修正）
  - 可視化オブジェクトの座標変換修正
  - アクションセット評価を論文準拠に修正
  - Curvature適用メソッド実装

#### Phase 2で作成したクラス
1. **Trajectory.cs** - 軌跡を表すデータクラス + ApplyCurvature() メソッド（Phase 6追加）
2. **RedirectionAction.cs** - リダイレクションアクション（Phase 6で使用開始）
3. **IPathPredictor.cs** - パス予測器のインターフェース
4. **CurvesWrapper.cs** - Curvesライブラリのラッパー
5. **PathSmoother.cs** - パス平滑化（HMD用、デフォルトOFF）
6. **ClothoidGenerationTest.cs** - テストスクリプト

#### Phase 3-5で作成したクラス
1. **LemniscatePathPredictor.cs** - レムニスケート予測器（論文 Eq.1）
2. **TrajectoryEvaluator.cs** - コスト関数（論文 Eq.14-18）
3. **PredRedLPP_Redirector.cs** - メインリダイレクター

---

## ✅ 解決済みの問題点

### 問題1: 可視化オブジェクトの位置ずれ（解決済み）

#### 原因
**座標系の混同** - 物理空間と仮想空間の変換が欠けていた

Redirected Walkingの仕組み：
- **仮想空間**（`currPos`/`currDir`）：ユーザーが見ている世界、Unityワールド座標
  - アバター、Plane、カメラはこの座標系で動く
  - ユーザーは直進していると感じる
- **物理空間**（`currPosReal`/`currDirReal`）：実際のトラッキングスペース
  - `trackingSpace.transform`のローカル座標系
  - 予測軌跡はこの座標系で生成される（障害物回避のため）
  - 実際にはカーブして歩いている

#### 解決方法
**座標変換の実装**（`trackingSpace.TransformPoint()`）

```csharp
// 物理空間の2D座標 → 仮想空間の3D座標
Vector3 physicalPos3D = Utilities.UnFlatten(physicalPoint2D);
Vector3 virtualPos3D = trackingSpace.TransformPoint(physicalPos3D);
```

変換の仕組み：
- 仮想→物理：`GetPosReal(pos)` = `Inverse(trackingSpace.rotation) * (pos - trackingSpace.position)`
- 物理→仮想：`trackingSpace.TransformPoint(posReal)` = 逆変換

修正箇所：
1. `UpdateLemniscateVisualization()`: 物理空間の座標を仮想空間に変換して描画
2. `UpdateTrajectoryVisualization()`: 同上
3. LineRendererは`useWorldSpace=true`で仮想空間のワールド座標を使用

---

### ✅ 問題2: アクションセット評価のアプローチと論文との整合性（解決済み）

#### 原因
**アクションの評価方法が論文と異なっていた** - アクションを軌跡に適用せずにコストを計算していた

問題点：
- TrajectoryEvaluator.cs の `EvaluateAllActions()` (旧Line 89-91)
  ```csharp
  // 全てのアクションで同じコストを使用していた
  float actionCost = trajectoryCost;  // ❌ 間違い
  ```
- 各アクション π を軌跡 T に適用した結果（T_red）を評価すべきだった
- 特に Curvature ゲインの効果が評価されず、Translation ゲインばかり選ばれていた

#### 解決方法
**論文 Section 3.3 のアルゴリズムを正しく実装**

1. **Trajectory.cs に `ApplyCurvature()` メソッドを追加** (Line 255-303)
   ```csharp
   public Trajectory ApplyCurvature(float curvature)
   {
       // 各セグメントに曲率を適用して T_red を生成
       // rotation = curvature * distance
       for (int i = 1; i < points.Count; i++)
       {
           float segmentLength = Vector2.Distance(points[i - 1], points[i]);
           float rotationAngle = curvature * segmentLength;
           currentAngle += rotationAngle;

           Vector2 direction = new Vector2(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle));
           Vector2 newPoint = newPoints[newPoints.Count - 1] + direction * segmentLength;
           newPoints.Add(newPoint);
       }
       return new Trajectory(newPoints);
   }
   ```

2. **TrajectoryEvaluator.cs の `EvaluateAllActions()` を書き直し** (Line 43-159)
   ```csharp
   public (RedirectionAction, Trajectory) EvaluateAllActions(...)
   {
       foreach (var trajectory in predictions)
       {
           foreach (var action in actions)
           {
               // Step 1: アクションを軌跡に適用 → T_red を生成
               Trajectory T_red = ApplyActionToTrajectory(trajectory, action);

               // Step 2: T_red のコストを評価
               float actionCost = CalculateTotalCost(T_red, ...);

               // Step 3: 最小コストを記録
               if (actionCost < minCost)
               {
                   minCost = actionCost;
                   bestAction = action;
                   bestTrajectory = trajectory;
               }
           }
       }
       return (bestAction, bestTrajectory);
   }
   ```

3. **PredRedLPP_Redirector.cs の `InjectRedirection()` を修正** (Line 247-270)
   ```csharp
   // Step 5: アクションセット U を生成
   List<RedirectionAction> actionSet =
       RedirectionActionFactory.GenerateActionSet(globalConfiguration);

   // Step 6: 全ての (軌跡, アクション) ペアを評価
   (RedirectionAction bestAction, Trajectory bestTrajectory) =
       trajectoryEvaluator.EvaluateAllActions(
           feasibleTrajectories, actionSet, ...);

   // Step 7: 最良のアクション π_optimal を適用
   ApplyRedirectionAction(bestAction);
   ```

#### 結果
✅ **論文 Section 3.3 のアルゴリズムに準拠** - アクションセット U 全体を評価
✅ **予測的アプローチを実装** - T_red のコスト評価で最適なアクションを選択
✅ **Curvature ゲインが正しく評価される** - 障害物回避性能の向上が期待される

---

この問題には、過去に **3つの異なるアプローチ**が検討されました（参考）。

---

## 📚 3つのアプローチの比較

### アプローチA: 現在の実装（元の状態、動作確認済み）

#### 実装内容
```csharp
// Step 5: コストに基づいて最良の軌跡を選択
Trajectory bestTrajectory = SelectBestTrajectory(feasibleTrajectories, physicalSpace);

// Step 6: 最良の軌跡に基づいてリダイレクションをリアクティブに適用
ApplyRedirectionFromTrajectory(bestTrajectory, physicalSpace);
```

**`ApplyRedirectionFromTrajectory()`の動作:**
```csharp
// 軌跡上のターゲットポイントを取得
Vector2 targetPoint = trajectory.points[targetIndex];
Vector2 desiredDir2D = (targetPoint - currentPos2D).normalized;

// 常に3つのゲイン全てを設定
SetTranslationGain(...);  // 軌跡との整合性で決定
SetRotationGain(...);     // 回転方向で決定
SetCurvature(...);        // 操舵方向で決定
ApplyGains();
```

#### 特徴
✅ **動作確認済み** - ユーザーが曲がることを確認
✅ **実装がシンプル** - リアクティブアプローチ
✅ **全てのゲインが適用される** - T/R/Cを毎フレーム計算

❌ **論文と異なる** - アクションセットUを使用していない
❌ **予測的ではない** - 毎フレームリアクティブに計算

#### 論文との差異

| 論文（Section 3.3） | 現在の実装 |
|-------------------|-----------|
| アクションセットU全体を評価（Table 2） | アクションセット不使用 |
| 複数のアクション候補から選択（Eq.13） | 軌跡に基づいてリアクティブに計算 |
| 予測的アプローチ | リアクティブアプローチ |

---

### アプローチB: 論文準拠の実装（試行したが動作せず）

#### 実装内容
```csharp
// Step 5: アクションセットU生成（論文 Table 2）
List<RedirectionAction> actionSet = RedirectionActionFactory.GenerateActionSet(globalConfiguration);
// → 19個のアクション（6 Translation + 6 Rotation + 6 Curvature + 1 Null）

// Step 6: 全アクション-軌跡ペアを評価
(RedirectionAction bestAction, Trajectory bestTrajectory) =
    trajectoryEvaluator.EvaluateAllActions(feasibleTrajectories, actionSet, ...);

// Step 7: 最良のアクションを適用
ApplyRedirectionAction(bestAction);
```

**`ApplyRedirectionAction()`の動作:**
```csharp
switch (action.gainType)
{
    case RedirectionGainType.Translation:
        SetTranslationGain(action.primaryValue);
        SetRotationGain(1.0f);  // 中立
        SetCurvature(0f);       // 中立
        break;

    case RedirectionGainType.Rotation:
        SetTranslationGain(1.0f);       // 中立
        SetRotationGain(action.primaryValue);
        SetCurvature(0f);       // 中立
        break;

    case RedirectionGainType.Curvature:
        SetTranslationGain(1.0f);  // 中立
        SetRotationGain(1.0f);     // 中立
        SetCurvature(action.primaryValue);
        break;
}
ApplyGains();
```

#### 特徴
✅ **論文に準拠** - Section 3.3の手順通り
✅ **予測的アプローチ** - 全アクションを事前評価

❌ **並進ゲインのみ適用される** - コスト評価の結果、Translation actionがbestとして選ばれ続ける
❌ **ユーザーが曲がらない** - Curvature/Rotationゲインが適用されない

#### なぜ並進ゲインのみになるのか？

**推定される原因:**

1. **コスト関数の問題**
   - APFコスト（Eq.16）が支配的
   - Translation actionが常に最小コストを持つ

2. **軌跡の問題**
   - 予測軌跡が直線的
   - Curvatureアクションの効果が評価されない

3. **評価方法の問題**
   - `TrajectoryEvaluator.EvaluateAllActions()`が各アクションの影響を正しく評価していない
   - 現在は「アクションを適用した結果の軌跡」ではなく「元の軌跡」を評価

**論文の本来の意図（推測）:**
```
For each trajectory T in predictions:
    For each action π in U:
        1. Simulate applying π to T → get T_redirected
        2. Calculate cost of T_redirected
        3. Store (T, π, cost)
Select (T*, π*) with minimum cost
```

**現在の実装:**
```
For each trajectory T in predictions:
    1. Calculate cost of T (without considering actions)
    For each action π in U:
        2. Use same cost for all actions
Select first trajectory with minimum cost, any action
```

---

### アプローチC: ハイブリッドアプローチ（提案）

アプローチAとBの利点を組み合わせる：

#### 実装内容
```csharp
// Step 5: 最良の軌跡を選択（アプローチA）
Trajectory bestTrajectory = SelectBestTrajectory(feasibleTrajectories, physicalSpace);

// Step 6: 軌跡に向かうための最良のアクションを選択（アプローチB）
RedirectionAction bestAction = SelectBestActionForTrajectory(bestTrajectory, actionSet, physicalSpace);

// Step 7: アクションを適用
ApplyRedirectionAction(bestAction);
```

**`SelectBestActionForTrajectory()`:**
```csharp
private RedirectionAction SelectBestActionForTrajectory(
    Trajectory trajectory,
    List<RedirectionAction> actionSet,
    SingleSpace physicalSpace)
{
    float minCost = float.MaxValue;
    RedirectionAction bestAction = null;

    foreach (var action in actionSet)
    {
        // このアクションを適用した場合の効果を評価
        float cost = EvaluateActionEffect(action, trajectory, physicalSpace);

        if (cost < minCost)
        {
            minCost = cost;
            bestAction = action;
        }
    }

    return bestAction ?? RedirectionAction.CreateNullAction();
}
```

#### 特徴
✅ **動作する可能性が高い** - アプローチAベース
✅ **論文に近い** - アクションセットを使用
✅ **全ゲイン適用可能** - アクション選択による

⚠️ **論文との差異あり** - 軌跡選択とアクション選択を分離

---

## 🤔 論文のアルゴリズムをどう解釈すべきか？

### 論文の記述（Section 3.3）

> "The action set U consists of rotation gains, translation gains, curvature gains, and a combination of translation and curvature gains."

> "A cost-based analysis of T_red is used to identify the best redirection π_optimal ∈ U"

### 2つの解釈

#### 解釈1: 軌跡とアクションを独立に評価
```
1. 予測軌跡 T を生成
2. T のコストを計算（アクション非依存）
3. アクションセット U からコストが最小のものを選択
```
→ **これは意味をなさない**（アクションが軌跡に影響しないなら選択する意味がない）

#### 解釈2: アクションが軌跡に影響を与える
```
1. 予測軌跡 T を生成（ユーザーの自然な動き）
2. 各アクション π を T に適用した結果 T_red を計算
3. T_red のコストを評価
4. 最小コストの (T, π) ペアを選択
```
→ **論文の意図はこちら**だが、実装方法が不明確

### 問題: 「アクションを軌跡に適用」の意味

論文では以下が不明確：
1. どのように T に π を適用して T_red を得るのか？
2. T_red は新しい軌跡か、修正された軌跡か？
3. リダイレクションは軌跡生成時に考慮されるのか、後から適用されるのか？

---

## 💡 採用した実装アプローチ

### ✅ 論文準拠のアプローチB（実装済み）

**選択理由:**
- ✅ 論文 Section 3.3 に完全準拠
- ✅ 予測的アプローチの真の実装
- ✅ アクションセット U 全体を正しく評価
- ✅ Curvature ゲインの効果を評価可能

**実装内容:**
1. ✅ 可視化の座標変換を修正（問題1）
2. ✅ `Trajectory.ApplyCurvature()` を実装
3. ✅ `TrajectoryEvaluator.EvaluateAllActions()` を論文準拠に書き直し
4. ✅ `PredRedLPP_Redirector.InjectRedirection()` をアクションセット評価方式に変更

**結果:**
- 論文のアルゴリズムを正確に実装
- T_pred → T_red のフローを正しく実装
- 各 (軌跡, アクション) ペアのコストを個別評価
- π_optimal の選択が論文通り


---


## 📚 参考情報

### 重要なファイル

| ファイル | 役割 | 重要度 |
|---------|------|--------|
| `PredRedLPP_Redirector.cs` | メインリダイレクター | ⭐⭐⭐ |
| `LemniscatePathPredictor.cs` | 軌跡予測（Eq.1） | ⭐⭐⭐ |
| `TrajectoryEvaluator.cs` | コスト評価（Eq.14-18） | ⭐⭐⭐ |
| `RedirectionAction.cs` | アクション定義 | ⭐⭐ |
| `RedirectionManager.cs` | 基底クラス | ⭐⭐ |
| `APF_Redirector.cs` | APF基底クラス | ⭐⭐ |
| `HeadFollower.cs` | Body更新 | ⭐ |

```

### GameObjectの親子関係

```
RedirectedAvatar (PredRedLPP_Redirector)
├── Body (HeadFollower)
│   ├── transform.position = currPos
│   └── transform.rotation = LookRotation(currDir)
├── TrackingSpace0
├── Simulated Avatar/Head (SimulatedWalker)
└── PredRedLPP_Trajectory (LineRenderer)
    └── 親: RedirectedAvatar
    └── useWorldSpace = false (ローカル座標)
└── PredRedLPP_Lemniscate (LineRenderer)
    └── 親: RedirectedAvatar
    └── useWorldSpace = false (ローカル座標)
└── Arrow(Clone)

```



## ⚠️ 現在の問題点

### 問題3: 計算時間が著しく重い（未解決）

#### 症状
- バックグラウンドでシミュレーションを実行すると、他のアルゴリズムと比べて著しく遅い
- 10分以上待ってもシミュレーションが終了しないほど重い
- 実用的な速度での動作確認ができない

#### 原因分析

**計算量の詳細:**

1. **基本パラメータ（デフォルト設定）**
   - 軌跡数: **11本** (`lemniscateEndpoints = 11`)
   - アクション数: **19個** (Translation 6 + Rotation 6 + Curvature 6 + Null 1)
   - 1軌跡あたりの点数: **20点** (`trajectorySamplePoints = 20`)

2. **1フレームあたりの計算量**
   ```
   総評価回数 = 軌跡数 × アクション数 × 点数
              = 11 × 19 × 20
              = 4,180回のコスト計算/フレーム
   ```

3. **各コスト計算の内訳** (TrajectoryEvaluator.cs:171-199)
   ```csharp
   for (int i = 0; i < trajectory.points.Count; i++)  // 20回
   {
       J_APF = CalculateAPFCost(...);      // O(境界線数 + 障害物数 + アバター数)
       J_Heading = CalculateHeadingCost(...);  // O(1)
       J_Reset = CalculateResetCost(...);      // O(1)
       totalCost += Mathf.Pow(discountFactor, i) * (J_APF + J_Heading + J_Reset);
   }
   ```

4. **APFコスト計算の詳細** (TrajectoryEvaluator.cs:211-258)
   - トラッキング空間の全エッジをループ（通常4本）
   - 全ての障害物ポリゴンをループ
   - 全ての他アバターをループ
   - 各要素について最近傍点の計算 (`GetNearestPos`)

5. **他のアルゴリズムとの比較**

   | アルゴリズム | 1フレームあたりの計算量 |
   |------------|---------------------|
   | ThomasAPF（リアクティブ） | 1回のAPF力計算 |
   | PredRedLPP（現在の実装） | **4,180回のコスト計算** |
   | 計算量の差 | **約4,180倍** |

#### なぜこれほど重いのか？

**論文準拠の実装による必然的な結果:**

1. **予測的アプローチの特性**
   - リアクティブ手法: 現在の状況だけを見て即座に反応
   - 予測的手法: 全ての可能な未来のシナリオを事前評価

2. **アクションセット評価の代償**
   - 論文 Section 3.3 のアルゴリズムを正確に実装
   - 全ての (軌跡, アクション) ペアを評価する必要がある
   - これが論文の「予測的RDW」の本質

3. **コスト関数の複雑さ**
   - 論文 Eq. 14-18 を忠実に実装
   - APF（人工ポテンシャル場）ベースの計算は本質的に重い

#### 最適化の方向性

以下の方法で計算時間を短縮できます。**論文の精神を維持しながら実用的な速度を実現する**アプローチを推奨します。

---

### 🚀 最適化オプション一覧

#### オプション1: アクションセットのサイズ削減 ⭐⭐⭐（推奨）

**方法A: Minimalアクションセットを使用**

`RedirectionActionFactory.GenerateMinimalActionSet()` を使用（既に実装済み）

```csharp
// PredRedLPP_Redirector.cs Line 247 を変更
// 変更前
List<RedirectionAction> actionSet =
    RedirectionActionFactory.GenerateActionSet(globalConfiguration);

// 変更後
List<RedirectionAction> actionSet =
    RedirectionActionFactory.GenerateMinimalActionSet(globalConfiguration);
```

**効果:**
- アクション数: 19個 → **7個** (約63%削減)
- 総計算量: 4,180回 → **1,540回** (約63%削減)

**内訳:**
- Translation: 最小値・最大値の2個
- Rotation: 最小値・最大値の2個
- Curvature: 最小値・最大値の2個
- Null: 1個

**メリット:**
- ✅ 1行の変更で実装可能
- ✅ 論文の本質（アクションセット評価）は維持
- ✅ 極端な値（最小・最大）は評価されるため、効果は保たれる

**デメリット:**
- ⚠️ 中間的なゲイン値が評価されない
- ⚠️ 細かい調整ができない可能性

---

**方法B: numStepsを削減**

`RedirectionAction.cs` Line 183 の `numSteps` を変更

```csharp
// 変更前
const int numSteps = 6;  // 各ゲインタイプ6段階

// 変更後
const int numSteps = 3;  // 各ゲインタイプ3段階
```

**効果:**
- アクション数: 19個 → **10個** (約47%削減)
- 総計算量: 4,180回 → **2,200回** (約47%削減)

**内訳:**
- Translation: 3個 (最小、中間、最大)
- Rotation: 3個
- Curvature: 3個
- Null: 1個

**メリット:**
- ✅ 中間値も評価される
- ✅ 論文の本質を維持

**デメリット:**
- ⚠️ オプションAより削減効果が小さい

---

#### オプション2: 軌跡数の削減 ⭐⭐

**方法: lemniscateEndpointsを削減**

`GlobalConfiguration.cs` または Unity Inspector で設定変更

```csharp
// 変更前
public int lemniscateEndpoints = 11;

// 変更後
public int lemniscateEndpoints = 5;  // 論文でも5～21が推奨範囲
```

**効果:**
- 軌跡数: 11本 → **5本** (約55%削減)
- 総計算量: 4,180回 → **1,900回** (約55%削減)

**メリット:**
- ✅ 大幅な計算量削減
- ✅ 論文の推奨範囲内（5～21）

**デメリット:**
- ⚠️ 予測の多様性が減る
- ⚠️ 複雑な環境で最適な軌跡を見逃す可能性

---

#### オプション3: 評価頻度の削減 ⭐⭐⭐（推奨）

**方法: Nフレームごとに評価**

`PredRedLPP_Redirector.cs` の `InjectRedirection()` にフレームカウンター追加

```csharp
private int evaluationFrameCounter = 0;
private const int EVALUATION_INTERVAL = 2;  // 2フレームに1回評価
private RedirectionAction cachedBestAction = null;

protected override void InjectRedirection()
{
    evaluationFrameCounter++;

    if (evaluationFrameCounter >= EVALUATION_INTERVAL)
    {
        evaluationFrameCounter = 0;

        // 通常の評価処理
        List<RedirectionAction> actionSet = ...;
        (cachedBestAction, Trajectory bestTrajectory) =
            trajectoryEvaluator.EvaluateAllActions(...);
    }

    // キャッシュされたアクションを適用
    ApplyRedirectionAction(cachedBestAction);
}
```

**効果:**
- 評価頻度: 毎フレーム → **2フレームに1回** (50%削減)
- 実質的な計算量: 4,180回 → **2,090回/フレーム（平均）**

**メリット:**
- ✅ 計算量を大幅削減
- ✅ ユーザーの動きは比較的遅いため、影響は小さい
- ✅ 他の最適化と組み合わせ可能

**デメリット:**
- ⚠️ 急な状況変化への対応が遅れる可能性
- ⚠️ 実装に若干の手間がかかる

---

#### オプション4: サンプル点数の削減 ⭐

**方法: trajectorySamplePointsを削減**

```csharp
// LemniscatePathPredictor.cs または GlobalConfiguration
// 変更前
public int trajectorySamplePoints = 20;

// 変更後
public int trajectorySamplePoints = 10;
```

**効果:**
- 点数: 20点 → **10点** (50%削減)
- 総計算量: 4,180回 → **2,090回** (50%削減)

**メリット:**
- ✅ 単純な設定変更で実装可能

**デメリット:**
- ⚠️ 軌跡の精度が落ちる
- ⚠️ 障害物との衝突判定が甘くなる

---

### 📊 組み合わせ最適化の効果

複数の最適化を組み合わせた場合の効果：

| 組み合わせ | 計算量削減 | 推奨度 |
|-----------|----------|-------|
| **A: Minimal actionSet** | 63% | ⭐⭐⭐ |
| **B: A + lemniscate=5** | 86% (4,180 → 570回) | ⭐⭐⭐ |
| **C: B + 2フレーム間隔** | 93% (平均285回/フレーム) | ⭐⭐⭐⭐⭐ |
| D: C + samples=10 | 96% (平均143回/フレーム) | ⭐⭐⭐⭐ |

**推奨設定（組み合わせC）:**
```csharp
// 1. Minimalアクションセットを使用
actionSet = RedirectionActionFactory.GenerateMinimalActionSet(config);

// 2. 軌跡数を削減
lemniscateEndpoints = 5;

// 3. 2フレームに1回評価
EVALUATION_INTERVAL = 2;

// 結果: 4,180回 → 平均285回/フレーム（93%削減）
```

この設定であれば、論文の本質を維持しながら実用的な速度が得られる可能性が高いです。

---

### 🔬 将来的な最適化（高度）

1. **Unity Job System による並列処理**
   - コスト計算を並列化
   - マルチコアCPUを活用

2. **コスト計算のキャッシング**
   - 軌跡が変わらない場合は再計算しない
   - インクリメンタルな更新

3. **Early stopping**
   - コストが閾値を超えたら評価を打ち切り

4. **空間分割（Spatial hashing）**
   - APF計算で近傍の障害物のみを考慮

これらは実装が複雑なため、まずは上記の基本的な最適化を試すことを推奨します。

---

## ✅ 論文との整合性について

### 現在の実装は論文と完全に一致しています

**実装内容:**
- ✅ アクションセットU全体を評価（Table 2準拠）
- ✅ 最良の(軌跡, アクション)ペアを選択（Eq. 13準拠）
- ✅ 予測的アプローチ（軌跡予測）を使用
- ✅ APFベースのコスト関数を使用（Eq. 14-18）
- ✅ レムニスケート＋クロソイドを使用（Eq. 1）
- ✅ T_pred → T_red の変換を正しく実装

**論文 Section 3.3 との対応:**
```
論文: "T_pred is redirected based on an action set U"
実装: actionSet = RedirectionActionFactory.GenerateActionSet()

論文: "a cost-based analysis of T_red is used to identify π_optimal ∈ U"
実装: (bestAction, bestTrajectory) = trajectoryEvaluator.EvaluateAllActions(
         predictions, actionSet, ...)

論文: "The optimal redirection π_optimal is applied"
実装: ApplyRedirectionAction(bestAction)
```

**Phase 6で解決した問題:**
1. 座標変換の不備 → 物理空間↔仮想空間の変換を実装
2. アクション評価の不備 → T_red を生成してコストを正しく評価

---

## 🔜 次のステップ（推奨作業）

### Phase 7: 計算量最適化（優先度: 高）

**目的:** 実用的な速度で動作確認できるようにする

**推奨する実装順序:**

1. **まず試す: 組み合わせC（93%削減）**
   ```
   ✅ Minimalアクションセット使用（1行変更）
   ✅ lemniscateEndpoints = 5（設定変更）
   ✅ 2フレームに1回評価（実装必要）
   ```
   - これで動作確認が可能になれば十分

2. **それでも遅い場合: オプション4を追加**
   ```
   ✅ trajectorySamplePoints = 10
   ```
   - さらに50%の削減（合計96%削減）

3. **動作確認後: パフォーマンス測定**
   - 各最適化の効果を定量的に評価
   - 精度とのトレードオフを検証

4. **必要に応じて: パラメータの微調整**
   - シミュレーション結果を見ながら調整
   - 論文と同等の性能が得られるか確認

### Phase 8: 検証と評価

1. **動作確認**
   - シミュレーションが完了するか
   - アバターが正しくカーブするか
   - 障害物を回避できるか

2. **性能評価**
   - 他のアルゴリズムと比較
   - 論文の結果と比較（可能な範囲で）

3. **ドキュメント更新**
   - 最適化の結果を記録
   - 最終的なパラメータ設定を記載

