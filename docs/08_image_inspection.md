# 08 画像検査フロー（旧機種 RasPi 調査結果）

> **本ドキュメントの目的**  
> 旧機種で使用していた画像検査プログラム（`RasPi_Main` / C#）を調査し、Spica 向け作業指示 Program に同等機能を組み込む際の参考情報として整理する。

---

## 1. 旧機種システム構成

| コンポーネント | 旧機種（RasPi） | 新機種（Spica） |
|--------------|----------------|----------------|
| 制御ユニット   | Raspberry Pi | MiniPC（C0L-0161） |
| サーバー      | HostPC（WebAPI） | HostPCProgram |
| 機器通信      | TCP/IP | TCP/IP（同様） |
| 画像処理      | `ForImage.cs`（8,500行） | **未実装 → 今後の対象** |
| 外部ライブラリ | HalfTone.dll、HOST_Communication.dll | TBD |
| ストレージ    | RAMDISK（`/mnt/ramdisk/`）＋SSD | TBD |

---

## 2. 旧機種プログラム全体フロー

### 2.1 起動シーケンス（3フェーズ）

```mermaid
flowchart TD
    START([START]) --> P1

    subgraph P1["フェーズ1: RasPi 本体初期化"]
        P1A[OS種別・パス設定\nClass_Set] --> P1B
        P1B[HostPC URL 読み込み] --> P1C
        P1C[RAMDISKを作成・初期化] --> P1D
        P1D[プログラム一式を RAMDISK へコピー] --> P1E
        P1E[ローカルログフォルダ作成] --> P1F
        P1F[時刻同期・クリーンアップ実行]
    end

    subgraph P2["フェーズ2: 機器通信セットアップ"]
        P2A[ソフトウェアデーモン起動] --> P2B
        P2B[RasPi ホスト名取得・ID登録] --> P2C
        P2C[セッションID・旧ログファイル削除]
    end

    subgraph P3["フェーズ3: HostPC 通信セットアップ"]
        P3A[RasPi ネットワーク情報をアップロード] --> P3B
        P3B[機器シリアル番号取得\nGetSerialNo / GetSerialNoFromMainBoard\nGetMachineCode] --> P3C
        P3C{シリアル番号\n検証 OK?}
        P3C -- NG --> P3ERR[タブレットにエラー表示\nライン停止]
        P3C -- OK --> P3D
        P3D[SessionID を HostPC から取得] --> P3E
        P3E[次の Job JSON を取得\nGet_NextJson] --> P3F
        P3F[シーケンス番号取得\nGet_SequenceNo]
    end

    P1 --> P2 --> P3 --> LOOP([メインループへ])
    P3ERR --> END2([終了])
```

### 2.2 メインループ（3ステップを繰り返す）

```mermaid
flowchart TD
    LOOP([メインループ開始]) --> RESET[AllResult = NG にリセット]
    RESET --> CHECK_SESSION{SessionID\nファイル存在?}
    CHECK_SESSION -- なし --> REISSUE[SessionID 再発行]
    REISSUE --> S1
    CHECK_SESSION -- あり --> S1

    subgraph S1["ステップ1: 機器通信 （Json_Analysis.RasPi_Machine_Comm）"]
        S1A[NextJson ファイル確認] --> S1B
        S1B[ログファイル作成] --> S1C
        S1C[JSON ファイル全読み込み] --> S1D
        S1D[JSON を JigDataset 配列にデシリアライズ] --> S1E
        S1E{各ステップを\nTCP 送信・検証}
        S1E -- NG発生 --> S1NG[ループ中断\nAllResult=NG]
        S1E -- 全ステップ OK --> S1OK[AllResult=OK]
    end

    S1NG --> S2
    S1OK --> S2

    subgraph S2["ステップ2: 画像検査 （ForImage.CallFromRaspi）"]
        S2A[HostPC からセッション情報取得] --> S2B
        S2B[シーケンス種別・ステート確認] --> S2C
        S2C{検査種別}
        S2C --> S2D["用紙トレイ確認\nCheckPaperForPaperFeedTray"]
        S2C --> S2E["ハーフトーンパターン解析\nHalfTone.dll"]
        S2C --> S2F["チャート認識\nChartA4 / HalfTone"]
        S2C --> S2G["画像 FTP 取得\nGet_jpg / Send_jpg"]
        S2D & S2E & S2F & S2G --> S2JUDGE{判定}
        S2JUDGE -- NG --> S2ERR[FinalErrorShoriForSession\nエラーファイル生成]
        S2JUDGE -- OK --> S2OK2[画像結果を保持]
    end

    S2ERR --> S3
    S2OK2 --> S3

    subgraph S3["ステップ3: HostPC へ結果送信 （Host_Communication.RasPi_Host_Comm）"]
        S3A["全体結果を POST\nRasPiPostToHostPC_AllResult"] --> S3B
        S3B["次の Job JSON を取得\nCheckAndGetNextJob"] --> S3C
        S3C[シーケンス番号更新] --> S3D{SessionID\n削除フラグ?}
        S3D -- 削除 → 全工程完了 --> S3FIN[SessionID ファイル削除\nループ終了]
        S3D -- 継続 --> LOOP2{AllResult?}
    end

    LOOP2 -- NG --> BREAK([ループ中断・エラー終了])
    LOOP2 -- OK --> LOOP

    S3FIN --> END([正常終了])
```

