# Spica 保証工程 システム設計書

- **DB**: `prod_process_execution_db`（MySQL）
- **アプリ**: WorkInstructionApp（ASP.NET MVC 5 / IIS）

---

## 登場人物（アクター）

| アクター | 役割 |
|---------|------|
| 製品マシン | 保証対象の製品。工程ごとに MiniPC からコマンドを受け取る |
| MiniPC（治具） | 各ゾーンに設置。製品マシンへコマンドを送り、HostPC と通信する |
| WorkInstructionApp（HostPC） | 工程トランザクションを管理する Web アプリ。MySQL に記録する |
| タブレット | 各ゾーンに設置。オペレーターが作業指示を確認・応答する |
| オペレーター | 作業者。タブレットで作業指示を確認し OK / NG を入力する |
| DBEntryApp | 外部システム。工程マスタ・工程定義 JSON を管理する |
| 管理者 | WorkInstructionApp の管理画面で各種マスタを設定する |

---

## 1. 全体概要フロー

製品が工程ラインを流れる全体像をざっくりと示す。

```mermaid
sequenceDiagram
    actor Op as オペレーター
    participant Machine as 製品マシン
    participant MiniPC as MiniPC（治具）
    participant App as WorkInstructionApp
    participant Tablet as タブレット

    Note over Machine,Tablet: 【事前準備】管理者が工程・Jsonファイル・作業指示を登録済み

    rect rgb(200,230,255)
        Note over Op,MiniPC: ① IP採番（初工程のみ）
        Op->>App: シリアル番号をスキャン登録
        App->>MiniPC: IP割当情報を提供
        MiniPC->>Machine: IPアドレスを付与
    end

    loop 各工程（STA1→STA2→...→COA1→...→Final）
        rect rgb(200,255,200)
            Note over MiniPC,App: ② 入室
            MiniPC->>App: POST /MachineApi/Enter
            App->>App: process_execution 開始 / WI・ファイル実行レコードを PENDING で一括生成
            App-)Tablet: SignalR ダッシュボード更新
        end

        rect rgb(255,255,200)
            Note over MiniPC,Machine: ③ 工程 Jsonファイル実行ループ
            loop 各 Jsonファイル（StepOrder 1, 2, 3...）
                MiniPC->>App: GET /ProcessFileApi/Next
                App-->>MiniPC: fileName / fileHash
                opt ハッシュ不一致
                    MiniPC->>App: GET /ProcessFileApi/FileContent
                    App-->>MiniPC: JSON 内容
                end
                loop Jsonファイル内の各 Step
                    MiniPC->>Machine: コマンド送信
                    MiniPC->>App: POST /StepApi/UpdateStep
                    opt 作業指示あり
                        Tablet->>Op: 作業指示表示
                        Op->>Tablet: OK / NG
                        Tablet->>App: POST /InstructionApi/Complete
                    end
                    MiniPC->>App: POST /StepApi/RecordStep
                end
            end
        end

        rect rgb(255,200,200)
            Note over MiniPC,Tablet: ④ 工程完了
            alt 全 Step OK
                MiniPC->>App: POST /MachineApi/Complete (OK)
                App-)Tablet: SignalR 退室・完了
            else NG 発生
                MiniPC->>App: POST /MachineApi/Complete (NG)
                App-)Tablet: SignalR NG 退室
                Note over Op,Machine: ラインアウト → 修正 → 次工程から再投入
            end
        end
    end
```

---

## 2. IP 採番フロー（初工程のみ）

製品マシンが初めてラインに入るとき、IP アドレスを採番して付与する。

```mermaid
sequenceDiagram
    actor Op as オペレーター
    participant App as WorkInstructionApp
    participant DB as MySQL
    participant MiniPC as MiniPC（治具）
    participant Machine as 製品マシン

    Note over Op,Machine: SetSerial 工程（最初の工程）

    Op->>App: 製品マシンのシリアル番号をスキャン
    App->>DB: ip_numbering にINSERT（MachineSerial, IpAddress, IsFinished=0）
    App-->>Op: IP: 192.168.x.x を割り当てました

    Note over MiniPC,Machine: MiniPC が SetSerial.json を実行
    MiniPC->>App: GET /ProcessFileApi/Next
    App-->>MiniPC: fileName=Y_jsonStr_SetSerial_raspi.json / fileHash
    opt ハッシュ不一致
        MiniPC->>App: GET /ProcessFileApi/FileContent
        App-->>MiniPC: JSON 内容
    end
    MiniPC->>Machine: IP アドレス設定コマンド送信
    Machine-->>MiniPC: 設定完了

    MiniPC->>App: POST /MachineApi/Complete (OK)
    App->>DB: ip_numbering.IsFinished = 1
    App->>DB: process_execution.Status = OK
```

---

## 3. 入室〜工程開始フロー

MiniPC が製品マシンとともにゾーンに入室し、工程トランザクションを開始する。

