# 02 DBテーブル定義

- **DB名**: `prod_process_execution_db`
- **RDBMS**: MySQL
- **文字コード**: utf8mb4

---

## テーブル一覧

| テーブル名 | 種別 | 説明 |
|-----------|------|------|
| `cells` | マスター | セル（ライン）管理 |
| `zones` | マスター | ゾーン（位置）管理 |
| `process_master` | マスター | 工程マスタ |
| `process_definition` | マスター | 工程JSON定義（バージョン管理） |
| `work_instruction_master` | マスター | 手動ステップの作業指示コンテンツ |
| `process_execution` | トランザクション | 工程実行（1マシン×1工程） |
| `process_step_execution` | トランザクション | Stepごとの実行結果 |
| `work_instruction_execution` | トランザクション | 作業指示の実行状態（PENDING/OK/NG/SKIPPED） |
| `ip_numbering` | トランザクション | IPアドレス採番管理 |

---

## cells（セル管理）

1つのセルは1つのプロセスと1:1で紐づく。セル内に複数ゾーンが存在する。

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| CellId | INT AUTO_INCREMENT | PK | NO | | セルID |
| CellCode | VARCHAR(50) | UQ | NO | | 識別コード（例: LINE-A） |
| CellName | VARCHAR(100) | | NO | | 表示名 |
| ProcessId | INT | FK | NO | | FK → process_master |
| GridRows | INT | | NO | 2 | ダッシュボードグリッド行数 |
| GridCols | INT | | NO | 2 | ダッシュボードグリッド列数 |
| IsActive | TINYINT(1) | | NO | 1 | 有効フラグ |
| CreatedAt | DATETIME | | NO | CURRENT_TIMESTAMP | 登録日時 |

---

## zones（ゾーン管理）

セル内の物理位置。ダッシュボードのカードレイアウト（グリッド座標）を定義する。

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| ZoneId | INT AUTO_INCREMENT | PK | NO | | ゾーンID |
| CellId | INT | FK | NO | | FK → cells |
| ZoneCode | VARCHAR(50) | UQ(Cell内) | NO | | 識別コード（例: ZONE-A1） |
| ZoneName | VARCHAR(100) | | NO | | 表示名 |
| GridRow | INT | | NO | 1 | グリッド行（1始まり） |
| GridRowSpan | INT | | NO | 1 | グリッド行スパン |
| GridCol | INT | | NO | 1 | グリッド列（1始まり） |
| GridColSpan | INT | | NO | 1 | グリッド列スパン |
| IsActive | TINYINT(1) | | NO | 1 | 有効フラグ |
| CreatedAt | DATETIME | | NO | CURRENT_TIMESTAMP | 登録日時 |

---

## process_master（工程マスタ）

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| ProcessId | INT AUTO_INCREMENT | PK | NO | | 工程ID |
| ProcessCode | VARCHAR(50) | UQ | NO | | 工程コード（例: A1） |
| ProcessName | VARCHAR(100) | | NO | | 工程名 |
| Description | TEXT | | YES | NULL | 説明 |
| IsActive | TINYINT(1) | | NO | 1 | 有効フラグ |
| CreatedAt | DATETIME | | NO | CURRENT_TIMESTAMP | 登録日時 |

---

## process_definition（工程JSON定義）

工程の実行内容をJSONで管理。バージョンごとにレコードを追加し、ハッシュで重複防止。

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| ProcessDefId | INT AUTO_INCREMENT | PK | NO | | 工程定義ID |
| ProcessId | INT | FK | NO | | FK → process_master |
| DefinitionName | VARCHAR(100) | | NO | | 定義名称 |
| DefinitionVersion | VARCHAR(50) | | NO | | バージョン（例: 1.0.0） |
| DefinitionHash | CHAR(64) | UQ | NO | | JSONのSHA-256ハッシュ（重複登録防止） |
| DefinitionJson | JSON | | NO | | 工程JSON全文 |
| CreatedAt | DATETIME | | NO | CURRENT_TIMESTAMP | 登録日時 |

### 工程JSONの構造例

```json
{
  "steps": [
    {
      "step_key": 1,
      "step_type": "AUTO",
      "command": "SoftInstall",
      "params": {}
    },
    {
      "step_key": 2,
      "step_type": "MANUAL",
      "comment": "work_instruction_master.TargetStepKey=2 の指示を表示"
    },
    {
      "step_key": 3,
      "step_type": "AUTO",
      "command": "SetArea",
      "params": { "area": "JP" }
    }
  ]
}
```

---

## work_instruction_master（作業指示マスタ）

