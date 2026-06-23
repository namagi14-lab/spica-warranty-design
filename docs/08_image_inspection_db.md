# 08 `host_pc_db`（旧 `image_inspection_db`）DB 仕様

> **`host_pc_db` は保証工程の中心トランザクションDBです。**  
> 旧版ではこのDB（旧名 `image_inspection_db`）を画像検査専用の「暫定構成」と位置づけていましたが、  
> 現行方針では **HostPcアプリ（CarrotRape）が所有する正式な中心DB** となりました。  
> MiniPC はこのDBへ直接 INSERT せず、HostPcアプリの WebAPI を介して登録・更新します。  
> アーキテクチャ全体は **[12_host_pc_app_pivot.md](12_host_pc_app_pivot.md)** を参照。
>
> 以下の本文は旧 `image_inspection_db` の調査時点のテーブル仕様であり、`Session` 中心の実スキーマは
> リポジトリ `CarrotRape`（`Models/SessionData.cs` 等）が正となります。

---

## 1. 概要

| 項目 | 内容 |
|------|------|
| DB名 | `host_pc_db` |
| RDBMS | SQL Server（km.local / `10.183.29.246`） |
| 管理主体 | 画像検査Program（C0L-0162）がスキーマ・データを管理 |
| HostPCProgram の役割 | 読み取り・作業指示結果の更新のみ（スキーマ変更は行わない） |

---

## 2. 通常構成との違い

| 観点 | 通常工程（MiniPC） | 画像検査工程（暫定） |
|------|------------------|-------------------|
| トランザクション書き込み | HostPCProgram が `prod_process_execution_db` に書く | 画像検査Program が `host_pc_db` に直接書く |
| 作業指示トリガー | MiniPC → `/StepApi/UpdateStep` API → HostPCProgram | 画像検査Program が `host_pc_db` に WAIT レコードを INSERT |
| 作業指示表示 | HostPCProgram が SignalR でタブレットに Push | HostPCProgram が `host_pc_db` をポーリングして検知し表示 |
| OK/NG 返却 | HostPCProgram → MiniPC にコールバック（Push） | HostPCProgram が `host_pc_db` の `RaspiExeStatus` を更新 |
| 結果確認 | MiniPC がコールバックを受信 | 画像検査Program が `host_pc_db` の `RaspiExeStatus` をポーリングして確認 |

---

## 3. host_pc_db の主要テーブル

### image_analysis_sessions

画像検査ジョブおよび作業指示セッションを管理するテーブルです。  
画像検査Program がレコードを INSERT し、HostPCProgram が `RaspiExeStatus` と `IsLocked` を更新します。

| カラム名 | 型 | NULL | 説明 |
|---------|-----|------|------|
| `Id` | BIGINT AUTO_INCREMENT | NO | セッションID（PK） |
| `TabletNo` | VARCHAR(50) | NO | タブレット識別番号（例: `TABLET_01`） |
| `MachineCode` | VARCHAR(50) | YES | マシンコード |
| `SerialNo` | VARCHAR(20) | YES | マシンシリアル番号（未確定時は `XXXXXXXXXX`） |
| `RaspiNo` | VARCHAR(50) | YES | RaspiNo（基板識別番号） |
| `OperatorMsg` | TEXT | YES | タブレットに表示する作業指示文字列 |
| `SequenceNo` | INT | YES | シーケンス番号 |
| `StartSequenceNo` | INT | YES | 開始シーケンス番号 |
| `SequenceType` | VARCHAR(50) | YES | シーケンス種別 |
| `RaspiExeStatus` | ENUM('WAIT','OK','NG') | NO | 実行状態（初期値: `WAIT`） |
| `IsLocked` | TINYINT(1) | NO | LOCK状態（0: 未LOCK, 1: LOCK中）（初期値: 0） |
| `CreatedAt` | DATETIME | NO | レコード作成日時 |
| `UpdatedAt` | DATETIME | NO | 最終更新日時 |

#### RaspiExeStatus の遷移

