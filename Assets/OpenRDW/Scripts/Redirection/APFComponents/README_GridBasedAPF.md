# Grid-based APF Implementation for OpenRDW2

## 概要

このディレクトリには、グリッドベースのAPF（Artificial Potential Field）システムが含まれています。従来のレイキャストベースの実装に対して、以下の改善を提供します：

### 従来の問題点（レイキャストベース）

- **局所的な視野**: ユーザー周囲の限られた方向（16方向）しか検知できない
- **大域的構造の欠如**: 物理空間全体の構造を把握できない
- **計算コストの線形増加**: 方向数に比例して計算コストが増加

### グリッドベースの利点

- **大域的視野**: 物理空間全体を一度に把握
- **効率的な開放空間検出**: Distance Transformによる高速計算
- **可視化が容易**: グリッド全体のポテンシャル場を視覚的に確認可能
- **研究実績のある手法**: Hirt et al. (2022), Dong et al. (2020) などの先行研究に基づく

---

## 実装の基礎となる研究

1. **Hirt et al. (2022)**: "Grid-based APF for redirected walking"
   - グリッド分割による効率的なAPF計算
   - Distance Transformを用いた障害物からの距離計算

2. **Dong et al. (2020)**: "Dynamic Artificial Potential Fields for Multi-User Redirected Walking"
   - ステアリングターゲット（開放空間の目標点）を動的に選択
   - 開放空間のサイズと境界までの距離を基準に評価

3. **Thomas & Rosenberg (2019)**: "A general reactive algorithm for redirected walking using artificial potential functions"
   - ベースとなるAPF実装（斥力のみ）

---

## ファイル構成

### コアコンポーネント

1. **APFGrid.cs**
   - グリッドベースのAPFシステムの中核
   - Distance FieldとPotential Fieldの計算
   - 有限差分法による勾配（力の方向）の計算

2. **OpenSpaceFinder.cs**
   - グリッドベースの開放空間検出
   - Dongらの評価基準に基づくステアリングターゲット選択
   - 局所検索と大域検索の2つのモード

3. **S2G_WithAttraction_Redirector.cs** (Spatial-to-Grid)
   - ThomasAPF_Redirectorを継承
   - グリッドベースAPFと引力を統合
   - デバッグ用可視化機能を内蔵

### 既存コンポーネント（再利用）

4. **AttractivePotentialField.cs**
   - 引力ポテンシャル場の計算（レイキャスト版と共通）
   - コニック型ポテンシャル（飽和距離あり）

---

## 使用方法

### 1. 基本セットアップ

UnityエディタでGlobalConfigurationオブジェクトを選択し、以下のセクションを設定：

#### Grid-based APF Parameters
```
Grid Cell Size: 0.2m（推奨初期値）
  - セルサイズ。小さいほど精密だが計算コストが増加
  - 0.2m = 10m x 10m空間で 50x50 = 2500セル
  - 0.1m = 10m x 10m空間で 100x100 = 10000セル

Grid Min Open Distance: 1.0m
  - 開放空間とみなす最小距離
  - 障害物から1m以上離れた場所を「開放空間」と判定

Use Local Search: ✓ ON（推奨）
  - ユーザー周辺のみを検索（高速）
  - OFFにすると空間全体を検索（遅いが正確）

Local Search Radius: 8.0m
  - 局所検索の範囲
  - ユーザーから半径8m以内で最適な開放空間を探す
```

#### Attractive Potential Field Parameters（既存のパラメータ）
```
Enable Attractive Potential Field: ✓ ON
Attraction Strength: 1.0
Attractive Weight: 0.3
Repulsive Weight: 0.7
Attraction Saturation Distance: 3.0m
Attraction Target Update Interval: 1.0秒
```

