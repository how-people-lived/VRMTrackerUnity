# VRMTrackerUnity

iPhone ARKit **表情トラッキング** → Unity VRM アバター表示アプリ（OBS 配信向け）。
表情と頭の向きを反映し、腕・指は自然な立ち姿で固定します。

## 必要なもの

| ツール | バージョン |
|--------|-----------|
| Unity Hub | 最新版 |
| Unity Editor | **6000.0.76f1**（Unity 6） |
| Xcode | 15 以上（iPhone ビルド用） |

---

## セットアップ手順

### 1. Unity でプロジェクトを開く

1. Unity Hub を起動
2. 「プロジェクトを追加」→ このフォルダ（`VRMTrackerUnity/`）を選択
3. Unity 2022.3 LTS で開く
4. 初回起動時に UniVRM パッケージが自動でダウンロードされます（数分かかります）

### 2. シーンを作成する

**File → New Scene** で空のシーンを作成し、以下の GameObject を追加します。

#### (a) Controller オブジェクト
```
GameObject（名前: VRMController）
  ├─ NetworkReceiver.cs  ← Port: 12345
  ├─ FaceMapper.cs
  ├─ BoneMapper.cs
  └─ AvatarController.cs
```

`AvatarController` の Inspector で `Receiver`, `FaceMapper`, `BoneMapper` の各フィールドに同じ GameObject をドラッグして設定します。

#### (b) カメラ設定
Main Camera を選択し：
- Background Type: **Solid Color**
- Background: 好みの色（OBS でクロマキー合成する場合は緑 `#00FF00`）
- Clear Flags: Solid Color

#### (c) ライト
Directional Light を 1 つ追加し、アバターに光が当たるよう向きを調整します。

#### (d) UI キャンバス（任意）
Canvas（Screen Space - Overlay）に以下の Text / Button を追加：
- `Status Label` → AvatarController.statusLabel にドラッグ
- `Connection Label` → AvatarController.connectionLabel
- `FPS Label` → AvatarController.fpsLabel
- `Load Button` → onClick: `AvatarController.OnLoadButtonClick`

シーンを **Scenes/Main.unity** として保存します。

### 3. macOS ビルド設定

**File → Build Settings**：
- Platform: **macOS**
- Architecture: Apple Silicon / Intel
- Scripting Backend: IL2CPP（推奨）または Mono

**Player Settings**：
- App Sandbox: **オフ**（ネットワーク受信のため）
- Network: Incoming Connections を許可

### 4. iPhone 側の設定

iPhone の VRM Tracker アプリを起動し、右上の **歯車アイコン** をタップして、Mac の **IP アドレス** を入力します。

Mac の IP 確認：システム設定 → Wi-Fi → 詳細 → TCP/IP タブ

### 5. VRM を読み込む

Unity の Play モードまたはビルドしたアプリで：
- VRM ファイルをウィンドウに**ドラッグ＆ドロップ**
- エディター実行中は「Load」ボタンからファイルダイアログを開けます

---

## 操作方法（実行中）

| キー / 操作 | 機能 |
|------|------|
| **Tab** | コントロールパネルの表示/非表示 |
| **H** | クリーンモード（UI を全て隠す。OBS キャプチャ用） |
| ドラッグ＆ドロップ | `.vrm` ファイルを読み込み |
| Enter | パス入力欄の VRM を読み込み |

コントロールパネル（Tab）でできること：
- **背景色**：緑/青クロマキー・黒/白/グレー・マゼンタのプリセット ＋ RGB スライダーでカスタム（設定は保存されます）
- **出力解像度**：1280×720 / 1920×1080 / 720×1280（縦）
- **クリーンモード**切替

## 絵作り（VisualEnhancer）— VRoidっぽさを抑える

モデルを編集せず、URP の描画だけで見栄えを底上げします（`VisualEnhancer.cs`）。
`AvatarController` が起動時に自動でコンポーネントを追加し、VRM 読み込み後に適用します。

- **ポストプロセス**：ACES トーンマップ／Bloom／カラーグレーディング（露出・コントラスト・彩度・色温度）／Vignette
- **リムライト**：MToon の縁取り光を有効化（VRoid 標準では切れている＝平面的に見える原因）
- **陰影**：影色を寒色寄りに補強し立体感を付与
- **バックライト**：背後からのリム分離光（背景に影は落とさない）

**調整したい場合**：`VRMController`（AvatarController が付いた GameObject）に
`VisualEnhancer` コンポーネントを手動で追加すると、Inspector で各値を変更できます。

> **OBS クロマキーと併用する場合**：Vignette とBloomが背景の単色を歪めることがあります。
> `VisualEnhancer` の **`Obs Chroma Safe`** をオンにすると背景を平坦に保てます。

## OBS への映像出力（ウィンドウキャプチャ＋クロマキー）

1. Unity アプリを起動し、**Tab** で背景を「緑 (Chroma)」に設定。
2. **H** を押して UI を隠す（クリーンモード）。
3. OBS で **ソース → ウィンドウキャプチャ** を追加し、Unity ウィンドウを選択。
   - macOS の「画面収録」権限を OBS に許可してください。
4. そのソースに **フィルタ → クロマキー** を追加し、キーの種類を「緑」に。背景が透過します。
5. 影は背景（カメラの単色クリア）に落ちないため、クロマキーのエッジはきれいに抜けます。

> 非フォーカス時もレンダリングを継続（`Application.runInBackground`）するため、
> OBS で操作しながらでも映像が止まりません。フレームレートは既定 60fps です。

---

## ファイル構成

```
Assets/Scripts/
  TrackingData.cs     — iOS と共通のデータモデル
  NetworkReceiver.cs  — UDP 受信（ポート 12345）
  FaceMapper.cs       — ARKit → VRM 1.0 表情マッピング (47 エントリ)
  BoneMapper.cs       — 頭部・首の回転（YXZ オイラー分解）＋ 自然な立ち姿の固定
  AvatarController.cs — VRM 読み込み・背景色/クリーンモード/解像度・毎フレーム処理
```

---

## Spring Bone について

UniVRM の spring bone は **仕様通りの Verlet 積分**で動作します。
追加設定は不要です。VRM ファイルに含まれる物理パラメータが自動で適用されます。

---

## 通信仕様

| 項目 | 値 |
|------|----|
| プロトコル | UDP (IPv4) |
| ポート | 12345 |
| フォーマット | JSON (UTF-8) |
| 送信元 | iPhone アプリ（UDPSender.swift） |
| 送信先 | Mac 上の Unity アプリ |

---

## トラブルシューティング

**パッケージのダウンロードが失敗する**  
→ Unity Hub でリポジトリへの GitHub アクセスが必要です。ネットワーク接続を確認してください。

**UDP パケットが届かない**  
→ macOS ファイアウォールで Unity への着信を許可してください（システム設定 → ネットワーク → ファイアウォール）。

**表情が動かない**  
→ iPhone の ARKit を使うため TrueDepth カメラ搭載機種（Face ID 対応）が必要です。
