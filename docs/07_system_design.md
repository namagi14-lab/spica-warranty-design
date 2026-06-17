# Spica 保証工程 システム設計書

- **DB**: `prod_process_execution_db`（MySQL）
- **アプリ**: HostPCProgram（ASP.NET MVC 5 / IIS）

---

## 登場人物（アクター）

| アクター | 役割 |
|---------|------|
| 製品マシン | 保証対象の製品。MiniPC からコマンドを受け取る |
| MiniPC（ライン内） | 各ゾーンに設置。**保証工程制御Program（C0L-0161）**が動作。製品マシンを制御し、HostPCProgram と通信する。**HTTP サーバーも兼ねる（コールバック受信）** |
| HostPCProgram（HostPC） | **C0L-0160**。工程トランザクションを管理する Web アプリ（ASP.NET MVC 5）。MiniPC API・タブレット向け SignalR・DashboardProgram 向け通知を担う |
| タブレット（ライン内） | **作業指示Program（C0L-0163）**が動作。オペレーターが作業内容を確認し OK/NG を入力する |
| 画像検査PC（ライン内） | **画像検査Program（C0L-0162）**が動作。実機スキャナで画像検査を行い、`image_inspection_db` へ直接書き込む（暫定構成） |
| DashboardProgram（HostPC） | **C0L-0164**。HostPC 上で動作。SignalR で更新通知を受け取り、データは MySQL へ直接 SQL（SELECT のみ）で取得する。**表示はライン外のダッシュボード表示専用デバイスのブラウザで行う** |
| ダッシュボード表示デバイス（ライン外） | ブラウザで DashboardProgram にアクセスし、工程の進捗をモニター表示する専用デバイス |
| オペレーター | 作業者。タブレットで作業指示を確認し OK / NG を入力する |
| DBEntryApp | 外部システム。工程マスタ・工程定義 JSON を管理する |
| 管理者 | HostPCProgram の管理画面で各種マスタを設定する |

> **マシン特定の原則**: すべての処理でマシンを特定するキーは **シリアル番号** を使用する。

---

## 1. 全体概要フロー

製品が工程ラインを流れる全体像。

```mermaid
sequenceDiagram
    actor Op as オペレーター
    participant Tablet as タブレット（作業指示専用）
    participant Dash as DashboardProgram（HostPC・ブラウザ）
    participant App as HostPCProgram
    participant DB as MySQL
    participant MiniPC as MiniPC（治具）
    participant Machine as 製品マシン

    Note over App,Machine: 【事前準備】管理者が工程・Jsonファイル・作業指示を登録済み

    rect rgb(200,230,255)
        Note over MiniPC,Machine: ① IP採番（初工程のみ）
        MiniPC->>App: GET /IpApi/Assign?serialNo=SN123
        App-->>MiniPC: { ipAddress: "192.168.1.10" }
        MiniPC->>Machine: IPアドレスを付与
    end

    loop 各工程（STA1 → STA2 → ... → COA1 → ... → Final）

        rect rgb(200,255,200)
            Note over Op,App: ② 入室（オペレーターがタブレットから操作）
            Op->>Tablet: シリアル番号を入力・入室操作
            Tablet->>App: POST /MachineApi/Enter { serialNo }
            App->>App: process_execution 開始 / WI・ファイル実行レコードを PENDING 一括生成
            App-->>Tablet: { success: true }
            App-)Dash: SignalR: 更新通知（入室）
            Dash->>DB: SELECT（直接SQL参照）
        end

        rect rgb(255,255,200)
            Note over MiniPC,Machine: ③ 工程 Jsonファイル実行ループ
            loop 各 Jsonファイル（StepOrder 1, 2, 3...）
                MiniPC->>App: GET /ProcessFileApi/Next?serialNo=SN123
                App-->>MiniPC: { fileName, fileHash }
                opt ハッシュ不一致
                    MiniPC->>App: GET /ProcessFileApi/FileContent/{seqId}
                    App-->>MiniPC: JSON 内容
                end
                loop 各 Step
                    MiniPC->>Machine: コマンド送信
                    MiniPC->>App: POST /StepApi/UpdateStep { serialNo, stepKey }
                    opt 作業指示あり（MANUAL Step）
                        App-)Tablet: SignalR: 作業指示を表示
                        Op->>Tablet: OK / NG
                        Tablet->>App: POST /InstructionApi/Complete
                        App->>MiniPC: POST /api/instructionResult { result }
                    end
                    MiniPC->>App: POST /StepApi/RecordStep
                end
            end
        end

        rect rgb(255,200,200)
            Note over MiniPC,Dash: ④ 工程完了
            alt 全 Step OK
                MiniPC->>App: POST /MachineApi/Complete { serialNo, result: OK }
            else NG 発生
                MiniPC->>App: POST /MachineApi/Complete { serialNo, result: NG }
                Note over Op,Machine: ラインアウト → 修正 → 再投入
            end
            App-)Dash: SignalR: 更新通知（退室）
            Dash->>DB: SELECT（直接SQL参照）
        end
    end
```

