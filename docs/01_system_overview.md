# 01 システム全体構成

> ⚠️ **本書の一部は旧アーキテクチャ（HostPCProgram 中心）の記述です。**
> 現行方針では MiniPC は **HostPcアプリ（CarrotRape）** を中心に連携し、中心DBは **`host_pc_db`（旧 `image_inspection_db`）** です。
> 最新の方針・読み替えは **[12_host_pc_app_pivot.md](12_host_pc_app_pivot.md)** を参照してください。

## 1. 概要

Spica（プリンター）の組立後に行う**保証工程**のシステムです。
マシンに対してコマンドを送り、電気検査・仕向地設定・画像検査を行います。

---

## 2. システム構成図

![システム構成図](image/system-image.png)

---

## 3. 各システムの役割

### C0L-0160 HostPCProgram（HostPC上）

- MiniPCからのAPIリクエストを受けてトランザクションを管理
- 作業指示コンテンツをタブレット（作業指示Program）に SignalR で配信
- MySQLへのSQL発行（全トランザクション書き込み）
- SignalRでDashboardProgramにリアルタイム更新をブロードキャスト

### C0L-0161 保証工程制御Program（ライン内・MiniPC上）

- プリンター本体にKCFGコマンドを送信
- HostPCProgram のAPIを呼び出してトランザクションを登録・更新
- 工程JSONに従ってステップを順次実行

### C0L-0162 画像検査Program（ライン内・画像検査PC上）

- 実機スキャナを使った画像検査
- 検査結果・作業指示トランザクションを **`host_pc_db`（HostPcアプリが所有する中心DB）と連携**する
- 作業指示が必要なステップでは `host_pc_db` を介してタブレットに表示させる
- オペレーターの OK/NG 応答も `host_pc_db` 経由で通知される

> **注**: 旧版では `host_pc_db`（旧 `image_inspection_db`）を「暫定構成」と位置づけていたが、  
> 現行方針では `host_pc_db` は保証工程の**正式な中心トランザクションDB**であり、HostPcアプリ（CarrotRape）が所有・管理する。  
> （→ [12_host_pc_app_pivot.md](12_host_pc_app_pivot.md)）

### C0L-0163 作業指示Program（ライン内・タブレット上）

- ライン内のタブレットで動作し、オペレーターに作業指示を表示するプログラム
- HostPCProgram から SignalR で作業指示を受信して画面表示する
- オペレーターの OK/NG 入力を HostPCProgram へ送信する

### C0L-0164 DashboardProgram（HostPC上）

- **HostPC 上で動作する**プログラム。表示はライン外のダッシュボード表示専用デバイスのブラウザで行う
- MySQLへ**直接SQLクエリを発行**してデータを取得（SELECT のみ / READ ONLY）
- HostPCProgram を経由せず DB から直接データ参照する
- SignalRでHostPCProgramから**更新通知**を受信し、通知をトリガーにDBへ再クエリして画面を更新
- セル・ゾーンごとの現在状態をモニター表示

---

## 4. 保証工程の構成

### ゾーン構成

| ゾーン | 内容 | ステップ種別 |
|--------|------|------------|
| Soft Install / 電気check | Softインストール・電気系統確認 | 自動 + 手動 |
| 画像検査 | 実機スキャナによる画像検査 | 自動 + 手動 |
| 出荷設定 (A1) | 仕向地設定（言語・紙サイズ・エリア等） | 自動 + 手動 |

### セル・ゾーン構造

```
CELL (cells テーブル)
  └─ ZONE × N  (zones テーブル)
        └─ 1台のプリンターが在席して工程を実行
```

- 1つのセルに複数ゾーンが存在
- ダッシュボード表示のためにゾーンはグリッド座標（GridRow/Col）を持つ

---

## 5. ステップ種別

| 種別 | 説明 | タブレット表示 |
|------|------|--------------|
| 自動 (AUTO) | MiniPCがプリンターにKCFGコマンドを送って完結 | なし |
| 手動 (MANUAL) | 作業者がタブレットで操作・確認 | タイトル＋テキスト＋画像＋フォーム |

- 自動/手動の区別は`process_definition.definition_json`内で定義
- 手動ステップのコンテンツは`work_instruction_master`で管理

---

## 6. 作業指示フォームの種類

| FormType | 表示ボタン | 用途 |
|----------|-----------|------|
| `OK_ONLY` | 「次へ」のみ | 確認・通知のみ |
| `OK_NG` | 「OK」「NG」 | 判定が必要な作業 |

---

## 7. IPアドレス管理

保証工程に入ってくるプリンターはすべて同一のデフォルトIPアドレスを持つため、
ソフトインストール工程で固有IPを採番してマシンシリアルと紐づける。

```
【ソフトインストール工程：IP採番】
作業者がタブレットでマシンのシリアル番号をスキャン
  → 作業指示Program（タブレット）→ HostPCProgram へシリアルを通知
  → HostPCProgram が未使用IPを採番し ip_numbering に登録（シリアルと紐付け）
  → HostPCProgram → MiniPC へ採番したIPを通知（プッシュ）
  → MiniPC → プリンターのIPを設定

【以降の工程】
タブレットでシリアルをスキャンするだけで ip_numbering から対応IPを参照できる
```

> **採番主体**: IP採番・登録は HostPCProgram（C0L-0160）が `ip_numbering`（`prod_process_execution_db`）に対して行う。
> HostPcアプリ（CarrotRape）への移行対象ではなく、従来どおり HostPCProgram が担当する。

---

## 8. NG時の再作業

NGになった場合は**新規の`process_execution`レコードを作成**して再実行する。  
元の実行IDを`retry_of_execution_id`に記録することで再作業の連鎖を追跡できる。

```
execution_id=10  retry_of=NULL  status=NG    ← 1回目
execution_id=25  retry_of=10    status=OK    ← 再作業
```
