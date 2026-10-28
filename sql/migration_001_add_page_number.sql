-- Migration 001: Add PageNumber column and update Documents table
-- Run this once against your RAGDemo database.
--
-- If the Documents table does not exist yet, run the full CREATE below.
-- If it already exists from the old schema, run only the ALTER.

-- ── Option A: Create from scratch ──────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Documents')
BEGIN
    CREATE TABLE Documents (
        Id         INT IDENTITY(1,1) PRIMARY KEY,
        FileName   NVARCHAR(260)  NOT NULL,
        ChunkIndex INT            NOT NULL,
        PageNumber INT            NOT NULL DEFAULT 0,   -- 0 = unknown (plain text files)
        ChunkText  NVARCHAR(MAX)  NOT NULL,
        Embedding  VECTOR(1536)   NOT NULL,
        UserId     NVARCHAR(128)  NOT NULL DEFAULT 'public'
    );

    CREATE INDEX IX_Documents_UserId   ON Documents (UserId);
    CREATE INDEX IX_Documents_FileName ON Documents (FileName, UserId);

    PRINT 'Documents table created.';
END

-- ── Option B: Add PageNumber to an existing table ──────────────────────────
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Documents') AND name = 'PageNumber')
BEGIN
    ALTER TABLE Documents ADD PageNumber INT NOT NULL DEFAULT 0;
    PRINT 'PageNumber column added to existing Documents table.';
END
ELSE
BEGIN
    PRINT 'Documents table already has PageNumber — no changes made.';
END