> **ダッシュボードのデータ取得方針**:  
> SignalR はデータを持たない「更新トリガー通知」のみに使用する。  
> データ本体はダッシュボードが MySQL へ直接 SQL（SELECT のみ）を発行して取得する。  
> INSERT / UPDATE は HostPCProgram のみが行い、ダッシュボードは書き込みを行わない。

---

## 2. IP 採番フロー（初工程のみ）

製品マシンが初めてラインに入るとき、サーバーの IP プールから空き IP を割り当てて MiniPC 経由で製品に付与する。

```mermaid
sequenceDiagram
    participant MiniPC as MiniPC（治具）
    participant App as HostPCProgram
    participant DB as MySQL
    participant Machine as 製品マシン

    Note over MiniPC,Machine: SetSerial 工程（最初の工程）

    MiniPC->>App: GET /IpApi/Assign?serialNo=SN123
    App->>DB: ip_numbering から IsFinished=0 の空き IP を 1 件取得
    App->>DB: ip_numbering UPDATE（MachineSerial=SN123 を紐付け、使用中フラグ）
    App-->>MiniPC: { ipAddress: "192.168.1.10" }

    MiniPC->>Machine: IP アドレス設定コマンド送信（SetSerial.json の内容）
    Machine-->>MiniPC: 設定完了

    Note over MiniPC,DB: 工程完了後
    MiniPC->>App: POST /MachineApi/Complete { serialNo, result: OK }
    App->>DB: ip_numbering.IsFinished = 1（使用終了）
    App->>DB: process_execution.Status = OK
```

---

## 3. 入室〜工程開始フロー

**オペレーターがタブレットから**シリアル番号を入力して入室を登録する。

```mermaid
sequenceDiagram
    actor Op as オペレーター
    participant Tablet as タブレット（作業指示専用）
    participant App as HostPCProgram
    participant DB as MySQL
    participant Dash as DashboardProgram（HostPC・ブラウザ）

    Op->>Tablet: シリアル番号を入力（バーコードスキャン等）
    Tablet->>App: POST /MachineApi/Enter { serialNo }

    App->>DB: serialNo に対応する工程・ゾーンを解決
    App->>DB: process_definition から最新 ProcessDefId を取得

    alt 同シリアルで RUNNING が存在する場合（二重入室防止）
        App->>DB: 既存 process_execution.Status = NG
        App->>DB: 残 work_instruction_execution.Status = SKIPPED
        App->>DB: 残 process_file_execution.Status = SKIPPED
    end

    App->>DB: process_execution INSERT（Status=RUNNING, MachineSerialNo=SN123）
    App->>DB: work_instruction_master から PENDING を一括 INSERT<br/>→ work_instruction_execution × N
    App->>DB: process_file_sequence（IsActive=1）から PENDING を一括 INSERT<br/>→ process_file_execution × M（StepOrder 順）

    App-->>Tablet: { success: true }
    App-)Dash: SignalR: 更新通知（入室）
    Dash->>DB: SELECT（直接SQL参照）
```

