# VRMTrackerUnity

iPhone ARKit 顔・手トラッキング → Unity VRM アバター表示アプリ。

## 必要なもの

| ツール | バージョン |
|--------|-----------|
| Unity Hub | 最新版 |
| Unity Editor | **2022.3 LTS** |
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

## OBS への映像出力

| 方法 | 説明 |
|------|------|
| ウィンドウキャプチャ | OBS で Unity ウィンドウを直接キャプチャ |
| クロマキー | カメラ背景を緑にして OBS でクロマキー合成 |
| Unity Virtual Camera | Unity Recorder + Virtual Camera プラグイン |

---

## ファイル構成

```
Assets/Scripts/
  TrackingData.cs     — iOS と共通のデータモデル
  NetworkReceiver.cs  — UDP 受信（ポート 12345）
  FaceMapper.cs       — ARKit → VRM 1.0 表情マッピング (47 エントリ)
  BoneMapper.cs       — 頭部回転（YXZ オイラー分解、JS リファレンスと同方式）
  AvatarController.cs — VRM 読み込み・毎フレーム処理
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