```mermaid
sequenceDiagram
    participant MiniPC as MiniPC（治具）
    participant App as WorkInstructionApp
    participant DB as MySQL
    participant Tablet as タブレット

    MiniPC->>App: POST /MachineApi/Enter（serialNumber, cellCode, zoneCode）

    App->>DB: zones / cells から ZoneId・ProcessId を解決
    App->>DB: process_definition から最新 ProcessDefId を取得

    alt 同ゾーンに RUNNING が存在する場合
        App->>DB: 既存 process_execution.Status = ABORT
        App->>DB: 残 work_instruction_execution.Status = SKIPPED
        App->>DB: 残 process_file_execution.Status = SKIPPED
    end

    App->>DB: process_execution INSERT（Status=RUNNING, StartTime=NOW()）
    Note right of DB: MachineSerialNo, ZoneId, ProcessDefId を記録

    App->>DB: work_instruction_master から PENDING を一括 INSERT → work_instruction_execution × N
    App->>DB: process_file_sequence（IsActive=1）から PENDING を一括 INSERT → process_file_execution × M（StepOrder 順）

    App-->>MiniPC: success=true, zoneId=3
    App-)Tablet: SignalR zoneUpdated（マシン表示を更新）
```

---

## 4. 工程 Jsonファイル取得フロー

MiniPC がサーバーに「次の JSON ファイル」を問い合わせ、必要ならダウンロードする。

```mermaid
sequenceDiagram
    participant MiniPC as MiniPC（治具）
    participant App as WorkInstructionApp
    participant DB as MySQL

    MiniPC->>App: GET /ProcessFileApi/Next?serialNo=SN123

    App->>DB: process_execution から RUNNING を取得（シリアルで検索）
    App->>DB: 前の RUNNING レコード → OK に更新（CompletedAt=NOW()）
    App->>DB: 次の PENDING を StepOrder ASC で 1 件取得
    App->>DB: → RUNNING に更新（StartedAt=NOW()）

    alt PENDING あり（まだ実行するファイルがある）
        App-->>MiniPC: hasNext=true / seqId / fileName / fileHash / fileVersion

        MiniPC->>MiniPC: ローカルファイルの SHA-256 を計算して比較

        alt ハッシュ一致（最新版を保持）
            MiniPC->>MiniPC: ローカルファイルをそのまま使用
        else ハッシュ不一致（古い or 初回）
            MiniPC->>App: GET /ProcessFileApi/FileContent/{seqId}
            App->>DB: process_file_sequence.FileContent を取得
            App-->>MiniPC: JSON 内容（Content-Type: application/json）
            MiniPC->>MiniPC: ローカルファイルを上書き保存
        end

        MiniPC->>MiniPC: JSON ファイルを実行開始

    else PENDING なし（全ファイル完了）
        App-->>MiniPC: hasNext=false
        Note over MiniPC: 工程完了処理へ
    end
```

---

## 5. Step 実行〜作業指示確認フロー

JSON ファイル内の各 Step を実行し、MANUAL Step ではオペレーター確認を待つ。

```mermaid
sequenceDiagram
    actor Op as オペレーター
    participant Machine as 製品マシン
    participant MiniPC as MiniPC（治具）
    participant App as WorkInstructionApp
    participant DB as MySQL
    participant Tablet as タブレット

    loop JSON ファイル内の Step（step_key 順）
        MiniPC->>Machine: Step コマンド送信（AUTO / MANUAL）
        Machine-->>MiniPC: レスポンス

        MiniPC->>App: POST /StepApi/UpdateStep（serialNumber, stepKey）
        App->>DB: process_execution.CurrentStepKey = stepKey
        App->>DB: work_instruction_master で Display ステップか確認

        alt hasInstruction = true（オペレーター確認が必要な MANUAL Step）
            App-->>MiniPC: success=true / hasInstruction=true
            App-)Tablet: SignalR 作業指示カード表示
            Tablet-->>Op: 指示内容・画像を表示

            loop InstructionStatus ポーリング（約 1 秒間隔）
                MiniPC->>App: GET /StepApi/InstructionStatus（serialNumber, stepKey）
                App->>DB: work_instruction_execution.ResultStatus を参照
                App-->>MiniPC: status=PENDING
            end

            Op->>Tablet: OK または NG を押す
            Tablet->>App: POST /InstructionApi/Complete（instructionExecId, resultStatus, userId）
            App->>DB: work_instruction_execution 更新（ResultStatus, ExecutedBy, ExecutedAt）
            App-)Tablet: SignalR ゾーンカード再描画

            MiniPC->>App: GET /StepApi/InstructionStatus
            App-->>MiniPC: status=OK または NG

            alt オペレーター OK
                MiniPC->>MiniPC: 次の Step へ
            else オペレーター NG
                MiniPC->>MiniPC: エラー処理（工程 NG として完了へ）
            end

        else hasInstruction = false（自動 Step）
            App-->>MiniPC: success=true / hasInstruction=false
            Note over MiniPC: 次の Step へ即進む
        end

        MiniPC->>App: POST /StepApi/RecordStep（serialNumber, stepKey, result=OK/NG）
        App->>DB: process_step_execution INSERT（ExecTime, ResultStatus, Note）
    end
```

---

## 6. 工程完了・退室フロー