```
WAIT  →  OK     HostPCProgram がオペレーターの OK を受けて更新
WAIT  →  NG     HostPCProgram がオペレーターの NG を受けて更新
```

#### SerialNo の特殊値

| 値 | 意味 |
|----|------|
| `XXXXXXXXXX` | SerialNo 未確定。作業指示Program がオペレーターに入力を促す |
| `ZZZZZZZZZZ` | 特殊処理対象外のマーカー値 |

---

## 4. サーバー接続情報

`host_pc_db` は **km.local（固定IP: `10.183.29.246`）** 上の SQL Server として構築済みです。  
バックアップファイルからリストア済みです。

### 接続先

| 項目 | 値 |
|------|-----|
| サーバー | `10.183.29.246,1433` |
| データベース | `host_pc_db` |
| 認証方式 | SQL Server 認証 |
| ユーザー名 | `spica_test_user` |
| パスワード | `hH8$2trwYf6F` |
| 権限 | `db_owner`（全権限） |

### 接続文字列（ADO.NET / .NET アプリ用）

```
Server=10.183.29.246,1433;
Database=host_pc_db;
User Id=spica_test_user;
Password=hH8$2trwYf6F;
```

### SSMS での接続

| 設定項目 | 値 |
|---------|-----|
| サーバー名 | `10.183.29.246,1433` |
| 認証 | SQL Server 認証 |
| ユーザー名 | `spica_test_user` |
| パスワード | `hH8$2trwYf6F` |

### HostPCProgram の接続設定例

`prod_process_execution_db` とは別の接続文字列を使用します。

```xml
<!-- appsettings.json / Web.config の例 -->
<add name="ImageInspectionDb"
     connectionString="Server=10.183.29.246,1433;Database=host_pc_db;User Id=spica_test_user;Password=hH8$2trwYf6F;" />

---

## 5. 作業指示フロー詳細

### 5.1 作業指示Program が作業待ちを検知するポーリング

作業指示Program（作業指示タブ）は一定周期（`numericUpDownSpanTablet` で設定）で以下を実行します:

1. `GET /api/ImageAnalysisJobs/GetTablet?tabletNo=<TabletNo>` を呼ぶ
2. HostPCProgram が `host_pc_db` から `RaspiExeStatus=WAIT AND IsLocked=0` のレコードを取得して返す
3. 一覧の先頭行を自動的に LOCK して作業指示を表示する

### 5.2 LOCK の排他制御

- LOCK は先頭行（`Id` の昇順で最古のレコード）に対してのみ行う
- `PUT /api/ImageAnalysisJobs/LockById/{id}` で `IsLocked=1` に更新
- LOCK 中は他のオペレーションからの更新を防ぐ

### 5.3 OK/NG 返却と readback

1. `PUT /api/ImageAnalysisJobs/PutTabletExeStatus` で `RaspiExeStatus` を `OK` または `NG` に更新
2. 最大3回 PUT 試行（失敗時リトライ）
3. PUT 成功後、最大3回 readback 確認
   - `GET /api/ImageAnalysisJobs/GetCurrentSessionData/{id}` で `RaspiExeStatus != WAIT` になるまで確認
4. 画像検査Program 側は `host_pc_db` を直接ポーリングして結果を確認する

---

## 6. 方針変更（旧「将来的な移行方針」）

旧版では「将来的に `image_inspection_db` を廃止し `prod_process_execution_db` に統合する」方針でしたが、**この方針は撤回されました。**

現行方針は以下のとおりです（詳細: [12_host_pc_app_pivot.md](12_host_pc_app_pivot.md)）:

1. `host_pc_db`（旧 `image_inspection_db`）を**中心トランザクションDBとして残す**
2. MiniPC・タブレット・画像検査PC は **HostPcアプリ（CarrotRape）の WebAPI** を介して `host_pc_db` と連携する
3. `prod_process_execution_db`（新DB）は**ダッシュボード表示用に残置**し、HostPcアプリが `host_pc_db` への書き込みと同期して**ミラー書き込み**する
4. ダッシュボード（C0L-0164）は従来どおり `prod_process_execution_db` を READ ONLY 参照する