#### Grid-based Potential Field Visualization（デバッグ・研究用）
```
Enable Grid Potential Visualization: ✓ ON
  - グリッドベースのポテンシャル場可視化を有効化
  - ゲーム実行時に床にヒートマップと矢印を表示

Show Grid Heatmap: ✓ ON
  - ヒートマップメッシュを表示
  - ポテンシャル値または距離値で色分け
  - 青（安全）→ 緑 → 黄 → 赤（危険）のグラデーション

Show Grid Arrows: ✓ ON
  - 矢印フィールドを表示
  - 各グリッドセルの勾配方向（誘導方向）を表示

Grid Color Gamma: 0.5
  - ガンマ補正値（0.1-2.0）
  - 低い値ほど床の色の違いが強調される
  - 推奨: 0.3-0.7（低ポテンシャル領域の可視化に有効）

Grid Arrow Spacing: 2
  - 矢印の間隔（グリッドセル数）
  - 2 = 2セルおきに矢印表示（推奨）
  - 1 = 全セルに矢印（重い）

Realtime Grid Update: OFF
  - リアルタイム更新（パフォーマンス負荷大）
  - 通常はOFF、デバッグ時のみON
```

**ヒートマップの3つのモード**（コードで切り替え可能）：
1. **Potential Mode**: ポテンシャル値で色分け（高い = 赤 = 危険）
2. **Distance Mode**: 距離値で色分け（遠い = 青 = 安全）
3. **Open Space Mode**: 開放空間を緑で強調表示

### 2. リダイレクタの選択

Experiment Setupまたはコマンドファイルで、リダイレクタとして**S2G_WithAttraction**を指定します。

### 3. パラメータ調整ガイド

#### 推奨初期パラメータ

| パラメータ | 推奨値 | 説明 |
|-----------|--------|------|
| Grid Cell Size | 0.2m | セルサイズ。0.1-0.3mの範囲で調整 |
| Grid Min Open Distance | 1.0m | 開放空間の閾値。広い空間なら1.5m |
| Use Local Search | ON | ほとんどの場合ONで十分 |
| Local Search Radius | 8.0m | 小さい空間なら5m、大きい空間なら10m |
| Attraction Strength | 1.0 | 引力の強さ |
| Attractive Weight | 0.3 | 引力の重み（0-1） |
| Repulsive Weight | 0.7 | 斥力の重み（0-1） |
| Target Update Interval | 1.0秒 | ターゲット更新頻度 |

#### パフォーマンスとメモリ使用量

| セルサイズ | 10m x 10m空間 | セル数 | メモリ | 計算時間（推定） |
|-----------|--------------|-------|--------|----------------|
| 0.3m | 34 x 34 | 1,156 | ~9KB | ~0.005秒 |
| 0.2m | 50 x 50 | 2,500 | ~20KB | ~0.01秒 |
| 0.1m | 100 x 100 | 10,000 | ~78KB | ~0.04秒 |

**推奨**: 0.2mで開始し、パフォーマンスに余裕があれば0.1mに変更

#### トラブルシューティング

**問題: グリッド計算が遅い**
- Cell Sizeを0.3mに増やす
- Use Local Searchを ON にする
- Local Search Radiusを小さくする（5-6m）

**問題: 開放空間が見つからない**
- Grid Min Open Distanceを小さくする（0.7-0.8m）
- Use Local Searchを OFF にして全体検索

**問題: ユーザーが壁に向かって誘導される**
- Repulsive Weightを増やす（0.8-0.9）
- Attractive Weightを減らす（0.1-0.2）

**問題: 引力が弱すぎる**
- Attraction Strengthを1.5-2.0に増やす
- Attractive Weightを0.4に増やす

---

## 動作原理

### 1. グリッド分割

```
物理空間（例: 10m x 10m）
       ↓
グリッド分割（セルサイズ 0.2m）
       ↓
50 x 50 = 2500セル
```

### 2. Distance Field計算

各セルについて、最も近い障害物までの距離を計算：

```
セルA: 距離 0.5m → 壁に近い（危険）
セルB: 距離 2.0m → 壁から遠い（安全）
セルC: 距離 3.5m → とても安全（開放空間の中心）
```

計算方法：
- トラッキング空間の境界（壁）
- 障害物のポリゴン（柱、家具など）
- 他のユーザー（半径0.5mの円）

これらすべてについて、各セルから最短距離を計算。

### 3. Potential Field計算

Distance Fieldから、Non-harmonic APF公式でポテンシャル値を計算：

