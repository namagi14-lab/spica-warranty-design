-- ============================================================
-- Spica 保証工程DB 完全DDL
-- DB名: prod_process_execution_db
-- RDBMS: MySQL  charset: utf8mb4
-- ============================================================

CREATE DATABASE IF NOT EXISTS prod_process_execution_db
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_unicode_ci;

USE prod_process_execution_db;

-- ============================================================
-- マスターテーブル
-- ============================================================

-- 工程マスタ
CREATE TABLE IF NOT EXISTS process_master (
  ProcessId    INT          NOT NULL AUTO_INCREMENT,
  ProcessCode  VARCHAR(50)  NOT NULL COMMENT '工程コード（例: A1）',
  ProcessName  VARCHAR(100) NOT NULL,
  Description  TEXT,
  IsActive     TINYINT(1)   NOT NULL DEFAULT 1,
  CreatedAt    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (ProcessId),
  UNIQUE KEY uq_process_code (ProcessCode)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- セル管理（1セル = 1工程）
CREATE TABLE IF NOT EXISTS cells (
  CellId    INT          NOT NULL AUTO_INCREMENT,
  CellCode  VARCHAR(50)  NOT NULL COMMENT '識別コード（例: LINE-A）',
  CellName  VARCHAR(100) NOT NULL,
  ProcessId INT          NOT NULL COMMENT 'FK: process_master',
  GridRows  INT          NOT NULL DEFAULT 2 COMMENT 'ダッシュボードグリッド行数',
  GridCols  INT          NOT NULL DEFAULT 2 COMMENT 'ダッシュボードグリッド列数',
  IsActive  TINYINT(1)   NOT NULL DEFAULT 1,
  CreatedAt DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (CellId),
  UNIQUE KEY uq_cell_code (CellCode),
  CONSTRAINT fk_cells_process FOREIGN KEY (ProcessId) REFERENCES process_master (ProcessId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ゾーン管理（セル内の物理位置・ダッシュボードレイアウト）
CREATE TABLE IF NOT EXISTS zones (
  ZoneId      INT          NOT NULL AUTO_INCREMENT,
  CellId      INT          NOT NULL COMMENT 'FK: cells',
  ZoneCode    VARCHAR(50)  NOT NULL COMMENT '識別コード（例: ZONE-A1）',
  ZoneName    VARCHAR(100) NOT NULL,
  GridRow     INT          NOT NULL DEFAULT 1,
  GridRowSpan INT          NOT NULL DEFAULT 1,
  GridCol     INT          NOT NULL DEFAULT 1,
  GridColSpan INT          NOT NULL DEFAULT 1,
  IsActive    TINYINT(1)   NOT NULL DEFAULT 1,
  CreatedAt   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (ZoneId),
  UNIQUE KEY uq_zone_in_cell (CellId, ZoneCode),
  CONSTRAINT fk_zones_cell FOREIGN KEY (CellId) REFERENCES cells (CellId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 工程JSON定義（バージョン管理）
CREATE TABLE IF NOT EXISTS process_definition (
  ProcessDefId      INT          NOT NULL AUTO_INCREMENT,
  ProcessId         INT          NOT NULL COMMENT 'FK: process_master',
  DefinitionName    VARCHAR(100) NOT NULL,
  DefinitionVersion VARCHAR(50)  NOT NULL,
  DefinitionHash    CHAR(64)     NOT NULL COMMENT 'JSONのSHA-256ハッシュ（重複登録防止）',
  DefinitionJson    JSON         NOT NULL,
  CreatedAt         DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (ProcessDefId),
  UNIQUE KEY uq_definition_hash (DefinitionHash),
  CONSTRAINT fk_procdef_process FOREIGN KEY (ProcessId) REFERENCES process_master (ProcessId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 作業指示マスタ（手動Stepのタブレット表示コンテンツ）
-- TargetStepKey NULL = 工程開始時（入庫直後）に表示
CREATE TABLE IF NOT EXISTS work_instruction_master (
  InstructionId          INT          NOT NULL AUTO_INCREMENT,
  ProcessId              INT          NOT NULL COMMENT 'FK: process_master',
  InstructionName        VARCHAR(200) NOT NULL COMMENT '手順タイトル',
  InstructionDescription TEXT                  COMMENT '説明テキスト',
  ImagePath              VARCHAR(500)           COMMENT '画像パス（ファイルサーバー）',
  FormType               ENUM('OK_ONLY','OK_NG') NOT NULL DEFAULT 'OK_ONLY',
  TargetStepKey          INT                    COMMENT '対象StepKey（NULL=入庫時表示）',
  IsActive               TINYINT(1)   NOT NULL DEFAULT 1,
  CreatedAt              DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt              DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (InstructionId),
  CONSTRAINT fk_wim_process FOREIGN KEY (ProcessId) REFERENCES process_master (ProcessId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ============================================================
-- トランザクションテーブル
-- ============================================================

-- 工程実行（1マシン × 1工程 = 1レコード）
CREATE TABLE IF NOT EXISTS process_execution (
  ExecutionId         BIGINT   NOT NULL AUTO_INCREMENT,
  MachineSerialNo     VARCHAR(20) NOT NULL COMMENT '製品シリアル番号',
  ZoneId              INT              COMMENT 'FK: zones',
  ProcessDefId        INT      NOT NULL COMMENT 'FK: process_definition',
  RetryOfExecutionId  BIGINT           COMMENT '再作業元ExecutionId（初回はNULL）',
  CurrentStepKey      INT              COMMENT '現在到達しているStepKey',
  StartTime           DATETIME NOT NULL,
  EndTime             DATETIME,
  Status              ENUM('RUNNING','OK','NG','ABORT') NOT NULL DEFAULT 'RUNNING',
  PRIMARY KEY (ExecutionId),
  KEY idx_serial  (MachineSerialNo),
  KEY idx_zone_status (ZoneId, Status),
  CONSTRAINT fk_exec_procdef  FOREIGN KEY (ProcessDefId)       REFERENCES process_definition (ProcessDefId),
  CONSTRAINT fk_exec_zone     FOREIGN KEY (ZoneId)             REFERENCES zones (ZoneId),
  CONSTRAINT fk_exec_retry    FOREIGN KEY (RetryOfExecutionId) REFERENCES process_execution (ExecutionId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Stepごとの実行結果
CREATE TABLE IF NOT EXISTS process_step_execution (
  StepExecId    BIGINT   NOT NULL AUTO_INCREMENT,
  ExecutionId   BIGINT   NOT NULL COMMENT 'FK: process_execution',
  StepKey       INT      NOT NULL,
  ResultStatus  ENUM('OK','NG') NOT NULL,
  ExecTime      DATETIME NOT NULL,
  Note          TEXT              COMMENT 'イレギュラー対応用メモ',
  PRIMARY KEY (StepExecId),
  KEY idx_execution (ExecutionId),
  CONSTRAINT fk_stepexec_exec FOREIGN KEY (ExecutionId) REFERENCES process_execution (ExecutionId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 作業指示の実行状態（工程開始時にPENDINGで一括生成）
CREATE TABLE IF NOT EXISTS work_instruction_execution (
  InstructionExecId BIGINT   NOT NULL AUTO_INCREMENT,
  ExecutionId       BIGINT   NOT NULL COMMENT 'FK: process_execution',
  InstructionId     INT      NOT NULL COMMENT 'FK: work_instruction_master',
  ResultStatus      ENUM('PENDING','OK','NG','SKIPPED') NOT NULL DEFAULT 'PENDING',
  ExecutedBy        VARCHAR(100)       COMMENT '完了した作業者名',
  ExecutedAt        DATETIME           COMMENT '完了日時',
  PRIMARY KEY (InstructionExecId),
  KEY idx_exec_status (ExecutionId, ResultStatus),
  CONSTRAINT fk_wie_exec FOREIGN KEY (ExecutionId)  REFERENCES process_execution      (ExecutionId),
  CONSTRAINT fk_wie_instr FOREIGN KEY (InstructionId) REFERENCES work_instruction_master (InstructionId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- IPアドレス採番管理
CREATE TABLE IF NOT EXISTS ip_numbering (
  Id             INT         NOT NULL AUTO_INCREMENT,
  MachineSerial  VARCHAR(20) NOT NULL COMMENT 'マシンシリアル番号',
  IpAddress      VARCHAR(15) NOT NULL COMMENT '割り当てIPアドレス',
  IsFinished     TINYINT(1)  NOT NULL DEFAULT 0 COMMENT '0:使用中, 1:工程完了',
  AssignedAt     DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (Id),
  UNIQUE KEY uq_ip_address (IpAddress),
  KEY idx_machine_serial (MachineSerial)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
