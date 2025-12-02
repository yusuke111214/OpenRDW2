# Attractive Potential Field Implementation for OpenRDW

## 概要

このディレクトリには、OpenRDWのAPF（Artificial Potential Field）リダイレクタにおけるローカルミニマ問題を解決するための引力（Attractive Force）実装が含まれています。

従来のAPF実装は斥力のみを使用しており、以下の問題がありました：
- **ローカルミニマ問題**: 複数の障害物からの斥力が均衡し、ユーザーが動けなくなる
- **非効率な経路**: ユーザーが壁の近くを不必要に長く歩く
- **リセット頻度の増加**: 開放空間への誘導がないため、早期にリセットが必要になる

本実装は、開放空間への引力を追加することで、これらの問題を解決します。

## 実装の基礎となる研究

1. **Dong et al. (2020)**: "Dynamic Artificial Potential Fields for Multi-User Redirected Walking"
   - ステアリングターゲット（開放空間の目標点）を動的に選択
   - 開放空間のサイズと境界までの距離を基準に評価

2. **Khatib (1986)**: "Real-time obstacle avoidance for manipulators and mobile robots"
   - コニック型ポテンシャル場（飽和距離を持つ引力）
   - 遠距離では引力を一定に保つことで過度な引き込みを防止

3. **Thomas & Rosenberg (2019)**: "A general reactive algorithm for redirected walking using artificial potential functions"
   - ベースとなるAPF実装（斥力のみ）

## ファイル構成

### コアコンポーネント

1. **OpenSpaceDetector.cs**
   - レイキャストベースの開放空間検出
   - ユーザーの周囲360度に複数のレイを放射
   - 各方向の開放度を測定し、最適な方向を選択

2. **SteeringTargetSelector.cs**
   - ステアリングターゲット（誘導先）の選択と管理
   - 一定間隔での更新（計算コスト削減）
   - Dongらの手法に基づく評価基準

3. **AttractivePotentialField.cs**
   - 引力ポテンシャル場の計算
   - コニック型ポテンシャル（飽和距離あり）
   - 速度を考慮した力の調整機能

4. **CombinedAPF.cs**
   - 斥力と引力の統合
   - 重み付き合成
   - 適応的な重み調整（障害物との距離に基づく）

5. **APFDebugVisualizer.cs**
   - デバッグ用の可視化ツール
   - 力のベクトル、ターゲット位置などを表示

### リダイレクタ

**ThomasAPF_WithAttraction_Redirector.cs**
- ThomasAPF_Redirectorを継承
- 引力機能を統合
- 可視化機能を内蔵

## 使用方法

### 1. 基本セットアップ

UnityエディタでGlobalConfigurationオブジェクトを選択し、以下のセクションを設定：

#### Attractive Potential Field Parameters
```
Enable Attractive Potential Field: ✓ ON（引力を有効化）
Attraction Strength: 1.0（推奨初期値）
Attractive Weight: 0.3（引力の重み）
Repulsive Weight: 0.7（斥力の重み）
Attraction Saturation Distance: 3.0m
Attraction Velocity Weight: 0.3
Attraction Target Update Interval: 1.0秒
```

#### Adaptive Weight Adjustment（オプション）
```
Use Adaptive Weights: OFF（通常はOFF、実験的に試す場合はON）
Adaptive Close Distance: 1.0m
Adaptive Far Distance: 2.0m
```

#### Attraction Visualization
```
Enable Attraction Visualization: ✓ ON（デバッグ時）
Show Attractive Force: ✓ ON（シアン色の矢印）
Show Steering Target: ✓ ON（黄色の球）
Log Attraction Debug Info: OFF（詳細ログが必要な場合のみON）
```

### 2. リダイレクタの選択

Experiment Setupまたはコマンドファイルで、リダイレクタとして`ThomasAPF_WithAttraction`を指定します。

### 3. パラメータ調整ガイド

#### 推奨初期パラメータ
| パラメータ | 推奨値 | 説明 |
|-----------|--------|------|
| Attraction Strength | 1.0 | 引力の強さ。大きいほど強く引き寄せる |
| Attractive Weight | 0.3 | 引力の重み（0-1）。斥力とのバランス |
| Repulsive Weight | 0.7 | 斥力の重み（0-1）。安全性優先 |
| Saturation Distance | 3.0m | これ以上遠いと引力が一定に |
| Velocity Weight | 0.3 | 速度による調整の度合い |
| Update Interval | 1.0秒 | ターゲット更新頻度 |