```
U(d) = F_max / (1 + k * d)

d = 0.5m → U = 100 / (1 + 2*0.5) = 50  （高い＝危険）
d = 2.0m → U = 100 / (1 + 2*2.0) = 20  （低い＝安全）
d = 3.5m → U = 100 / (1 + 2*3.5) = 12.5（とても安全）
```

パラメータ：
- F_max = 100.0（斥力の最大強度）
- k = 2.0（減衰率）

### 4. 開放空間の検出

全セルをスキャンして、Distance値が大きいセルを探す：

```
全セルをループ
  → Distance >= 1.0mのセルを「開放空間候補」とする
  → スコア計算: score = distance * weight
  → ユーザーから近い場所にボーナス
  → 最高スコアの位置を「ステアリングターゲット」とする
```

### 5. 力の計算と合成

```
斥力 = グリッドの勾配（∇U）の逆方向
     = 有限差分法で近似

引力 = ステアリングターゲットへの方向 * 強度
     = コニック型ポテンシャル（飽和距離あり）

合力 = 斥力 * 0.7 + 引力 * 0.3
```

### 6. リダイレクションの適用

```
合力 → 目標方向を計算
     → RotationGainまたはCurvatureで実現
```

---

## レイキャストベースとの比較

| 項目 | レイキャストベース | グリッドベース |
|------|-------------------|---------------|
| **視野** | 局所的（16方向） | 大域的（全空間） |
| **計算回数** | 毎フレーム | 1秒ごと |
| **メモリ使用** | 少ない（~1KB） | 中程度（~20KB） |
| **開放空間検出** | 近似的 | 正確 |
| **可視化** | 困難 | 容易 |
| **パフォーマンス** | 軽量 | 中程度 |

### 使い分けの目安

**レイキャストベース（ThomasAPF_WithAttraction）を使う場合**:
- パフォーマンスが最重要
- シンプルな空間形状
- リアルタイム性が必要

**グリッドベース（S2G_WithAttraction）を使う場合**:
- 複雑な空間形状
- 開放空間を正確に検出したい
- デバッグしやすい実装が必要
- 研究目的で詳細なログが必要

---

## 期待される改善効果

### 定量的改善（Dongらの論文より）
- リセット間平均距離: 20-30%改善
- リセット回数: 15-25%削減
- 壁との平均距離: 増加（安全性向上）

### 定性的改善
- ユーザーが開放空間に自然に誘導される
- U字型障害物での動きが改善
- 全体的な経路がスムーズになる
- ローカルミニマからの脱出が可能
- デバッグとパラメータ調整が容易

---

## 可視化の見方

### グリッドベースのポテンシャル場可視化（Enable Grid Potential Visualization = ON）

#### ヒートマップ（床のメッシュ）

**Potential Mode（ポテンシャル値モード）**：
- **青**: 低いポテンシャル（安全、開放空間の中心）
- **シアン**: やや低いポテンシャル
- **緑**: 中程度のポテンシャル
- **黄**: やや高いポテンシャル
- **オレンジ**: 高いポテンシャル
- **赤**: 最も高いポテンシャル（危険、壁に近い）

**Distance Mode（距離値モード）**：
- **青**: 障害物から遠い（安全）
- **緑**: 中程度の距離
- **黄→赤**: 障害物に近い（危険）

**Open Space Mode（開放空間モード）**：
- **緑（強調色）**: 開放空間（Distance >= 1.0m）
- **青→赤のグラデーション**: 非開放空間（距離に応じて色分け）

#### 矢印フィールド（床の矢印）

- **白い矢印**: 負の勾配方向（誘導方向、ポテンシャルが低い方へ）
- 矢印の向き = ユーザーが誘導される方向
- 矢印の長さ = 勾配の強さ（力の大きさ）

### 力のベクトル可視化（既存の機能）

- **赤い矢印**: 斥力（障害物から離れる方向）
- **シアン色の矢印**: 引力（開放空間に向かう方向）
- **緑の矢印**: 合成力（実際のリダイレクション方向）
- **黄色の球**: ステアリングターゲット（誘導先）

### 可視化の活用方法