手動ステップのタブレット表示コンテンツ。自動ステップは登録不要。  
`TargetStepKey` が NULL の場合は工程開始時（入庫直後）に表示する。

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| InstructionId | INT AUTO_INCREMENT | PK | NO | | 作業指示ID |
| ProcessId | INT | FK | NO | | FK → process_master |
| InstructionName | VARCHAR(200) | | NO | | 手順タイトル |
| InstructionDescription | TEXT | | YES | NULL | 説明テキスト |
| ImagePath | VARCHAR(500) | | YES | NULL | 画像ファイルパス（ファイルサーバー） |
| FormType | ENUM('OK_ONLY','OK_NG') | | NO | 'OK_ONLY' | フォーム種別 |
| TargetStepKey | INT | | YES | NULL | 対象StepKey（NULL=入庫時表示） |
| IsActive | TINYINT(1) | | NO | 1 | 有効フラグ |
| CreatedAt | DATETIME | | NO | CURRENT_TIMESTAMP | 登録日時 |
| UpdatedAt | DATETIME | | NO | CURRENT_TIMESTAMP | 更新日時 |

> **設計ポイント**:  
> 作業指示コンテンツは`process_master`と紐づき、`process_definition`のバージョンとは独立して更新可能。

---

## process_execution（工程実行）

1マシン×1工程の実行を1レコードで管理するトランザクションテーブル。

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| ExecutionId | BIGINT AUTO_INCREMENT | PK | NO | | 工程実行ID |
| MachineSerialNo | VARCHAR(20) | IDX | NO | | 製品シリアル番号 |
| ZoneId | INT | FK | YES | NULL | FK → zones |
| ProcessDefId | INT | FK | NO | | FK → process_definition |
| RetryOfExecutionId | BIGINT | FK | YES | NULL | 再作業元の実行ID（初回はNULL） |
| CurrentStepKey | INT | | YES | NULL | 現在到達しているStepKey |
| StartTime | DATETIME | | NO | | 工程開始時刻 |
| EndTime | DATETIME | | YES | NULL | 工程終了時刻 |
| Status | ENUM('RUNNING','OK','NG','ABORT') | IDX | NO | 'RUNNING' | 工程状態 |

### Statusの遷移

```
RUNNING → OK      正常完了
RUNNING → NG      異常完了（再作業が必要）
RUNNING → ABORT   中断（マシン退室・異常終了）
```

### 再作業の追跡

```
ExecutionId=10  RetryOf=NULL  Status=NG    ← 1回目
ExecutionId=25  RetryOf=10    Status=OK    ← 再作業
```

---

## process_step_execution（Step実行結果）

各Stepの実行結果を記録するトランザクションテーブル。自動・手動問わず全Stepを記録。

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| StepExecId | BIGINT AUTO_INCREMENT | PK | NO | | Step実行ID |
| ExecutionId | BIGINT | FK | NO | | FK → process_execution |
| StepKey | INT | | NO | | JSON内のstep_key |
| ResultStatus | ENUM('OK','NG') | | NO | | Step結果 |
| ExecTime | DATETIME | | NO | | Step完了時刻 |
| Note | TEXT | | YES | NULL | メモ（イレギュラー対応用） |

---

## work_instruction_execution（作業指示実行状態）

工程開始時に`work_instruction_master`から全指示をPENDINGでコピーして作成。  
作業者の操作に応じてResultStatusを更新する。

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| InstructionExecId | BIGINT AUTO_INCREMENT | PK | NO | | 指示実行ID |
| ExecutionId | BIGINT | FK | NO | | FK → process_execution |
| InstructionId | INT | FK | NO | | FK → work_instruction_master |
| ResultStatus | ENUM('PENDING','OK','NG','SKIPPED') | | NO | 'PENDING' | 状態 |
| ExecutedBy | VARCHAR(100) | | YES | NULL | 完了した作業者名 |
| ExecutedAt | DATETIME | | YES | NULL | 完了日時 |

### ResultStatusの遷移

```
PENDING → OK       作業者がOKを押した
PENDING → NG       作業者がNGを押した
PENDING → SKIPPED  工程完了/中断時に残っていた指示（自動スキップ）
```

---

## ip_numbering（IPアドレス採番管理）

保証工程に入ってくるプリンターへ固有IPを割り当てて管理する。

| 列名 | 型 | キー | NULL | デフォルト | 説明 |
|------|----|------|------|-----------|------|
| Id | INT AUTO_INCREMENT | PK | NO | | ID |
| MachineSerial | VARCHAR(20) | IDX | NO | | マシンシリアル番号 |
| IpAddress | VARCHAR(15) | UQ | NO | | 割り当てIPアドレス |
| IsFinished | TINYINT(1) | | NO | 0 | 0:使用中, 1:工程完了（返却可） |
| AssignedAt | DATETIME | | NO | CURRENT_TIMESTAMP | 採番日時 |
