import { useState } from 'react'
import { SendHorizontal, Loader2 } from 'lucide-react'

interface InputBarProps {
  onSend: (question: string) => void
  isLoading: boolean
}

export function InputBar({ onSend, isLoading }: InputBarProps) {
  const [value, setValue] = useState('')

  function handleSubmit(e?: React.FormEvent) {
    e?.preventDefault()
    const q = value.trim()
    if (!q || isLoading) return
    onSend(q)
    setValue('')
  }

  return (
    <form
      onSubmit={handleSubmit}
      className="border-t border-gray-200 bg-white px-6 py-4 flex gap-3 items-end"
    >
      <textarea
        value={value}
        onChange={e => setValue(e.target.value)}
        onKeyDown={e => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault()
            handleSubmit()
          }
        }}
        placeholder="Ask a question about your HR documents… (Enter to send, Shift+Enter for new line)"
        rows={1}
        disabled={isLoading}
        className="flex-1 resize-none rounded-2xl border border-gray-300 px-4 py-2.5 text-sm
                   outline-none focus:border-blue-400 focus:ring-2 focus:ring-blue-100
                   disabled:bg-gray-50 disabled:text-gray-400 transition-colors
                   max-h-32 overflow-y-auto"
        style={{ lineHeight: '1.5' }}
      />
      <button
        type="submit"
        disabled={!value.trim() || isLoading}
        className="shrink-0 w-10 h-10 rounded-full bg-blue-600 hover:bg-blue-700
                   disabled:bg-gray-300 transition-colors flex items-center justify-center"
      >
        {isLoading
          ? <Loader2 className="w-4 h-4 text-white animate-spin" />
          : <SendHorizontal className="w-4 h-4 text-white" />
        }
      </button>
    </form>
  )
}
