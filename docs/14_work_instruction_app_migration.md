# 14 WorkInstructionApp 移行設計案
## CarrotRape API 統合・MySQL 依存の排除

> **ステータス**: 設計案（実装前レビュー用）  
> 本書は WorkInstructionApp（C0L-0163）を CarrotRape API に全面移行するための設計をまとめたものです。  
> 現行アーキテクチャは [12_host_pc_app_pivot.md](12_host_pc_app_pivot.md)、CarrotRape 仕様は [13_carrotrape_spec.md](13_carrotrape_spec.md) を参照。

---

## 1. 現状と目標

### 現状
WorkInstructionApp（ASP.NET MVC 5 / .NET Framework 4.7.2）は現在 **MySQL（`prod_process_execution_db`）に直接接続**している。

```
[WorkInstructionApp]
  └─ Dapper → MySQL（prod_process_execution_db）
       ├─ cells, zones, work_instruction_master
       ├─ process_execution, work_instruction_execution
       └─ その他マスタ（process_master, users, ip_numbering ...）
```

### 目標
WorkInstructionApp が **CarrotRape の API のみ**を使う構成へ移行する。

```
[WorkInstructionApp]
  └─ HttpClient → CarrotRape WebAPI
                    └─ SQL Server（host_pc_db）
```

---

## 2. 全体影響マップ

### 2-1. WorkInstructionApp 側で変わるもの

| 区分 | 現状 | 変更後 |
|------|------|--------|
| DBアクセス | Dapper → MySQL 直接 | 全廃。CarrotRape API 経由のみ |
| 作業指示取得 | `work_instruction_execution` をポーリング | `GET /api/Session/GetTablet/{tabletNo}` |
| OK/NG送信 | `work_instruction_execution` を UPDATE | `PUT /api/Session/PutTabletExeStatus/{id}/ExeStatusType` |
| Cell/Zone マスタ参照 | `cells`, `zones` テーブルを SELECT | 新設 CarrotRape API `GET /api/Cells`, `GET /api/Zones/{cellId}` |
| Cell/Zone マスタ管理 | 管理者画面から MySQL へ INSERT/UPDATE | 新設 CarrotRape API 経由で SQL Server へ |
| MiniPC への API 提供 | MachineApiController, StepApiController, ProcessFileApiController が MiniPC から叩かれる | **廃止**（MiniPC は CarrotRape を直接呼ぶ新構成） |
| IP採番 API | IpApiController → `ip_numbering` テーブル | CarrotRape 既存 `IpNumberings`（MySQL内）または移行不要 |
| ユーザー管理 | `users` テーブル | 暫定: MySQL 残置または SQL Server 新設 |
| SignalR | InstructionHub → `zoneUpdated` ブロードキャスト | CarrotRape ポーリング方式に変更（または CarrotRape に Hub を移設） |

### 2-2. CarrotRape 側で必要な変更

| 変更 | 内容 |
|------|------|
| テーブル追加（SQL Server） | `Cell`、`Zone` テーブルを `host_pc_db` に追加 |
| API 追加 | CellController・ZoneController（CRUD + ダッシュボード用） |
| 既存 API の流用 | SessionController の GetTablet / PutTabletExeStatus はそのまま使用 |

### 2-3. 変わらないもの・廃止するもの

| 対象 | 判断 |
|------|------|
| `prod_process_execution_db`（MySQL）のダッシュボード用テーブル | **変更なし**（DashboardProgram は引き続き参照） |
| CarrotRape の MySQL ミラー書き込み（Session→MySQLバッチ） | **変更なし** |
| DashboardProgram（C0L-0164） | **変更なし** |
| WorkInstructionApp の `process_master`（MySQL） | CarrotRape の `Jig_Process.SequenceTYPE` で代替 → **MySQL側は廃止** |
| WorkInstructionApp の `process_execution` / `work_instruction_execution` | CarrotRape の `Session` テーブルで代替 → **MySQL側は廃止** |
| WorkInstructionApp の `process_file_execution` / `process_file_sequence` | MiniPC が CarrotRape の `Jig_Process` を参照 → **廃止** |
| WorkInstructionApp の `mini_pcs` テーブル | CarrotRape の `RaspiData` で代替 → **廃止** |
| MachineApiController, StepApiController, ProcessFileApiController | MiniPC が CarrotRape を直接叩く構成のため **廃止** |

---

## 3. CarrotRape 側の追加テーブル設計

