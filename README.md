# Spica 保証工程 システム設計書

> **Spica（プリンター）の保証工程を管理するシステムの設計書・仕様書リポジトリ**  
> アプリ本体は [WorkInstructionApp](https://github.com/namagi14-lab/WorkInstructionApp) にあります。

---

## システム構成

```mermaid
graph TB
    subgraph 製造ライン
        P["🖨️ 製品マシン（Spica）"]
        M["💻 MiniPC（治具）\nHTTPクライアント + HTTPサーバー"]
        P <-->|KCFGコマンド LAN| M
    end

    subgraph HostPC
        H["🖥️ WorkInstructionApp\nASP.NET MVC 5"]
        DB[("🗄️ MySQL\nprod_process_execution_db")]
        H <-->|SQL 書込専用| DB
    end

    subgraph オペレーター側
        T["📱 タブレット\n作業指示専用"]
        Dash["🖥️ ダッシュボード PC\n別プログラム（ProcessDashboard）"]
    end

    subgraph 外部
        DBE["DBEntryApp\n工程マスタ管理"]
    end

    M -->|WebAPI HTTP| H
    H -->|コールバック HTTP| M
    T -->|HTTP| H
    H -->|SignalR| T
    H -->|SignalR（更新通知）| Dash
    Dash -->|SQL READ ONLY（直接参照）| DB
    DBE -->|工程定義同期| H
```

> **マシン特定の原則**: すべての処理でマシンを特定するキーは**シリアル番号**を使用する。

> **DBアクセスポリシー**:
> - **INSERT / UPDATE**: WorkInstructionApp のみが行う（書き込みの一元管理）
> - **SELECT（READ ONLY）**: ダッシュボード（ProcessDashboard）は MySQL へ直接 SQL を発行してデータを参照する
> - SignalR はダッシュボードへの「更新トリガー通知」として使用し、データ本体はダッシュボードが DB から直接取得する
> - MySQLユーザーはアプリケーション別に分離する（WorkInstructionApp 用: 全権限 / ProcessDashboard 用: SELECT のみ）

---

## 保証工程の流れ

```
① IP採番（初工程のみ）
   MiniPC がサーバーに「SN123 の IP をください」と問い合わせ
   → サーバーが ip_numbering プールから空き IP を割り当て
   → MiniPC が製品マシンに IP を付与

② 入室（オペレーターがタブレットから）
   オペレーターがタブレットでシリアル番号を入力・入室操作
   → WorkInstructionApp が process_execution（工程実行）を開始
   → 作業指示・ファイル実行レコードを PENDING で一括生成
   → ダッシュボードにリアルタイム反映（SignalR）

③ 工程 Jsonファイル実行（MiniPC が主導）
   MiniPC が /ProcessFileApi/Next を呼ぶ
   → ハッシュ確認（不一致なら /FileContent でダウンロード）
   → JSON ファイルを製品マシンに実行

④ MANUAL Step でのオペレーター確認（プッシュ型）
   MiniPC が /StepApi/UpdateStep → WorkInstructionApp が判断
   → タブレットに作業指示を表示（SignalR）
   → オペレーターが OK/NG を入力
   → WorkInstructionApp が MiniPC のエンドポイントへコールバック
   ※ MiniPC によるポーリングは不要

⑤ 工程完了
   MiniPC が /MachineApi/Complete → OK / NG / ABORT
   → NG の場合はラインアウト → 修正後に再投入
```

各フローの詳細は **[07_system_design.md](docs/07_system_design.md)** を参照。

---

## ドキュメント一覧

| # | ファイル | 内容 |
|---|---------|------|
| 01 | [system_overview.md](docs/01_system_overview.md) | システム全体構成・概要 |
| 02 | [db_schema.md](docs/02_db_schema.md) | DB テーブル定義・設計方針 |
| 03 | [er_diagram.md](docs/03_er_diagram.md) | ER 図（Mermaid） |
| 04 | [api_spec.md](docs/04_api_spec.md) | MachineApi / StepApi / InstructionApi 仕様 |
| 05 | [sequence.md](docs/05_sequence.md) | 基本シーケンス図 |
| 06 | [process_file_api.md](docs/06_process_file_api.md) | 工程 Jsonファイル API 仕様（/ProcessFileApi） |
| **07** | **[system_design.md](docs/07_system_design.md)** | **システム設計書（Mermaid シーケンス 8本）← メイン** |
| — | [SQL/schema.sql](SQL/schema.sql) | 完全 DDL（CREATE TABLE） |
| — | [docs/process_file_samples/](docs/process_file_samples/) | 工程 JSON サンプルファイル |

---

## API 早見表

### タブレット → WorkInstructionApp（オペレーター操作）

| エンドポイント | 用途 |
|--------------|------|
| `POST /MachineApi/Enter` | **入室**（オペレーターがシリアル番号を入力） |
| `POST /InstructionApi/Complete` | 作業指示を OK/NG で完了 |

### MiniPC → WorkInstructionApp

| エンドポイント | 用途 |
|--------------|------|
| `GET /IpApi/Assign?serialNo=` | **IP 採番**（空き IP をサーバーから取得） |
| `GET /ProcessFileApi/Next?serialNo=` | 次の JSON ファイルを問い合わせ |
| `GET /ProcessFileApi/FileContent/{seqId}` | ファイル内容を取得（ハッシュ不一致時） |
| `POST /StepApi/UpdateStep` | Step 開始通知（MANUAL Step の確認トリガー） |
| `POST /StepApi/RecordStep` | Step 完了を記録 |
| `POST /MachineApi/Complete` | 工程完了（OK/NG） |
| `POST /MachineApi/Exit` | 異常退室・ABORT |

### WorkInstructionApp → MiniPC（コールバック・プッシュ型）

| エンドポイント | 用途 |
|--------------|------|
| `POST /api/instructionResult` | 作業指示の OK/NG 結果をプッシュ通知 |

---

## DB テーブル早見表

### マスタ系

| テーブル | 役割 |
|---------|------|
| `process_master` | 工程マスタ（例: STA 工程、COA 工程） |
| `cells` | セル管理（1 工程 = 1 セル） |
| `zones` | ゾーン管理（セル内の物理ポジション） |
| `process_definition` | 工程定義 JSON（バージョン管理） |
| `work_instruction_master` | 作業指示マスタ（タブレット表示内容） |
| `process_file_sequence` | 工程 Jsonファイル順序・バージョン管理 |
| `users` | 作業者マスタ |
| `ip_numbering` | IP アドレス採番管理（空きプール） |

### トランザクション系

| テーブル | 役割 |
|---------|------|
| `process_execution` | 工程実行レコード（シリアル番号 × 1 工程） |
| `process_step_execution` | Step ごとの実行結果 |
| `work_instruction_execution` | 作業指示の実行状態（PENDING/OK/NG/SKIPPED） |
| `process_file_execution` | MiniPC ファイル実行進捗（PENDING/RUNNING/OK/NG） |

---

## DB 構築手順（新規環境）

```bash
# 1. ベーススキーマを実行
mysql -u root -p < SQL/schema.sql

# 2. WorkInstructionApp の追加マイグレーションを順番に実行
mysql -u root -p prod_process_execution_db < 20260408_prod_db_integration.sql
mysql -u root -p prod_process_execution_db < 20260603_spica_schema_migration.sql
mysql -u root -p prod_process_execution_db < 20260603_process_file_tables.sql
```

---

## 関連リポジトリ

| リポジトリ | 役割 |
|-----------|------|
| [WorkInstructionApp](https://github.com/namagi14-lab/WorkInstructionApp) | HostPC アプリ本体（ASP.NET MVC 5） |
| [ProcessDashboard](https://github.com/namagi14-lab/ProcessDashboard) | ダッシュボード（SignalR リアルタイム表示、別プログラム） |
