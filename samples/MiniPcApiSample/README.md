# MiniPC API サンプル（C0L-0161 保証工程制御Program）

MiniPC（保証工程制御Program）の **API 通信部分** の C# サンプルです。
設計書 [`docs/`](../../docs/) の仕様に沿って、以下の 3 つを最小構成で示します。

| 役割 | 内容 | 実装場所 |
|------|------|----------|
| **API を立てる（サーバー）** | OWIN セルフホストで HTTP サーバーを起動 | `Startup.cs` |
| **コールバックを受け取る** | `POST /api/instructionResult`（HostPC からのプッシュ通知） | `Server/Controllers/InstructionResultController.cs` |
| **API を叩く（クライアント）** | HostPC の各 API を呼ぶ HttpClient | `Client/HostPcApiClient.cs` |

> **注意**: プリンターへの KCFG コマンド送信や工程JSONの解析など「中の業務処理」は
> 別担当が実装する想定で、`TODO(別担当)` のスタブにしています。
> 本サンプルは API の **配線（立てる / 受け取る / 叩く）** に集中しています。

---

## 動作環境

- Windows
- .NET Framework 4.8（`net48`）
- Visual Studio 2022 もしくは `dotnet` CLI / `msbuild`

## ビルド

```powershell
# ソリューションのあるフォルダで
dotnet restore .\MiniPcApiSample.sln
dotnet build   .\MiniPcApiSample.sln -c Debug
```

※ `dotnet` を使う場合は .NET Framework 4.8 の Developer Pack が必要です。
   Visual Studio で `MiniPcApiSample.sln` を開いてビルド/実行しても構いません。

## 設定（`src/MiniPcApiSample/App.config`）

| キー | 説明 |
|------|------|
| `HostPcBaseUrl` | HostPC（C0L-0160）のベースURL。叩く先。 |
| `ListenUrl` | コールバック受信サーバーの待ち受けURL。 |
| `SerialNo` | 指定すると起動時に工程を 1 回流すデモを実行。空ならサーバー待ち受けのみ。 |
| `LocalFileDir` | 工程JSONファイルのローカル保存先。 |

`localhost` 以外（LAN 内の HostPC から受ける場合）は、管理者権限で URL 予約が必要です。

```powershell
netsh http add urlacl url=http://+:8080/ user=Everyone
```

その上で `ListenUrl` を `http://+:8080/` に設定してください。

## 実行

```powershell
dotnet run --project .\src\MiniPcApiSample\MiniPcApiSample.csproj
```

起動後のエンドポイント:

- `POST http://localhost:8080/api/instructionResult` … コールバック受信
- `GET  http://localhost:8080/api/ping` … 疎通確認

### コールバックの動作確認（curl）

```bash
curl -X POST http://localhost:8080/api/instructionResult \
  -H "Content-Type: application/json" \
  -d '{ "serialNo": "C0L0000001", "stepKey": 5, "result": "OK" }'
# => { "success": true }
```

---

## 全体フローと本サンプルの対応

```
① IP採番        GET  /IpApi/Assign                 … HostPcApiClient.AssignIpAsync
③ 工程ファイル   GET  /ProcessFileApi/Next          … GetNextProcessFileAsync
                GET  /ProcessFileApi/FileContent   … GetProcessFileContentAsync
   各Step       POST /StepApi/UpdateStep           … UpdateStepAsync
                （MANUAL Step）
                  └ HostPC → POST /api/instructionResult … InstructionResultController（受信）
                                                          → InstructionResultCoordinator（待機ループへ通知）
                POST /StepApi/RecordStep           … RecordStepAsync
④ 工程完了      POST /MachineApi/Complete          … CompleteAsync
   異常退室      POST /MachineApi/Exit              … ExitAsync
```

`MANUAL Step` のプッシュ型コールバックは、`ProcessRunner` が `InstructionResultCoordinator.WaitForResultAsync`
で待機し、受信コントローラが `Publish` で結果を渡す——という形で「立てる」「受け取る」「叩く」を連結しています。

## プロジェクト構成

```
samples/MiniPcApiSample/
├── MiniPcApiSample.sln
├── README.md
└── src/MiniPcApiSample/
    ├── MiniPcApiSample.csproj
    ├── App.config
    ├── Program.cs                    エントリ（サーバー起動 + デモ実行）
    ├── Startup.cs                    OWIN / Web API 構成
    ├── Configuration/
    │   └── MiniPcOptions.cs
    ├── Server/                       ── API を立てる / 受け取る ──
    │   ├── InstructionResultCoordinator.cs   受信⇄実行ループの橋渡し
    │   └── Controllers/
    │       ├── InstructionResultController.cs  POST /api/instructionResult
    │       └── PingController.cs                GET  /api/ping
    ├── Client/                       ── API を叩く ──
    │   ├── IHostPcApiClient.cs
    │   └── HostPcApiClient.cs
    ├── Workflow/
    │   └── ProcessRunner.cs          工程実行ループ（業務処理はTODOスタブ）
    └── Models/                       リクエスト/レスポンス DTO
```

## 関連ドキュメント

- [`docs/04_api_spec.md`](../../docs/04_api_spec.md) — API 仕様
- [`docs/06_process_file_api.md`](../../docs/06_process_file_api.md) — 工程JSONファイル API
- [`docs/07_system_design.md`](../../docs/07_system_design.md) — システム設計（フロー図・API早見表）