### 3-1. Cell テーブル（`host_pc_db` に追加）

物理的な作業エリア単位。`Jig_Process.SequenceTYPE` と紐づける。

```sql
CREATE TABLE Cell (
    Id              INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
    CellCode        NVARCHAR(50)  NOT NULL UNIQUE,
    CellName        NVARCHAR(100) NOT NULL,
    SequenceTYPE    NVARCHAR(50)  NOT NULL,  -- Jig_Process / Session の SequenceTYPE に対応
    GridRows        INT           NOT NULL DEFAULT 1,
    GridCols        INT           NOT NULL DEFAULT 1,
    IsActive        BIT           NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETDATE(),
    UpdatedAt       DATETIME2     NOT NULL DEFAULT GETDATE()
);
```

### 3-2. Zone テーブル（`host_pc_db` に追加）

Cell 内の作業ポジション単位。TabletRelation の `TabletNo` と紐づける。

```sql
CREATE TABLE Zone (
    Id              INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
    CellId          INT           NOT NULL REFERENCES Cell(Id),
    ZoneCode        NVARCHAR(50)  NOT NULL,
    ZoneName        NVARCHAR(100) NOT NULL,
    TabletNo        NVARCHAR(50)  NULL,   -- TabletRelation.TabletNo と対応（NULL=タブレット未割当）
    GridRow         INT           NOT NULL DEFAULT 0,
    GridCol         INT           NOT NULL DEFAULT 0,
    GridRowSpan     INT           NOT NULL DEFAULT 1,
    GridColSpan     INT           NOT NULL DEFAULT 1,
    IsActive        BIT           NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETDATE(),
    UpdatedAt       DATETIME2     NOT NULL DEFAULT GETDATE()
);
```

#### Cell ↔ Zone ↔ CarrotRape 既存テーブルの関係

```
Cell.SequenceTYPE ←→ Session.SequenceTYPE / Jig_Process.SequenceTYPE
Zone.TabletNo     ←→ TabletRelation.TabletNo / Session.TabletNo
```

### 3-3. WorkInstruction マスタについて

現在の `work_instruction_master` は**作業指示の表示名・画像パス・FormType（OK_ONLY / OK_NG）**を持つ。  
CarrotRape では `Session.OperatorMsg`（List\<string\>）が指示テキストを保持する。

**方針**: 移行フェーズ1では WorkInstruction マスタを新設せず、**OperatorMsg の文字列をそのまま表示**する。  
画像表示・FormType 制御が必要になった段階で `WorkInstructionMaster` テーブルを追加する（別 Issue）。

---

## 4. CarrotRape 側の追加 API

### 4-1. CellController（`/api/Cells`）

| メソッド | エンドポイント | 説明 |
|---------|--------------|------|
| GET | `/api/Cells` | 全 Cell 一覧（IsActive=true のみ） |
| GET | `/api/Cells/{id}` | 特定 Cell 取得 |
| POST | `/api/Cells` | Cell 新規作成 |
| PUT | `/api/Cells/{id}` | Cell 更新 |
| DELETE | `/api/Cells/{id}` | Cell 削除（論理削除） |
| GET | `/api/Cells/{id}/Dashboard` | Cell ダッシュボード用（Zone一覧＋各Zoneの現在Session状態） |

#### `/api/Cells/{id}/Dashboard` のレスポンス例

```json
{
  "cellId": 1,
  "cellCode": "C01",
  "cellName": "GCV工程ライン",
  "gridRows": 3,
  "gridCols": 4,
  "zones": [
    {
      "zoneId": 1,
      "zoneCode": "Z01",
      "zoneName": "GCV-A面",
      "tabletNo": "TABLET_01",
      "gridRow": 0,
      "gridCol": 0,
      "gridRowSpan": 1,
      "gridColSpan": 1,
      "currentSession": {
        "id": 123,
        "state": "UI_wait",
        "raspiExeStatus": "WAIT",
        "tabletExeStatus": "LOCK",
        "serialNo": "ZVH0012345",
        "operatorMsg": ["A面ガラス清掃を行ってください", "確認後OKを押してください"],
        "updateTime": "2026-06-25T10:30:00"
      }
    },
    {
      "zoneId": 2,
      "zoneName": "GCV-B面",
      "tabletNo": "TABLET_02",
      "gridRow": 0,
      "gridCol": 1,
      "currentSession": null
    }
  ]
}
```

