# 04 API仕様

HostPCProgram（HostPC）が提供するAPIの仕様です。  
すべて社内ネットワーク利用を前提とし、認証は不要です。

---

## MachineApi（MiniPC → HostPC）

保証工程制御Program（MiniPC）からHostPCへ発行するAPI。  
トランザクションのライフサイクルを管理します。

---

### POST /MachineApi/Enter

マシンがゾーンに入室したときに呼び出す。`process_execution`を開始し、`work_instruction_execution`をPENDINGで一括作成する。

**リクエストパラメータ**

| パラメータ | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| serialNumber | string | ✓ | マシンシリアル番号 |
| cellCode | string | ✓ | セルコード（例: LINE-A） |
| zoneCode | string | ✓ | ゾーンコード（例: ZONE-A1） |
| processCode | string | | 工程コード（省略時はセルのProcessIdを使用） |

**レスポンス（成功）**

```json
{ "success": true, "zoneId": 3 }
```

**レスポンス（失敗）**

```json
{ "success": false, "message": "ゾーンが見つかりません" }
```

**内部処理**

1. `zones` からzoneIdを取得
2. 同ゾーンにRUNNINGがあればNGに変更
3. `process_execution` にRUNNINGで新規登録
4. `work_instruction_master` から全指示をコピーし `work_instruction_execution` にPENDINGで登録
5. SignalRでダッシュボードに更新をブロードキャスト

---

### POST /MachineApi/UpdateStep

MiniPCがStepを開始した時点で呼び出す。`CurrentStepKey`を更新し、そのStepに作業指示があるかどうかを返す。

**リクエストパラメータ**

| パラメータ | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| serialNumber | string | ✓ | マシンシリアル番号 |
| stepKey | int | ✓ | 現在のStep番号 |

**レスポンス（成功）**

```json
{ "success": true, "hasInstruction": true, "zoneId": 3 }
```

- `hasInstruction: true` の場合、MiniPCはタブレット上での作業者確認を待つ（ポーリング）

---

### POST /MachineApi/RecordStep

Step完了を`process_step_execution`に記録する。自動・手動問わず全Stepで呼ぶ。

**リクエストパラメータ**

| パラメータ | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| serialNumber | string | ✓ | マシンシリアル番号 |
| stepKey | int | ✓ | 完了したStep番号 |
| result | string | ✓ | `OK` または `NG` |

**レスポンス**

```json
{ "success": true }
```

---

### POST /MachineApi/Complete

工程が正常完了（全Step終了）したときに呼び出す。  
`process_execution.Status` を `OK` または `NG` に更新し、残PENDING指示をSKIPPEDにする。

**リクエストパラメータ**

| パラメータ | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| serialNumber | string | ✓ | マシンシリアル番号 |
| result | string | ✓ | `OK` または `NG` |

**レスポンス**

```json
{ "success": true }
```

---

### POST /MachineApi/Exit

マシンがゾーンから退室したときに呼び出す（保険用）。  
`Complete`後にRUNNINGが残っていた場合にNGに変更する。  
通常フロー: `Complete → Exit` の順で呼ぶ。

**リクエストパラメータ**

| パラメータ | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| serialNumber | string | ✓ | マシンシリアル番号 |

**レスポンス**

```json
{ "success": true }
```

---

### GET /MachineApi/InstructionStatus

MiniPCが作業者のタブレット確認状況をポーリングするAPI。  
`hasInstruction: true` のStepでタブレット確認待ちの間、定期的に呼び出す。

**クエリパラメータ**

| パラメータ | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| serialNumber | string | ✓ | マシンシリアル番号 |
| stepKey | int | ✓ | 確認待ちのStep番号 |

**レスポンス**

```json
{
  "status": "OK",
  "executedBy": "作業者A",
  "executedAt": "2026-05-25 14:30:00"
}
```

- `status: "PENDING"` の間はポーリングを継続
- `status: "OK"` または `"NG"` になったらMiniPCは次Stepへ進む（または工程NG）

---

## InstructionApi（外部参照用）

---

### GET /InstructionApi/Status

全ゾーンの現在実行状況を返す。

**レスポンス例**

```json
[
  {
    "cellId": 1, "cellCode": "LINE-A",
    "zoneId": 3, "zoneCode": "ZONE-A1", "zoneName": "電装ポジション1",
    "executionId": 42, "machineSerialNo": "C0L0000001",
    "currentStepKey": 5,
    "pendingCount": 2,
    "instructionName": "電源ケーブル確認"
  }
]
```

---

### GET /InstructionApi/ZoneHistory

特定ゾーンの実行履歴（直近50件）を返す。

**クエリパラメータ**

| パラメータ | 型 | 説明 |
|-----------|-----|------|
| zoneId | int | ゾーンID（直接指定） |
| cellCode | string | セルコード（zoneCodeと組み合わせ） |
| zoneCode | string | ゾーンコード |

**レスポンス例**

```json
{
  "success": true,
  "zoneId": 3,
  "history": [
    {
      "executionId": 42,
      "machineSerialNo": "C0L0000001",
      "startTime": "2026-05-25 14:00:00",
      "endTime": "2026-05-25 14:30:00",
      "status": "OK"
    }
  ]
}
```

---

## StepApi（タブレット → HostPC）

作業者がタブレットで操作したときにHostPCへ通知するAPI。

---

### POST /StepApi/CompleteInstruction

作業者が作業指示を完了（OK/NG）したときに呼び出す。  
`work_instruction_execution.ResultStatus` を更新しSignalRでブロードキャストする。

**リクエストパラメータ**

| パラメータ | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| instructionExecId | long | ✓ | 作業指示実行ID |
| resultStatus | string | ✓ | `OK` または `NG` |
| userId | int | | 作業者ID（省略可） |

**レスポンス**

```json
{ "success": true }
```