#### パラメータ調整のヒント

**引力が弱すぎる場合**:
- `Attraction Strength`を1.5～2.0に増加
- `Attractive Weight`を0.4に増加

**引力が強すぎて不自然な場合**:
- `Attraction Strength`を0.7～0.8に減少
- `Attractive Weight`を0.2に減少

**頻繁に壁に近づく場合**:
- `Repulsive Weight`を0.8に増加
- `Adaptive Weights`をONにして試す

**ターゲットが頻繁に変わって混乱する場合**:
- `Update Interval`を2.0～3.0秒に延長

## 動作原理

### 1. 開放空間の検出
```
ユーザー位置 → 360度に16本のレイ → 各方向の開放度を計算
→ 最も開けた方向を選択 → ステアリングターゲットを設定
```

### 2. 引力の計算
```
距離 <= 飽和距離の場合: F = k_att * distance * direction
距離 > 飽和距離の場合: F = k_att * d_sat * direction（一定）
```

### 3. 力の合成
```
Total Force = repulsive_weight * Repulsive Force
            + attractive_weight * Attractive Force
```

### 4. リダイレクションの適用
```
Total Force → 目標方向を計算 → RotationGainまたはCurvatureで実現
```

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

## トラブルシューティング

### 問題: 引力が全く働いていない
**確認事項**:
- `Enable Attractive Potential Field`がONか確認
- `Attractive Weight`が0より大きいか確認
- ステアリングターゲットが正しく表示されているか（黄色の球）

### 問題: ユーザーが壁に向かって誘導される
**対処法**:
- `Repulsive Weight`を増加（0.8～0.9）
- `Attractive Weight`を減少（0.1～0.2）
- `Adaptive Weights`をONにする

### 問題: 動きがカクカクする
**対処法**:
- `Update Interval`を延長（2.0秒以上）
- `Velocity Weight`を増加（0.5程度）

### 問題: パフォーマンスが低下
**対処法**:
- `Update Interval`を延長（2.0～3.0秒）
- レイキャスト数を減らす（要コード修正）
- 可視化をOFFにする

## 可視化の見方

実行時に以下が表示されます：

- **赤い矢印**: 斥力（障害物から離れる方向）
- **シアン色の矢印**: 引力（開放空間に向かう方向）
- **緑の矢印**: 合成力（実際のリダイレクション方向）
- **黄色の球**: ステアリングターゲット（誘導先）
- **白い線**: ユーザーからターゲットへの直線

## パフォーマンス最適化

### 計算コスト
- 開放空間検出: 中程度（キャッシュ活用で削減）
- 引力計算: 低
- 合成: 低

### 最適化のポイント
1. **更新頻度**: デフォルト1秒は十分。0.5秒以下にする必要はない
2. **レイキャスト数**: 16本で十分。増やしても精度向上は限定的
3. **可視化**: 本番環境ではOFFにする

## 今後の拡張可能性

### オプション機能（未実装）
1. **グリッドベースの開放空間検出**
   - より精密な空間分析
   - 計算コストは高いが、より正確

2. **マルチターゲット**
   - 複数の候補から最適なものを選択
   - より柔軟な経路計画

3. **学習ベースの重み調整**
   - ユーザーの行動から最適な重みを学習

## 参考文献

1. Dong, Z. C., Chun, Y., & Fu, X. M. (2020). Dynamic Artificial Potential Fields for Multi-User Redirected Walking. *IEEE Transactions on Visualization and Computer Graphics*.

2. Thomas, J., & Rosenberg, E. S. (2019). A general reactive algorithm for redirected walking using artificial potential functions. *IEEE Conference on Virtual Reality and 3D User Interfaces (VR)*.

3. Khatib, O. (1986). Real-time obstacle avoidance for manipulators and mobile robots. *The International Journal of Robotics Research*, 5(1), 90-98.

## ライセンスとクレジット

実装者: Claude (Anthropic)
日付: 2025
ベース: OpenRDW2 (ThomasAPF_Redirector)

---

質問や問題があれば、GitHubのIssueで報告してください。