### 4-2. ZoneController（`/api/Zones`）

| メソッド | エンドポイント | 説明 |
|---------|--------------|------|
| GET | `/api/Zones/ByCellId/{cellId}` | Cell に属する Zone 一覧 |
| GET | `/api/Zones/{id}` | 特定 Zone 取得 |
| POST | `/api/Zones` | Zone 新規作成 |
| PUT | `/api/Zones/{id}` | Zone 更新 |
| PUT | `/api/Zones/{id}/Layout` | グリッド座標のみ更新（ドラッグ操作用） |
| DELETE | `/api/Zones/{id}` | Zone 削除（論理削除） |

---

## 5. WorkInstructionApp 側の変更詳細

### 5-1. 削除するコンポーネント

| ファイル / クラス | 削除理由 |
|----------------|---------|
| `Data/ProcessExecutionRepository.cs` | Session → CarrotRape API で代替 |
| `Data/ProcessFileRepository.cs` | Jig_Process → CarrotRape API で代替 |
| `Data/WorkInstructionRepository.cs` | OperatorMsg で代替（フェーズ1） |
| `Data/MiniPcRepository.cs` | RaspiData → CarrotRape API で代替 |
| `Data/ProcessRepository.cs` | SequenceTYPE → CarrotRape API で代替 |
| `Controllers/MachineApiController.cs` | MiniPC が CarrotRape を直接叩く |
| `Controllers/StepApiController.cs` | 同上 |
| `Controllers/ProcessFileApiController.cs` | 同上 |
| `Controllers/InstructionApiController.cs` | 同上（外部連携が必要なら CarrotRape に移設） |
| `Controllers/IpApiController.cs` | CarrotRape の MySQL 側 IpNumberings で代替 |
| `Hubs/InstructionHub.cs` | ポーリング方式に変更（または CarrotRape 側に移設） |
| `Models/ProcessExecution.cs` 等 実行系モデル | Session モデルで代替 |
| `Views/Master/ProcessFiles.cshtml` 等 Process関連 | 廃止 |
| `Web.config` の MySQL 接続文字列 | 削除 |

### 5-2. 変更するコンポーネント

| ファイル / クラス | 変更内容 |
|----------------|---------|
| `Data/CellRepository.cs` | MySQL クエリ → CarrotRape `GET /api/Cells` 呼び出しに変更 |
| `Data/ZoneRepository.cs` | MySQL クエリ → CarrotRape `GET /api/Zones/ByCellId/{id}` 呼び出しに変更 |
| `Controllers/DashboardController.cs` | CarrotRape API を呼ぶ形にリライト |
| `Controllers/MasterController.cs` | Cell/Zone CRUD を CarrotRape API 経由に変更、廃止アクションを削除 |
| `Models/Cell.cs`, `Models/Zone.cs` | プロパティはほぼ同じ。JSON デシリアライズ用に整理 |
| `Web.config` | MySQL 接続文字列削除、CarrotRape BaseUrl 追加（既にある） |

### 5-3. 追加するコンポーネント

| ファイル / クラス | 内容 |
|----------------|------|
| `Services/CarrotRapeClient.cs` | HttpClient ラッパー。全 API 呼び出しをここに集約 |
| `Models/SessionViewModel.cs` | CarrotRape の Session レスポンスを WorkInstructionApp 側のビューに合わせるモデル |
| `Models/CellDashboardViewModel.cs` | `/api/Cells/{id}/Dashboard` レスポンスに対応するモデル |

### 5-4. 変更後の DashboardController フロー

```csharp
// 現在（MySQL Dapper）
var dashboard = _execRepo.GetCellDashboard(cellId);  // 複雑なJOINクエリ

// 変更後（CarrotRape API）
var dashboard = await _carrotRapeClient.GetCellDashboardAsync(cellId);
// → GET /api/Cells/{cellId}/Dashboard
```

```csharp
// SubmitResult（現在）
_execRepo.CompleteInstruction(instructionExecId, result, userId);

// 変更後
await _carrotRapeClient.PutTabletExeStatusAsync(sessionId, exeStatusType);
// → PUT /api/Session/PutTabletExeStatus/{id}/ExeStatusType
```

---

## 6. 作業指示フローの変化

