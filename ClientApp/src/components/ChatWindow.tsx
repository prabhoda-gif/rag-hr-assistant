import { useEffect, useRef } from 'react'
import { MessageBubble } from './MessageBubble'
import type { Message } from '../types'

interface ChatWindowProps {
  messages: Message[]
}

export function ChatWindow({ messages }: ChatWindowProps) {
  const bottomRef = useRef<HTMLDivElement>(null)

  // Auto-scroll to bottom on new messages
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  if (messages.length === 0) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center text-center p-8 text-gray-400">
        <div className="w-16 h-16 bg-blue-50 rounded-full flex items-center justify-center mb-4">
          <span className="text-3xl">🤖</span>
        </div>
        <h2 className="text-lg font-semibold text-gray-600 mb-2">HR Assistant Ready</h2>
        <p className="text-sm max-w-sm">
          Upload a PDF policy document above, then ask questions about it.
          I'll answer using only what's in your documents — with page citations.
        </p>
        <div className="mt-6 grid grid-cols-1 gap-2 text-left max-w-sm w-full">
          {[
            'How many days of annual leave do I get?',
            'What is the maternity leave policy?',
            'How do I apply for sick leave?',
          ].map(q => (
            <div key={q}
              className="bg-gray-50 border border-gray-200 rounded-lg px-3 py-2 text-xs text-gray-500">
              💬 "{q}"
            </div>
          ))}
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
      {messages.map(msg => (
        <MessageBubble key={msg.id} message={msg} />
      ))}
      <div ref={bottomRef} />
    </div>
  )
}