1. **デバッグ時**:
   - Potential Modeでポテンシャル場の全体像を把握
   - 矢印フィールドで誘導方向を確認
   - ステアリングターゲット（黄色の球）が開放空間に向いているか確認

2. **パラメータ調整時**:
   - Gamma値を変更して、床の色の違いを強調
   - Grid Cell Sizeを変更して、グリッドの粗さを調整
   - Distance Modeで開放空間の分布を確認

3. **研究・論文用**:
   - ヒートマップのスクリーンショットを保存
   - ポテンシャル場の可視化で手法を説明
   - 矢印フィールドでアルゴリズムの動作を示す

---

## 実装の詳細（開発者向け）

### APFGrid.cs の主要メソッド

```csharp
// Distance Fieldを計算（障害物ポリゴンから）
void CalculateDistanceFieldFromPolygons(List<List<Vector2>> polygons)

// Potential Fieldを計算（Distance Fieldから）
void CalculatePotentialField()

// 指定位置の勾配（力の方向）を取得
Vector2 GetGradient2D(Vector3 worldPos)

// 指定位置の障害物までの距離を取得
float GetDistance(Vector3 worldPos)
```

### OpenSpaceFinder.cs の主要メソッド

```csharp
// 最適な開放空間を検出（全体検索）
Vector3 FindBestOpenSpace(Vector3 userPosition, float minDistance)

// 最適な開放空間を検出（局所検索、高速）
Vector3 FindBestOpenSpaceInRadius(Vector3 userPosition, float minDistance, float searchRadius)

// 指定位置が開放空間かどうか判定
bool IsOpenSpace(Vector3 position, float minDistance)
```

### S2G_WithAttraction_Redirector.cs の主要メソッド

```csharp
// グリッドデータを更新
void UpdateGridData()

// ステアリングターゲットを更新
void UpdateSteeringTarget(Vector3 userPosition)

// リダイレクションを適用（親クラスをオーバーライド）
override void InjectRedirection()
```

---

## パフォーマンス最適化のヒント

### 1. セルサイズの調整
- 大きいセル（0.3m）: 高速だが粗い
- 小さいセル（0.1m）: 正確だが遅い
- **推奨**: 0.2mで開始

### 2. 更新頻度の調整
- Target Update Interval = 1.0秒（デフォルト）
- 高速な動きなら0.5秒
- 静的な環境なら2.0秒

### 3. 検索範囲の最適化
- Use Local Search = ON（推奨）
- Local Search Radius = 8m（デフォルト）
- 小さい空間なら5m

### 4. 可視化のOFF（本番環境）
- Enable Grid Potential Visualization = OFF
- Show Attractive Force = OFF
- Show Steering Target = OFF
- Enable Attraction Visualization = OFF

---

## 今後の拡張可能性

### オプション機能（未実装）

1. **Fast Sweeping Method**
   - より高速なDistance Field計算
   - O(N^2) → O(N log N)

2. **マルチスレッド化**
   - グリッド計算を別スレッドで実行
   - メインスレッドをブロックしない

3. **適応的グリッド解像度**
   - 壁の近くは細かいグリッド
   - 開放空間は粗いグリッド

4. **学習ベースの重み調整**
   - ユーザーの行動から最適な重みを学習

---

## 参考文献

1. Hirt, C., et al. (2022). Grid-based artificial potential fields for redirected walking. *IEEE VR*.

2. Dong, Z. C., Chun, Y., & Fu, X. M. (2020). Dynamic Artificial Potential Fields for Multi-User Redirected Walking. *IEEE Transactions on Visualization and Computer Graphics*.

3. Thomas, J., & Rosenberg, E. S. (2019). A general reactive algorithm for redirected walking using artificial potential functions. *IEEE VR*.

4. Khatib, O. (1986). Real-time obstacle avoidance for manipulators and mobile robots. *The International Journal of Robotics Research*, 5(1), 90-98.

---

## ライセンスとクレジット

実装者: Claude (Anthropic)
日付: 2025年12月
ベース: OpenRDW2 (ThomasAPF_Redirector)

---

## サポート

質問や問題があれば、GitHubのIssueで報告してください。