工程が正常終了・NG・異常退室した場合の処理。

```mermaid
sequenceDiagram
    participant MiniPC as MiniPC（治具）
    participant App as WorkInstructionApp
    participant DB as MySQL
    participant Tablet as タブレット

    alt 正常完了（全 Step OK）
        MiniPC->>App: POST /MachineApi/Complete（serialNumber, result=OK）
        App->>DB: process_execution.Status = OK / EndTime = NOW()
        App->>DB: 残 work_instruction_execution.Status = SKIPPED
        App-)Tablet: SignalR 退室・ゾーンを空き状態に更新
        App-->>MiniPC: success=true / zoneId=3

    else NG 発生
        MiniPC->>App: POST /MachineApi/Complete（serialNumber, result=NG）
        App->>DB: process_execution.Status = NG / EndTime = NOW()
        App->>DB: 残 work_instruction_execution.Status = SKIPPED
        App-)Tablet: SignalR NG 表示・退室
        App-->>MiniPC: success=true / zoneId=3
        Note over MiniPC,Tablet: 製品はラインアウト → 原因調査・修正
        Note over MiniPC,Tablet: 再投入時：新しい process_execution を開始（RetryOfExecutionId で元の NG と紐付け）

    else 異常退室・緊急停止
        MiniPC->>App: POST /MachineApi/Exit（serialNumber）
        App->>DB: process_execution.Status = ABORT / EndTime = NOW()
        App->>DB: 残 work_instruction_execution.Status = SKIPPED
        App->>DB: 残 process_file_execution.Status = SKIPPED
        App-)Tablet: SignalR ゾーンを空き状態に更新
    end
```

---

## 7. プロセス定義同期フロー（DBEntryApp → WorkInstructionApp）

管理者が手動でトリガーし、外部の DBEntryApp から工程マスタ・定義 JSON を取り込む。

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant App as WorkInstructionApp
    participant DB as MySQL
    participant DBEntry as DBEntryApp

    Admin->>App: GET /ProcessSync（管理画面を開く）
    Admin->>App: 「同期実行」ボタンを押す

    App->>DBEntry: GET /api/processes（工程一覧を取得）
    DBEntry-->>App: ProcessCode, ProcessName, Description のリスト

    loop 各工程コード
        App->>DB: process_master UPSERT（ProcessCode で重複チェック）
    end

    loop 各工程
        App->>DBEntry: GET /api/process/{code}/definition（最新定義 JSON）
        DBEntry-->>App: DefinitionJson, Version, Hash

        App->>DB: DefinitionHash で重複チェック
        alt 新しいハッシュ（未登録）
            App->>DB: process_definition INSERT（新バージョンとして追加）
        else 同一ハッシュ（既登録）
            Note over App,DB: スキップ（重複登録しない）
        end
    end

    App-->>Admin: 同期結果（追加件数・スキップ件数）を表示
```

---

## 8. 管理者セットアップフロー

初期構築・マスタ登録の手順。

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant App as WorkInstructionApp
    participant DB as MySQL

    Note over Admin,DB: 初期セットアップ手順

    Admin->>App: 1. 工程マスタ同期（/ProcessSync）
    App->>DB: process_master・process_definition を登録

    Admin->>App: 2. セル登録（/Master/Cells）
    App->>DB: cells INSERT（CellCode, ProcessId, GridRows, GridCols）

    Admin->>App: 3. ゾーン登録（/Master/Zones）
    App->>DB: zones INSERT（CellId, ZoneCode, GridRow, GridCol, Span）

    Admin->>App: 4. 作業指示登録（/Master/WorkInstructions）
    App->>DB: work_instruction_master INSERT（ProcessId, InstructionName, FormType, TargetStepKey）

    Admin->>App: 5. 工程 Jsonファイル登録（/Master/ProcessFiles）
    App->>DB: process_file_sequence INSERT（ProcessId, StepOrder, FileName, FileHash, FileContent）

    Admin->>App: 6. ユーザー登録（/Master/Users）
    App->>DB: users INSERT（UserName）

    Note over Admin,DB: セットアップ完了 → MiniPC・タブレットが稼働可能
```

---

## テーブル関連図（簡略 ER）

```mermaid
erDiagram
    process_master ||--o{ cells : "1工程 = 1セル"
    process_master ||--o{ process_definition : "バージョン管理"
    process_master ||--o{ work_instruction_master : "工程ごとの指示"
    process_master ||--o{ process_file_sequence : "工程ごとの JSON ファイル"

    cells ||--o{ zones : "セル内のゾーン"

    process_definition ||--o{ process_execution : "実行時バージョン"
    zones ||--o{ process_execution : "どのゾーンで実行"
    process_execution ||--o{ work_instruction_execution : "指示の実行状態"
    process_execution ||--o{ process_step_execution : "Step の実行記録"
    process_execution ||--o{ process_file_execution : "ファイルの実行進捗"

    process_file_sequence ||--o{ process_file_execution : "どのバージョンを使用"
    work_instruction_master ||--o{ work_instruction_execution : "指示内容"
```
