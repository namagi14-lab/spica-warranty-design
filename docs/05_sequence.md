# 05 シーケンス図

> ⚠️ 本書は旧アーキテクチャ（HostPCProgram / `prod_process_execution_db` 中心）のシーケンスです。
> 現行方針では MiniPC は **HostPcアプリ（CarrotRape）** 経由で連携し DB へ直接アクセスしません。最新方針: **[12_host_pc_app_pivot.md](12_host_pc_app_pivot.md)**。

---

## 1. 工程実行の全体フロー（正常系）

```mermaid
sequenceDiagram
  participant P  as プリンター
  participant M  as MiniPC<br>(C0L-0161)
  participant H  as HostPC<br>(HostPCProgram)
  participant DB as MySQL
  participant T  as タブレット
  participant D  as Dashboard

  Note over M,H: ① 入庫・工程開始

  M->>H: POST /MachineApi/Enter<br>{serialNumber, cellCode, zoneCode}
  H->>DB: INSERT process_execution (RUNNING)
  H->>DB: INSERT work_instruction_execution × N (PENDING)
  H->>D: SignalR: zoneUpdated
  H-->>M: {success:true, zoneId}

  Note over M,H: ② Step実行ループ

  loop 各Stepごと
    M->>H: POST /MachineApi/UpdateStep {serialNumber, stepKey}
    H->>DB: UPDATE process_execution SET CurrentStepKey
    H-->>M: {success, hasInstruction, zoneId}

    alt 自動Step (hasInstruction=false)
      M->>P: KCFGコマンド送信
      P-->>M: 実行結果
      M->>H: POST /MachineApi/RecordStep {stepKey, result=OK/NG}
      H->>DB: INSERT process_step_execution
      H-->>M: {success:true}

    else 手動Step (hasInstruction=true)
      H->>T: SignalR: 作業指示表示
      T->>作業者: タイトル・テキスト・画像・ボタン表示
      作業者->>T: OK / NG を選択
      T->>H: POST /StepApi/CompleteInstruction
      H->>DB: UPDATE work_instruction_execution (OK/NG)
      H->>D: SignalR: zoneUpdated

      loop ポーリング（~1秒間隔）
        M->>H: GET /MachineApi/InstructionStatus {serialNumber, stepKey}
        H-->>M: {status: "PENDING" or "OK" or "NG"}
      end

      M->>H: POST /MachineApi/RecordStep {stepKey, result}
      H->>DB: INSERT process_step_execution
    end
  end

  Note over M,H: ③ 工程完了

  M->>H: POST /MachineApi/Complete {serialNumber, result=OK}
  H->>DB: UPDATE process_execution SET Status=OK, EndTime
  H->>DB: UPDATE work_instruction_execution PENDING→SKIPPED
  H->>D: SignalR: zoneUpdated
  H-->>M: {success:true}

  M->>H: POST /MachineApi/Exit {serialNumber}
  H-->>M: {success:true}
```

---

## 2. IPアドレス採番フロー

```mermaid
sequenceDiagram
  actor Op as 作業者
  participant T   as タブレット（作業指示Program）
  participant H   as HostPC
  participant DB  as MySQL
  participant M   as MiniPC
  participant P   as プリンター

  Note over Op,P: ソフトインストール工程（IP採番）

  Op->>T: マシンのシリアル番号をスキャン
  T->>H: シリアル番号通知
  H->>DB: ip_numbering から未使用IPを取得
  H->>DB: INSERT ip_numbering {serial, ip, isFinished=0}
  H-->>T: IP採番完了通知
  H->>M: IP設定指示（プッシュ）{serial, ip}
  M->>P: IP設定コマンド送信
  P-->>M: 設定完了
  M-->>H: 完了通知

  Note over Op,DB: 以降の工程ではシリアルから IP を照会
  Op->>T: シリアル番号をスキャン
  T->>H: シリアルから IP を照会
  H->>DB: ip_numbering から対応IPを取得
  H-->>T: { ip }

  Note over H,DB: 保証工程をすべて終えてライン退場時
  H->>DB: UPDATE ip_numbering SET IsFinished=1
```

---

## 3. NG→再作業フロー