---

## 3. 画像検査フロー詳細（`ForImage.CallFromRaspi`）

```mermaid
sequenceDiagram
    participant M  as 製品マシン
    participant R  as RasPi
    participant H  as HostPC
    participant FTP as FTP サーバー
    participant T  as タブレット

    Note over R,H: 画像検査フェーズ開始

    R->>H: セッションデータ取得\nGetCurrentSessionData()
    H-->>R: { SequenceNo, SequenceTYPE, State, Destination }

    alt SequenceTYPE = 用紙トレイ確認
        R->>M: 用紙センサー確認コマンド（TCP）
        M-->>R: センサー応答
        R->>R: CheckPaperForPaperFeedTray()\n画像解析で用紙有無・位置を検証
        R-->>T: 確認ダイアログ表示（タブレット有りの場合）
        T-->>R: オペレーター確認 OK/NG

    else SequenceTYPE = ハーフトーン検査
        R->>M: 印刷コマンド送信（TCP）
        M-->>R: 印刷完了通知
        R->>FTP: 印刷画像を取得（Get_jpg）
        FTP-->>R: 画像ファイル（RAMDISK へ保存）
        R->>R: HalfTone.dll でパターン解析
        R->>H: 解析結果画像をアップロード（Send_jpg）

    else SequenceTYPE = チャート認識（A4 / HalfTone）
        R->>M: チャート印刷コマンド（TCP）
        M-->>R: 完了
        R->>FTP: チャート画像を取得（Get_jpg）
        FTP-->>R: 画像ファイル
        R->>R: ChartA4 / HalfTone テンプレートマッチング
        R->>H: 認識結果をアップロード

    else SequenceTYPE = シリアル番号取得
        R->>M: DINQ 1297（サブ基板シリアル取得）
        M-->>R: シリアル番号
        R->>M: DINQ 0201（メイン基板シリアル取得）
        M-->>R: シリアル番号
        R->>R: 2つのシリアル番号を照合
    end

    alt 検査 OK
        R->>H: 結果 JSON 送信（PostRaspiData）
        H-->>R: 次のシーケンス情報
    else 検査 NG
        R->>R: FinalErrorShoriForSession()\nエラーファイル生成・ログ記録
        R->>H: NG 結果を送信
        R-->>T: エラー内容表示（タブレット有りの場合）
    end
```

---

## 4. 画像検査種別一覧

| 種別 | コマンドタイプ | 概要 | 使用ライブラリ |
|------|-------------|------|-------------|
| 用紙トレイ確認 | `CheckPaper` | 給紙トレイの用紙有無・位置を画像で検証 | 内製ロジック |
| ハーフトーン検査 | `HalfTone` | 印刷物のハーフトーンパターンを解析 | `HalfTone.dll` |
| チャート認識 A4 | `ChartA4` | A4 チャートのテンプレートマッチング | `HalfTone.dll` |
| 画像取得 | `Get_jpg` | 機器の FTP サーバーから画像を取得 | 内製 FTP |
| 画像送信 | `Send_jpg` | 解析済み画像を HostPC へアップロード | 内製 FTP |
| シリアル取得 | `SNRE_type1/2` | 基板からシリアル番号を TCP で読み取り | TCP 通信 |

