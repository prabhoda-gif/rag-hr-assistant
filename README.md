# RAG HR Assistant

A learning project for building a **Retrieval-Augmented Generation (RAG)** pipeline in C#/.NET 9.

Upload PDF documents, ask questions, and get answers grounded in your own documents with real page citations.

---

## What this project demonstrates

- PDF ingestion with page-level citation tracking
- Two chunking strategies: fixed-size and sentence-aware (switchable via config)
- OpenAI embeddings (`text-embedding-3-small`) stored in SQL Server using the native `VECTOR` type
- Cosine similarity retrieval with configurable top-k and relevance threshold
- Conversational memory with query rewriting for follow-up questions
- Streaming answers via Server-Sent Events (SSE)
- Secure file upload (no path traversal — accepts bytes, not file paths)

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- SQL Server 2025 Preview (or Azure SQL with vector support) — needed for `VECTOR` type
- An OpenAI API key

---

## Setup

### 1. Clone and configure

```bash
git clone <your-repo-url>
cd RagApi
```

Create `appsettings.Development.json` (this file is gitignored — never commit it):

```json
{
  "OpenAI": {
    "ApiKey": "sk-your-key-here"
  },
  "ConnectionStrings": {
    "RAGDemo": "Data Source=localhost;Initial Catalog=RAGDemo;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;"
  }
}
```

### 2. Create the database table

Run [sql/migration_001_add_page_number.sql](sql/migration_001_add_page_number.sql) against your SQL Server instance:

```sql
-- Creates the Documents table with VECTOR(1536) support
-- Run in SSMS or sqlcmd
```

### 3. Run the API

```bash
dotnet run
```

The API starts at `http://localhost:5011`.

---

## Testing

### Browser UI
Open `http://localhost:5011` — upload a PDF and start asking questions.

### Swagger
Open `http://localhost:5011/swagger` — interactive API docs, test every endpoint in the browser.

### HTTP file
Use [RagApi.http](RagApi.http) with VS Code REST Client or JetBrains HTTP Client.

---

## Configuration

All RAG parameters are in `appsettings.json`:

| Key | Default | Description |
|---|---|---|
| `Rag:ChunkingStrategy` | `fixed` | `fixed` or `sentence` |
| `Rag:ChunkSize` | `80` | Words per chunk (fixed) or target words (sentence) |
| `Rag:ChunkOverlap` | `15` | Overlap words (fixed) or overlap sentences (sentence) |
| `Rag:TopK` | `5` | Number of chunks to retrieve |
| `Rag:RelevanceThreshold` | `0.5` | Cosine distance cutoff (lower = more similar) |
| `Rag:BatchSize` | `20` | Chunks per embedding API call |

**Experiment:** Change `ChunkingStrategy` from `fixed` to `sentence`, re-ingest a document, ask the same questions — observe how chunk boundaries affect answer quality.

---

## Architecture

```
PDF Upload → PdfPigParser (page-aware text extraction)
          → IChunkingStrategy (fixed-size or sentence-aware)
          → OpenAI Embeddings (text-embedding-3-small)
          → SQL Server VECTOR storage

Question  → Query rewriting (if follow-up)
          → OpenAI Embeddings
          → VECTOR_DISTANCE cosine search in SQL Server
          → Grounded prompt with page citations
          → GPT-4o-mini (streaming)
          → Answer + page citations
```

---

## Roadmap

- [x] Phase 1: PDF parsing, chunking strategies, page citations, Swagger, secure upload
- [ ] Phase 2: Hybrid retrieval (SQL Full-Text Search + RRF fusion)
- [ ] Phase 3: Evaluation harness (Recall@k, MRR)
- [ ] Phase 4: GenAI features (structured extraction, document summarization, LLM-as-judge)
- [ ] Phase 5: Agentic AI (tool-calling, ReAct multi-step reasoning)
- [ ] Phase 6: Auth (JWT), tenant isolation, rate limiting

---

## Security notes

- The OpenAI API key is read from `appsettings.Development.json` (gitignored) — never from `appsettings.json`
- File upload accepts bytes directly — no file-path parameter (prevents path traversal)
- `UserId` isolation: each user's chunks are stored and retrieved separately
- Stack traces are never returned to API callers

---

## Learning resources

- [OpenAI Embeddings docs](https://platform.openai.com/docs/guides/embeddings)
- [PdfPig library](https://github.com/UglyToad/PdfPig)
- [SQL Server Vector Support](https://learn.microsoft.com/en-us/sql/relational-databases/vectors/vectors-sql-server)