```mermaid
sequenceDiagram
  participant M  as MiniPC
  participant H  as HostPC
  participant DB as MySQL

  M->>H: POST /MachineApi/Complete {result=NG}
  H->>DB: UPDATE process_execution SET Status=NG (ExecutionId=10)
  H-->>M: {success:true}

  Note over M,H: 修理・調整後に再入庫

  M->>H: POST /MachineApi/Enter {serialNumber, ...}
  H->>DB: INSERT process_execution<br>{RetryOfExecutionId=10, Status=RUNNING} (ExecutionId=25)
  H->>DB: INSERT work_instruction_execution × N (PENDING)
  H-->>M: {success:true}

  Note over DB: 履歴追跡<br>ExecutionId=25 → RetryOf=10 → RetryOf=NULL
```

---

## 4. 画像検査工程ハンドオフ

通常工程は MiniPC → HostPCProgram の `/ProcessFileApi/Next` 問い合わせで進むが、  
**画像検査工程に入ると MiniPC は HostPCProgram への問い合わせを一時停止し、`host_pc_db` を直接監視する。**

```mermaid
sequenceDiagram
    participant M  as MiniPC<br>(C0L-0161)
    participant H  as HostPCProgram<br>(C0L-0160)
    participant DB as host_pc_db<br>(SQL Server)
    participant I  as 画像検査PC<br>(C0L-0162)
    participant T  as 作業指示Program<br>(C0L-0163)
    actor Op as オペレーター

    Note over M,H: 通常工程ループ中

    M->>H: GET /ProcessFileApi/Next?serialNo=SN123
    H-->>M: { hasNext: true, isImageInspection: true, seqId: 5, ... }

    Note over M: 画像検査モード突入<br>TCP コマンド実行は行わない<br>/Next 問い合わせを一時停止

    Note over I,DB: 画像検査PC が独立して検査を実行<br>Session テーブルの ALLResult / State を更新

    par MiniPC：ALLResult をポーリング
        loop ~1秒間隔
            M->>DB: SELECT ALLResult FROM Session<br>WHERE SerialNo='SN123'
            DB-->>M: ALLResult='WAIT'
        end
    and HostPCProgram：Tablet_Interruptible をポーリング
        loop ~1秒間隔
            H->>DB: SELECT Id, OperatorMsg FROM Session<br>WHERE TabletExeStatus='NA'<br>AND OperatorMsg LIKE '%Tablet_Interruptible%'
        end
    end

    Note over H: Tablet_Interruptible を検知<br>（オペレーター確認が必要な場合のみ発生）

    H->>DB: UPDATE Session SET TabletExeStatus='WAIT'
    H->>T: SignalR: { type: 'imageInspection', sessionId, serialNo, messages }
    T-->>Op: メッセージ・OK/NG ボタン表示
    Op->>T: OK を選択
    T->>H: POST /ImageAnalysisJobsApi/TabletResult { sessionId, result: 'OK' }
    H->>DB: UPDATE Session SET TabletExeStatus='OK'

    Note over I: TabletExeStatus='OK' を検知して検査継続

    I->>DB: UPDATE Session SET ALLResult='OK', State='Next'

    Note over M: ALLResult='OK' を検知<br>→ 画像検査モード終了

    M->>H: GET /ProcessFileApi/Next?serialNo=SN123
    H-->>M: { hasNext: true, isImageInspection: false, ... }

    Note over M,H: 通常工程ループ再開
```

> オペレーター確認（`Tablet_Interruptible`）は画像検査の途中で複数回発生することがある。  
> MiniPC は ALLResult の変化のみを見ており、タブレット連携は HostPCProgram が透過的に処理する。

---

## 5. 異常系：通信切断・再接続

```mermaid
sequenceDiagram
  participant M  as MiniPC
  participant H  as HostPC
  participant DB as MySQL

  Note over M,H: 通信切断発生

  M-xH: POST /MachineApi/UpdateStep → タイムアウト

  Note over M: リトライ処理（指数バックオフ）

  M->>H: POST /MachineApi/UpdateStep （再送）
  H->>DB: SELECT process_execution WHERE MachineSerialNo AND Status=RUNNING

  alt RUNNING が存在する（正常）
    H-->>M: {success:true, hasInstruction, zoneId}
  else RUNNING が存在しない（NGに変更済み）
    H-->>M: {success:false, message: "実行中トランザクションが見つかりません"}
    Note over M: 再度 /Enter から開始
  end
```