---

## 4. 工程 Jsonファイル取得フロー

MiniPC がサーバーに「次の JSON ファイル」を問い合わせ、必要ならダウンロードする。マシン特定はすべてシリアル番号で行う。

```mermaid
sequenceDiagram
    participant MiniPC as MiniPC（治具）
    participant App as HostPCProgram
    participant DB as MySQL

    MiniPC->>App: GET /ProcessFileApi/Next?serialNo=SN123

    App->>DB: process_execution から serialNo で RUNNING を取得
    App->>DB: 前の RUNNING レコード → OK に更新（CompletedAt=NOW()）
    App->>DB: 次の PENDING を StepOrder ASC で 1 件取得
    App->>DB: → RUNNING に更新（StartedAt=NOW()）

    alt PENDING あり（まだ実行するファイルがある）
        App-->>MiniPC: { hasNext:true, seqId, fileName, fileHash, fileVersion }

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
        App-->>MiniPC: { hasNext: false }
        Note over MiniPC: 工程完了処理へ
    end
```

---

## 5. Step 実行〜作業指示フロー（プッシュ型）

MANUAL Step では MiniPC がサーバーに通知し、サーバーがタブレットへ表示・完了後に **MiniPC のエンドポイントへコールバック**する。MiniPC 側のポーリングは不要。

```mermaid
sequenceDiagram
    actor Op as オペレーター
    participant Machine as 製品マシン
    participant MiniPC as MiniPC（治具）
    participant App as HostPCProgram
    participant DB as MySQL
    participant Tablet as タブレット（作業指示専用）

    loop JSON ファイル内の Step（step_key 順）
        MiniPC->>Machine: Step コマンド送信（AUTO / MANUAL）
        Machine-->>MiniPC: レスポンス

        MiniPC->>App: POST /StepApi/UpdateStep { serialNo, stepKey }
        App->>DB: process_execution.CurrentStepKey = stepKey
        App->>DB: work_instruction_master で MANUAL Step か確認

        alt hasInstruction = true（オペレーター確認が必要な MANUAL Step）
            App-->>MiniPC: { success:true, hasInstruction:true }
            Note over MiniPC: コールバックを待機（ポーリング不要）

            App-)Tablet: SignalR: 作業指示を表示（内容・画像）
            Tablet-->>Op: 指示内容を表示

            Op->>Tablet: OK または NG を押す
            Tablet->>App: POST /InstructionApi/Complete { instructionExecId, result, userId }
            App->>DB: work_instruction_execution 更新（ResultStatus, ExecutedByUserId, ExecutedAt）

            App->>MiniPC: POST /api/instructionResult { serialNo, stepKey, result: OK/NG }
            Note right of App: MiniPC のエンドポイントへプッシュ通知<br/>（MiniPC IP は ゾーン設定 or 登録情報から解決）
            MiniPC-->>App: 受信確認（200 OK）

            alt result = OK
                MiniPC->>MiniPC: 次の Step へ進む
            else result = NG
                MiniPC->>MiniPC: エラー処理・工程 NG として完了へ
            end

        else hasInstruction = false（AUTO Step）
            App-->>MiniPC: { success:true, hasInstruction:false }
            Note over MiniPC: 次の Step へ即進む
        end

        MiniPC->>App: POST /StepApi/RecordStep { serialNo, stepKey, result: OK/NG }
        App->>DB: process_step_execution INSERT
    end
```

---

## 6. 工程完了・退室フロー

