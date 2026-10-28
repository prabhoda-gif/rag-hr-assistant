import { BotMessageSquare, User } from 'lucide-react'
import { SourceCitations } from './SourceCitations'
import type { Message } from '../types'

interface MessageBubbleProps {
  message: Message
}

export function MessageBubble({ message }: MessageBubbleProps) {
  const isUser = message.role === 'user'

  return (
    <div className={`flex gap-3 ${isUser ? 'flex-row-reverse' : 'flex-row'}`}>
      {/* Avatar */}
      <div className={`shrink-0 w-8 h-8 rounded-full flex items-center justify-center
                       ${isUser ? 'bg-blue-600' : 'bg-gray-200'}`}>
        {isUser
          ? <User className="w-4 h-4 text-white" />
          : <BotMessageSquare className="w-4 h-4 text-gray-600" />
        }
      </div>

      {/* Bubble */}
      <div className={`max-w-[75%] rounded-2xl px-4 py-3 text-sm leading-relaxed
                       ${isUser
                         ? 'bg-blue-600 text-white rounded-tr-sm'
                         : 'bg-white text-gray-800 rounded-tl-sm shadow-sm border border-gray-100'}`}>
        {/* Content */}
        <p className="whitespace-pre-wrap">
          {message.content}
          {message.isStreaming && (
            <span className="inline-block w-1.5 h-4 bg-gray-400 ml-0.5 animate-pulse rounded-sm align-middle" />
          )}
        </p>

        {/* Sources — only for assistant messages */}
        {!isUser && message.sources && message.sources.length > 0 && (
          <SourceCitations sources={message.sources} />
        )}
      </div>
    </div>
  )
}
