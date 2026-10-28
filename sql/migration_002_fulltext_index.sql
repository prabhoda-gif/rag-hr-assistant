-- =============================================================================
-- migration_002_fulltext_index.sql
--
-- LEARNING NOTE — What is Full-Text Search?
-- SQL Server's regular WHERE clause can do LIKE '%annual leave%' but it is slow
-- (full table scan) and has no ranking. Full-Text Search builds an inverted
-- index — like a book's index — so keyword lookups are fast and ranked.
--
-- Two steps needed:
--   1. Create a Full-Text Catalog (a container for FT indexes in this DB)
--   2. Create a Full-Text Index on the ChunkText column
--
-- After this, you can use:
--   CONTAINS(ChunkText, '"annual leave"')   -- exact phrase
--   CONTAINS(ChunkText, 'annual AND leave') -- both words
--   FREETEXT(ChunkText, 'annual leave')     -- natural language
-- =============================================================================

-- Step 1: Create a Full-Text Catalog (only needed once per database)
IF NOT EXISTS (
    SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'ft_rag_catalog'
)
BEGIN
    CREATE FULLTEXT CATALOG ft_rag_catalog AS DEFAULT;
    PRINT 'Full-text catalog created.';
END
ELSE
    PRINT 'Full-text catalog already exists — skipped.';

GO

-- Step 2: Create a Full-Text Index on Documents.ChunkText
-- KEY INDEX must reference a unique index on the table (PK_Documents)
IF NOT EXISTS (
    SELECT 1 FROM sys.fulltext_indexes
    WHERE object_id = OBJECT_ID('dbo.Documents')
)
BEGIN
    -- KEY INDEX must be a unique single-column non-nullable index.
    -- PK__Document__* is the system-generated name for the Id primary key.
    -- Find yours with: SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Documents') AND is_unique = 1
    DECLARE @pkName nvarchar(256) = (
        SELECT TOP 1 name FROM sys.indexes
        WHERE object_id = OBJECT_ID('dbo.Documents') AND is_unique = 1
        ORDER BY index_id
    );

    DECLARE @sql nvarchar(1000) = N'
        CREATE FULLTEXT INDEX ON dbo.Documents(ChunkText)
            KEY INDEX ' + QUOTENAME(@pkName) + N'
            ON ft_rag_catalog
            WITH CHANGE_TRACKING AUTO;';

    EXEC sp_executesql @sql;

    PRINT 'Full-text index created on Documents.ChunkText.';
END
ELSE
    PRINT 'Full-text index already exists — skipped.';

GO