```mermaid
sequenceDiagram
    participant MiniPC as MiniPC（治具）
    participant App as HostPCProgram
    participant DB as MySQL
    participant Dash as DashboardProgram（HostPC・ブラウザ）

    alt 正常完了（全 Step OK）
        MiniPC->>App: POST /MachineApi/Complete { serialNo, result: OK }
        App->>DB: process_execution.Status = OK / EndTime = NOW()
        App->>DB: 残 work_instruction_execution.Status = SKIPPED
        App-)Dash: SignalR: 更新通知（退室 OK）
        Dash->>DB: SELECT（直接SQL参照）

    else NG 発生（工程失敗）
        MiniPC->>App: POST /MachineApi/Complete { serialNo, result: NG }
        App->>DB: process_execution.Status = NG / EndTime = NOW()
        App->>DB: 残 work_instruction_execution.Status = SKIPPED
        App-)Dash: SignalR: 更新通知（退室 NG）
        Dash->>DB: SELECT（直接SQL参照）
        Note over MiniPC,Dash: 製品はラインアウト → 原因調査・修正<br/>再投入時は新 process_execution（RetryOfExecutionId で元 NG と紐付け）

    else 異常退室・緊急停止
        MiniPC->>App: POST /MachineApi/Exit { serialNo }
        App->>DB: process_execution.Status = NG / EndTime = NOW()
        App->>DB: 残 work_instruction_execution / process_file_execution = SKIPPED
        App-)Dash: SignalR: 更新通知（異常退室）
        Dash->>DB: SELECT（直接SQL参照）
    end

    App-->>MiniPC: { success: true }
```

---

## 7. プロセス定義同期フロー（DBEntryApp → HostPCProgram）

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant App as HostPCProgram
    participant DB as MySQL
    participant DBEntry as DBEntryApp

    Admin->>App: 「同期実行」ボタンを押す（/ProcessSync）

    App->>DBEntry: GET /api/processes（工程一覧）
    DBEntry-->>App: [ { ProcessCode, ProcessName }, ... ]

    loop 各工程コード
        App->>DB: process_master UPSERT（ProcessCode で重複チェック）
    end

    loop 各工程
        App->>DBEntry: GET /api/process/{code}/definition（最新定義 JSON）
        DBEntry-->>App: { DefinitionJson, Hash }

        App->>DB: DefinitionHash で重複チェック
        alt 新しいハッシュ
            App->>DB: process_definition INSERT（新バージョン）
        else 同一ハッシュ
            Note over App,DB: スキップ（重複登録しない）
        end
    end

    App-->>Admin: 同期結果（追加件数・スキップ件数）
```

---

## 8. 管理者セットアップフロー

```mermaid
sequenceDiagram
    actor Admin as 管理者
    participant App as HostPCProgram
    participant DB as MySQL

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
    App->>DB: users INSERT（UserCode, UserName）

    Note over Admin,DB: セットアップ完了 → 稼働可能
```

---

## テーブル関連図（ER）

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
    ip_numbering }o--|| process_execution : "シリアル番号で紐付け"
```

---

## 9. 画像検査工程フロー（暫定構成：専用DB経由）

> **この構成は暫定対応です。** 画像検査Program（C0L-0162）が現行のAPI連携に対応できないため、  
> `image_inspection_db`（HostPC上の専用DB）を仲介する構成を採用しています。  
> 将来的には通常の MiniPC → HostPCProgram API 構成に統一する予定です。

### 9-1. 画像検査での自動ステップ（作業指示なし）

```mermaid
sequenceDiagram
    participant IMG as 画像検査Program（C0L-0162）
    participant IDB as image_inspection_db（画像検査専用DB）

    IMG->>IDB: INSERT image_analysis_sessions（RUNNING）
    IMG->>IMG: スキャン実行・画像解析
    IMG->>IDB: UPDATE image_analysis_sessions（OK / NG）
```

---

### 9-2. 画像検査での手動ステップ（作業指示あり）

