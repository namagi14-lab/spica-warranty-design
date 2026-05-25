# Spica 保証工程 システム設計書

Spica（プリンター）保証工程の**設計書・仕様書**をまとめたリポジトリです。

## ドキュメント一覧

| ファイル | 内容 |
|---------|------|
| [docs/01_system_overview.md](docs/01_system_overview.md) | システム全体構成・概要 |
| [docs/02_db_schema.md](docs/02_db_schema.md) | DBテーブル定義・設計方針 |
| [docs/03_er_diagram.md](docs/03_er_diagram.md) | ER図（Mermaid） |
| [docs/04_api_spec.md](docs/04_api_spec.md) | API仕様（システム間連携） |
| [docs/05_sequence.md](docs/05_sequence.md) | シーケンス図 |
| [SQL/schema.sql](SQL/schema.sql) | 完全DDL（CREATE TABLE） |

## 関連リポジトリ

| リポジトリ | 役割 |
|-----------|------|
| [WorkInstructionApp](https://github.com/namagi14-lab/WorkInstructionApp) | 作業指示Program / HostPC Program |
| [ProcessDashboard](https://github.com/namagi14-lab/ProcessDashboard) | ダッシュボードProgram |

## システム構成サマリー

```
[プリンター本体]
    ↕ KCFGコマンド (LAN)
[MiniPC = 保証工程制御Program (C0L-0161)]
    ↕ WebAPI (HTTP)
[HostPC = WorkInstructionApp (C0L-0160/0163)]
    ├─ SQL ──────→ [MySQL: prod_process_execution_db]
    ├─ SignalR ──→ [Dashboard (C0L-0164)]
    ├─ HTML ────→ [タブレット] → 作業者
    └─ XXXX ────→ [画像処理PC (C0L-0162)]
```

## 保証工程の3ゾーン

| ゾーン | 内容 |
|--------|------|
| Soft Install / 電気check | Softインストール・電気系統確認 |
| 画像検査 | 実機スキャナによる画像検査 |
| 出荷設定 | 仕向地設定（言語・紙サイズ・エリア等） |