---

## 5. TCP コマンド実行フロー（各ステップ共通）

```mermaid
flowchart TD
    START([コマンド実行開始]) --> LOOP_START

    subgraph RETRY_LOOP["リトライループ（最大 Retry 回）"]
        LOOP_START[TCP 接続確立\nTCPCreate / TCPConnect]
        LOOP_START --> SEND[コマンド送信\nPrefix + Body + Suffix]
        SEND --> RECV[レスポンス受信]
        RECV --> V1{Verify_1 を含む?}
        V1 -- いいえ --> FAIL
        V1 -- はい --> V2{Verify_2 を含む?}
        V2 -- いいえ --> FAIL
        V2 -- はい --> OK[TCP 切断\nResult = OK]
        FAIL[TCP 切断\nResult = NG] --> SLEEP[RetryTimer ミリ秒 待機]
        SLEEP --> RETRY{リトライ\n残あり?}
        RETRY -- あり --> LOOP_START
        RETRY -- なし --> FINAL_NG[最終 NG]
    end

    OK --> END_OK([ステップ OK])
    FINAL_NG --> END_NG([ステップ NG → ループ中断])
```

---

## 6. ファイル・データ構造

### 6.1 Job JSON（HostPC → RasPi）

HostPC から RAMDISK へ配信される工程定義。各ステップの送信コマンドと検証条件を含む。

```json
[
  {
    "Step_key": 1,
    "Command_Control": {
      "Com_type": "SendReceiveJudge",
      "Comment": "用紙センサー確認",
      "Verify_1": "OK",
      "Verify_2": "",
      "Retry": 3,
      "RetryTimer": 500
    },
    "Command_Body": {
      "Prefix": "CMD",
      "Body": "CHECK_PAPER",
      "Suffix": "\r\n"
    },
    "Command_return": {
      "Result": "",
      "Ref": "",
      "Excomment": ""
    }
  }
]
```

### 6.2 結果ログファイル（RasPi ローカル保存）

```
/home/pi/log/YYYYMMDD/
  └── {MachineSerial}_{Timestamp}_SeqNo.{N}_{OK|NG}.json
```

### 6.3 セッション管理

```
/home/pi/SessionID/SessionID.txt   … 現在の SessionID
/mnt/ramdisk/Athena_debug/NextJson/test.json … 次に実行する Job JSON
```

---

## 7. Spica 向け実装検討ポイント

旧機種 RasPi のフローを参考に、Spica（MiniPC）向けに移植する際の検討項目。

| 項目 | 旧機種（RasPi） | Spica 向け検討 |
|------|--------------|--------------|
| 画像処理ライブラリ | `HalfTone.dll`（独自） | 同 DLL を流用するか OpenCV 等に変更するか確認が必要 |
| 画像保存先 | RAMDISK（`/mnt/ramdisk/`） | Windows なら TEMP フォルダ等に変更 |
| FTP アクセス | 機器内蔵 FTP サーバーへ直接接続 | Spica でも機器 FTP が使えるか確認 |
| タブレット連携 | `ForImage.PutGetWaitTablet()` | SignalR（プッシュ型）に変更（`05_sequence.md` 参照） |
| シリアル取得 | TCP（DINQ コマンド） | Spica でも同コマンドが使えるか確認 |
| HostPC への画像送信 | `Send_jpg`（独自プロトコル） | HostPCProgram の API エンドポイントとして設計 |
| 工程スキップ機能 | `CheckProcessSkip`（再作業対応） | `RetryOfExecutionId` で代替可能か検討 |

---

## 8. 未確認事項（要調査）

- [ ] Spica 機種の画像検査種別・内容（用紙トレイ・ハーフトーン等は同じか？）
- [ ] `HalfTone.dll` が Spica の環境（Windows / .NET バージョン）で動作するか
- [ ] 機器内蔵 FTP サーバーの仕様（Spica でも同様か）
- [ ] 画像取得のタイミング（印刷完了通知の方法）
- [ ] 画像の HostPC 保存要件（DB 格納 or ファイルサーバー）
- [ ] 作業指示 Program での画像検査 Step の JSON 定義形式