作業指示が必要なステップでは、画像検査Program が `image_inspection_db` に作業指示レコードを書き込む。  
HostPCProgram の作業指示Program（タブレットタブ）が DB をポーリングして検知し、タブレットに表示する。  
オペレーターが OK/NG を押すと、HostPCProgram が `image_inspection_db` を更新し、画像検査Program が結果を読み取る。

```mermaid
sequenceDiagram
    actor Op as オペレーター
    participant IMG as 画像検査Program（C0L-0162）
    participant IDB as image_inspection_db（画像検査専用DB）
    participant App as HostPCProgram（作業指示Program）
    participant T as タブレット

    IMG->>IDB: INSERT image_analysis_sessions<br/>（TabletNo, SerialNo, RaspiNo, OperatorMsg, RaspiExeStatus=WAIT）

    Note over App,IDB: 作業指示Program が一定周期でポーリング

    App->>IDB: GET /api/ImageAnalysisJobs/GetTablet（TabletNo指定）
    IDB-->>App: 作業待ちセッション一覧（WAIT レコード）

    App->>IDB: LOCK（先頭行を IsLocked=1 に更新）

    App-)T: SignalR or 画面表示: 作業指示を表示<br/>（OperatorMsg, SerialNo 等）

    T-->>Op: 指示内容を表示

    Op->>T: OK または NG を選択

    T->>App: PUT /api/ImageAnalysisJobs/PutTabletExeStatus<br/>{ Id, ExeStatus: OK/NG, SerialNo, StartSequenceNo }
    App->>IDB: UPDATE image_analysis_sessions<br/>（RaspiExeStatus = OK / NG）
    App-->>T: 成功レスポンス

    Note over App,IDB: readback確認（最大3回）
    App->>IDB: GET /api/ImageAnalysisJobs/GetCurrentSessionData（Id）
    IDB-->>App: { RaspiExeStatus: "OK" / "NG" }

    IMG->>IDB: SELECT image_analysis_sessions（RaspiExeStatus を確認）
    IDB-->>IMG: { RaspiExeStatus: "OK" / "NG" }

    IMG->>IMG: 結果に応じて次ステップへ
```

---

### 9-3. image_inspection_db の主要テーブル（概要）

`image_inspection_db` は画像検査Program が管理する専用DBです。  
HostPCProgram はこのDBに対して **読み取り・更新のみ** 行い、スキーマ管理は画像検査Program 側が行います。  
詳細は **[08_image_inspection_db.md](08_image_inspection_db.md)** を参照してください。

| テーブル | 主な用途 |
|---------|---------|
| `image_analysis_sessions` | 画像検査ジョブ・作業指示のセッション管理（WAIT/OK/NG） |

HostPCProgram が使用する主なカラム:

| カラム名 | 内容 |
|---------|------|
| `Id` | セッションID（LOCK/OK/NG操作のキー） |
| `TabletNo` | タブレット識別番号 |
| `SerialNo` | マシンシリアル番号 |
| `RaspiNo` | RaspiNo（基板識別） |
| `OperatorMsg` | タブレットに表示する作業指示文字列 |
| `SequenceNo` | シーケンス番号 |
| `StartSequenceNo` | 開始シーケンス番号 |
| `SequenceType` | シーケンス種別 |
| `MachineCode` | マシンコード |
| `RaspiExeStatus` | 実行状態（`WAIT` / `OK` / `NG`） |
| `IsLocked` | LOCK状態（0: 未LOCK, 1: LOCK中） |

---

### 9-4. HostPCProgram に追加する API エンドポイント（画像検査工程向け）

画像検査専用DB との仲介のため、HostPCProgram に以下のエンドポイントを追加します。

