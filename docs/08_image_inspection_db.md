# 08 画像検査専用DB 仕様（暫定構成）

> **この仕様は暫定対応です。**  
> 画像検査Program（C0L-0162）の担当開発者が現行システムのAPI連携対応が困難なため、  
> 既存の構成をそのまま流用する形で `image_inspection_db` を仲介する構成を採用しています。  
> **将来的には C0L-0161（MiniPC）と同様の HostPCProgram API 連携方式に統一する予定です。**

---

## 1. 概要

| 項目 | 内容 |
|------|------|
| DB名 | `image_inspection_db` |
| RDBMS | SQL Server（km.local / `10.183.29.246`） |
| 管理主体 | 画像検査Program（C0L-0162）がスキーマ・データを管理 |
| HostPCProgram の役割 | 読み取り・作業指示結果の更新のみ（スキーマ変更は行わない） |

---

## 2. 通常構成との違い

| 観点 | 通常工程（MiniPC） | 画像検査工程（暫定） |
|------|------------------|-------------------|
| トランザクション書き込み | HostPCProgram が `prod_process_execution_db` に書く | 画像検査Program が `image_inspection_db` に直接書く |
| 作業指示トリガー | MiniPC → `/StepApi/UpdateStep` API → HostPCProgram | 画像検査Program が `image_inspection_db` に WAIT レコードを INSERT |
| 作業指示表示 | HostPCProgram が SignalR でタブレットに Push | HostPCProgram が `image_inspection_db` をポーリングして検知し表示 |
| OK/NG 返却 | HostPCProgram → MiniPC にコールバック（Push） | HostPCProgram が `image_inspection_db` の `RaspiExeStatus` を更新 |
| 結果確認 | MiniPC がコールバックを受信 | 画像検査Program が `image_inspection_db` の `RaspiExeStatus` をポーリングして確認 |

---

## 3. image_inspection_db の主要テーブル

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

`image_inspection_db` は **km.local（固定IP: `10.183.29.246`）** 上の SQL Server として構築済みです。  
バックアップファイルからリストア済みです。

### 接続先

| 項目 | 値 |
|------|-----|
| サーバー | `10.183.29.246,1433` |
| データベース | `image_inspection_db` |
| 認証方式 | SQL Server 認証 |
| ユーザー名 | `spica_test_user` |
| パスワード | `hH8$2trwYf6F` |
| 権限 | `db_owner`（全権限） |

### 接続文字列（ADO.NET / .NET アプリ用）

```
Server=10.183.29.246,1433;
Database=image_inspection_db;
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
     connectionString="Server=10.183.29.246,1433;Database=image_inspection_db;User Id=spica_test_user;Password=hH8$2trwYf6F;" />

---

## 5. 作業指示フロー詳細

### 5.1 作業指示Program が作業待ちを検知するポーリング

作業指示Program（作業指示タブ）は一定周期（`numericUpDownSpanTablet` で設定）で以下を実行します:

1. `GET /api/ImageAnalysisJobs/GetTablet?tabletNo=<TabletNo>` を呼ぶ
2. HostPCProgram が `image_inspection_db` から `RaspiExeStatus=WAIT AND IsLocked=0` のレコードを取得して返す
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
4. 画像検査Program 側は `image_inspection_db` を直接ポーリングして結果を確認する

---

## 6. 将来的な移行方針

将来的に画像検査Program が HostPCProgram の API 連携に対応した際は、以下の変更を行います:

1. 画像検査Program を MiniPC（C0L-0161）と同様の API 呼び出し方式に変更
2. `image_inspection_db` の廃止
3. `prod_process_execution_db` に統合（`process_execution` / `work_instruction_execution` テーブルを使用）
4. HostPCProgram の `/api/ImageAnalysisJobs/*` エンドポイント群を削除

移行後は本ドキュメント（08_image_inspection_db.md）は不要となります。
