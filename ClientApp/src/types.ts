export interface ChunkResult {
  fileName: string
  chunkIndex: number
  pageNumber: number
  chunkText: string
  distance: number
}

export interface AskResponse {
  answer: string
  sources: ChunkResult[]
  conversationId: string
}

export interface IngestResponse {
  fileName: string
  chunks: number
  timeTaken: string
  userId: string
}

export interface Message {
  id: string
  role: 'user' | 'assistant'
  content: string
  sources?: ChunkResult[]
  isStreaming?: boolean
}