| エンドポイント | メソッド | 用途 |
|--------------|---------|------|
| `/api/ImageAnalysisJobs/GetCheck` | GET | 疎通確認（作業指示Program の PingCheck 用） |
| `/api/ImageAnalysisJobs/GetTablet` | GET | 作業待ちセッション一覧取得（TabletNo 指定） |
| `/api/ImageAnalysisJobs/LockById/{id}` | PUT | 先頭行を LOCK（IsLocked=1 に更新） |
| `/api/ImageAnalysisJobs/UnlockById/{id}` | PUT | LOCK 解除（IsLocked=0 に更新） |
| `/api/ImageAnalysisJobs/PutTabletExeStatus` | PUT | OK/NG 結果を反映（RaspiExeStatus 更新） |
| `/api/ImageAnalysisJobs/GetCurrentSessionData/{id}` | GET | readback 確認（RaspiExeStatus 取得） |
| `/api/ImageAnalysisJobs/GetTabletRelationList` | GET | StartSequenceNo 候補一覧取得 |
| `/api/ImageAnalysisJobs/DeclareMasterSlaveRelation` | PUT | RaspiNo ↔ SerialNo 関連宣言 |
| `/api/ImageAnalysisJobs/DeclareChartNoWithSNAndAreaName` | PUT | SerialNo + AreaNo から ChartNo 自動宣言 |
| `/api/ImageAnalysisJobs/DeclareChartNo` | PUT | 手動 ChartNo 宣言 |

---

## API 早見表

### タブレット → HostPCProgram（オペレーター操作）

| エンドポイント | 用途 |
|--------------|------|
| `POST /MachineApi/Enter` | **入室**（オペレーターがシリアル番号を入力） |
| `POST /InstructionApi/Complete` | 作業指示を OK/NG で完了 |

### MiniPC → HostPCProgram

| エンドポイント | 用途 |
|--------------|------|
| `GET /IpApi/Assign?serialNo=` | **IP 採番**（空き IP を割り当て） |
| `GET /ProcessFileApi/Next?serialNo=` | 次に実行する JSON ファイルを問い合わせ |
| `GET /ProcessFileApi/FileContent/{seqId}` | ファイル内容を取得（ハッシュ不一致時） |
| `POST /StepApi/UpdateStep` | Step 開始通知（MANUAL Step の確認トリガー） |
| `POST /StepApi/RecordStep` | Step 完了を記録 |
| `POST /MachineApi/Complete` | 工程完了（OK/NG） |
| `POST /MachineApi/Exit` | 異常退室 |

### HostPCProgram → MiniPC（コールバック・プッシュ型）

| エンドポイント | 用途 |
|--------------|------|
| `POST /api/instructionResult` | 作業指示の OK/NG をプッシュ通知 |

> MiniPC の IP は ゾーン設定またはMiniPC 起動時の登録情報から解決する。

### 作業指示Program → HostPCProgram（画像検査工程専用・暫定）

| エンドポイント | 用途 |
|--------------|------|
| `GET /api/ImageAnalysisJobs/GetCheck` | 疎通確認 |
| `GET /api/ImageAnalysisJobs/GetTablet` | 作業待ちセッション一覧取得 |
| `PUT /api/ImageAnalysisJobs/LockById/{id}` | LOCK |
| `PUT /api/ImageAnalysisJobs/UnlockById/{id}` | UNLOCK |
| `PUT /api/ImageAnalysisJobs/PutTabletExeStatus` | OK/NG 結果反映 |
| `GET /api/ImageAnalysisJobs/GetCurrentSessionData/{id}` | readback 確認 |
| `GET /api/ImageAnalysisJobs/GetTabletRelationList` | StartSequenceNo 候補取得 |
| `PUT /api/ImageAnalysisJobs/DeclareMasterSlaveRelation` | RaspiNo ↔ SerialNo 関連宣言 |
| `PUT /api/ImageAnalysisJobs/DeclareChartNoWithSNAndAreaName` | ChartNo 自動宣言 |
| `PUT /api/ImageAnalysisJobs/DeclareChartNo` | 手動 ChartNo 宣言 |
