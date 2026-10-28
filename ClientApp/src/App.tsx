import { useState, useCallback } from 'react'
import { Header } from './components/Header'
import { IngestPanel } from './components/IngestPanel'
import { ChatWindow } from './components/ChatWindow'
import { InputBar } from './components/InputBar'
import type { Message, ChunkResult, IngestResponse } from './types'

function makeId() {
  return Math.random().toString(36).slice(2)
}

export default function App() {
  const [userId, setUserId]             = useState('alice')
  const [conversationId, setConversationId] = useState(makeId)
  const [messages, setMessages]         = useState<Message[]>([])
  const [isLoading, setIsLoading]       = useState(false)

  // Append or update a message by id
  function upsertMessage(msg: Message) {
    setMessages(prev => {
      const idx = prev.findIndex(m => m.id === msg.id)
      if (idx === -1) return [...prev, msg]
      const next = [...prev]
      next[idx] = msg
      return next
    })
  }

  async function sendQuestion(question: string) {
    if (isLoading) return

    // Add user message immediately
    upsertMessage({ id: makeId(), role: 'user', content: question })

    // Add a streaming placeholder for the assistant
    const assistantId = makeId()
    upsertMessage({ id: assistantId, role: 'assistant', content: '', isStreaming: true })

    setIsLoading(true)

    try {
      const resp = await fetch('/api/ask/stream', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, userId, conversationId }),
      })

      if (!resp.ok || !resp.body) throw new Error('Stream request failed')

      const reader  = resp.body.getReader()
      const decoder = new TextDecoder()
      let   buffer  = ''
      let   answer  = ''
      let   sources: ChunkResult[] = []

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''

        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          const data = line.slice(6)

          if (data === '[DONE]') continue

          // Sources frame arrives as a JSON array
          if (data.startsWith('[')) {
            try { sources = JSON.parse(data) } catch {}
            continue
          }

          answer += data
          upsertMessage({
            id: assistantId,
            role: 'assistant',
            content: answer,
            isStreaming: true,
          })
        }
      }

      // Finalize — remove streaming cursor, attach sources
      upsertMessage({
        id: assistantId,
        role: 'assistant',
        content: answer || 'No response received.',
        sources,
        isStreaming: false,
      })
    } catch (err) {
      upsertMessage({
        id: assistantId,
        role: 'assistant',
        content: `❌ Error: ${err instanceof Error ? err.message : 'Unknown error'}`,
        isStreaming: false,
      })
    } finally {
      setIsLoading(false)
    }
  }

  const handleNewChat = useCallback(async () => {
    // Clear server-side history
    await fetch(`/api/ask/${conversationId}`, { method: 'DELETE' })
    setConversationId(makeId())
    setMessages([])
  }, [conversationId])

  function handleIngested(info: IngestResponse) {
    upsertMessage({
      id: makeId(),
      role: 'assistant',
      content: `✅ Ingested **${info.fileName}**\n${info.chunks} chunks created in ${info.timeTaken}\nReady to answer questions about this document.`,
    })
  }

  return (
    <div className="flex flex-col h-screen bg-gray-50">
      <Header
        userId={userId}
        onUserIdChange={setUserId}
        onNewChat={handleNewChat}
      />
      <IngestPanel userId={userId} onIngested={handleIngested} />
      <ChatWindow messages={messages} />
      <InputBar onSend={sendQuestion} isLoading={isLoading} />
    </div>
  )
}
