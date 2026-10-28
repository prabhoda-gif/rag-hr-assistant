import { BotMessageSquare, PlusCircle } from 'lucide-react'

interface HeaderProps {
  userId: string
  onUserIdChange: (id: string) => void
  onNewChat: () => void
}

export function Header({ userId, onUserIdChange, onNewChat }: HeaderProps) {
  return (
    <header className="bg-blue-600 text-white px-6 py-3 flex items-center justify-between shadow-md">
      <div className="flex items-center gap-3">
        <BotMessageSquare className="w-6 h-6" />
        <div>
          <h1 className="font-semibold text-base leading-tight">RAG HR Assistant</h1>
          <p className="text-blue-200 text-xs">Powered by OpenAI + SQL Server Vector Search</p>
        </div>
      </div>

      <div className="flex items-center gap-3">
        {/* User ID input */}
        <div className="flex items-center gap-2 bg-blue-700 rounded-full px-3 py-1.5 text-sm">
          <span className="text-blue-200 text-xs">User:</span>
          <input
            type="text"
            value={userId}
            onChange={e => onUserIdChange(e.target.value)}
            className="bg-transparent text-white placeholder-blue-300 outline-none w-24 text-sm"
            placeholder="user-id"
          />
        </div>

        {/* New chat button */}
        <button
          onClick={onNewChat}
          className="flex items-center gap-1.5 bg-blue-700 hover:bg-blue-800
                     transition-colors rounded-full px-3 py-1.5 text-sm font-medium"
        >
          <PlusCircle className="w-4 h-4" />
          New Chat
        </button>
      </div>
    </header>
  )
}