### 変更前（MySQL ポーリング）
```
タブレット → WorkInstructionApp
  → MySQL: SELECT work_instruction_execution WHERE ZoneId=? AND ResultStatus='PENDING'
  → 画面表示
オペレーターOK → WorkInstructionApp
  → MySQL: UPDATE work_instruction_execution SET ResultStatus='OK'
  → SignalR ブロードキャスト → 全タブレットに zoneUpdated
```

### 変更後（CarrotRape API ポーリング）
```
タブレット → WorkInstructionApp
  → CarrotRape: GET /api/Session/GetTablet/{tabletNo}
  → Session.OperatorMsg を画面表示
オペレーターOK → WorkInstructionApp
  → CarrotRape: PUT /api/Session/PutTabletExeStatus/{id}/OK
  ← MiniPC は PutGetWaitTablet ポーリングで結果を検知
ダッシュボード → WorkInstructionApp（一定間隔ポーリング）
  → CarrotRape: GET /api/Cells/{cellId}/Dashboard
  → 各 Zone の Session 状態を画面更新
```

---

## 7. 移行フェーズ案

### フェーズ1：CarrotRape 拡張（先行）

**担当**: CarrotRape 側  
**作業内容**:
- [ ] `host_pc_db` に `Cell`・`Zone` テーブルを追加（DDL実行）
- [ ] `CellController` 実装（GET/POST/PUT/DELETE + `/Dashboard`）
- [ ] `ZoneController` 実装（GET/POST/PUT/DELETE + `/Layout`）
- [ ] Swagger で動作確認
- [ ] Cell/Zone 初期マスタデータを登録（TabletRelation との整合確認）

**依存**: なし（WorkInstructionApp の変更前に完了可能）

---

### フェーズ2：WorkInstructionApp 移行（メイン）

**担当**: WorkInstructionApp 側  
**作業内容**:
- [ ] `CarrotRapeClient.cs` 作成（HttpClient ラッパー）
- [ ] `CellRepository` / `ZoneRepository` を CarrotRape API 呼び出しに差し替え
- [ ] `DashboardController` リライト（API ポーリングに変更）
- [ ] `MasterController` の Cell/Zone CRUD を CarrotRape API 経由に変更
- [ ] 作業指示取得・OK/NG 送信フローを CarrotRape API に変更
- [ ] MySQL 接続文字列削除
- [ ] 廃止コンポーネント（MachineApiController 等）削除
- [ ] SignalR → ポーリングへの切り替え（または廃止）
- [ ] 動作確認・結合テスト

---

### フェーズ3：ユーザー管理（後回し可）

`users` テーブルは移行コストの割に影響が限定的なため後回し可。  
オプション:
- A: MySQL の `users` テーブルを残置し、CarrotRape とは分離した認証として使い続ける
- B: `User` テーブルを SQL Server に追加し CarrotRape に UserController を実装する

---

## 8. 未決事項・リスク

| 項目 | 内容 | 判断 |
|------|------|------|
| **WorkInstruction マスタの画像・FormType** | OperatorMsg は文字列のみ。画像表示・OK_ONLYタイプは現状 CarrotRape で未対応 | フェーズ1では OperatorMsg テキストのみで運用。必要になったら `WorkInstructionMaster` テーブルを SQL Server に追加 |
| **SignalR のリアルタイム更新** | 現在は MachineApi/StepApi から SignalR でプッシュ。削除後は**ポーリング**になり遅延が生じる | 許容できるポーリング間隔（2〜5秒程度）で実装。CarrotRape に SignalR を移設するのは別フェーズ |
| **ユーザー認証** | `users` テーブルが MySQL にある。移行後も WorkInstructionApp 内でセッション管理が必要 | フェーズ3 で判断。フェーズ2 では MySQL の `users` テーブルのみ残す |
| **IP採番（IpApiController）** | CarrotRape の MySQL 側 `IpNumberings` テーブルが既存。WorkInstructionApp からは CarrotRape 経由または直接 MySQL 参照のどちらでも可 | CarrotRape に IpApi エンドポイントが未実装のため、暫定的に MySQL 参照を残す |
| **`process_master` の移行** | SequenceTYPE への読み替えで代替可能だが、マスタ管理画面をどうするか | 管理者が `Jig_Process` を編集する形（CarrotRape の管理画面）に移管予定 |

---

## 9. 関連ドキュメント

- [12_host_pc_app_pivot.md](12_host_pc_app_pivot.md) — 現行アーキテクチャ方針
- [13_carrotrape_spec.md](13_carrotrape_spec.md) — CarrotRape DB・API 仕様
