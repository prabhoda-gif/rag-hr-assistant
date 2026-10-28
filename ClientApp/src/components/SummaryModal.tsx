import { useEffect, useRef, useState } from 'react'
import { X, Loader2 } from 'lucide-react'

interface SummaryModalProps {
  fileName: string
  userId: string
  onClose: () => void
}

// =============================================================================
// SummaryModal — streams the document summary token by token
//
// LEARNING NOTE — Same SSE streaming pattern as chat
// We reuse the exact same fetch + ReadableStream approach from App.tsx.
// The server sends "data: token\n\n" events and we append each token
// to the displayed text in real time. No polling, no waiting for the full
// response — users see the summary being written live.
// =============================================================================
export function SummaryModal({ fileName, userId, onClose }: SummaryModalProps) {
  const [summary, setSummary]     = useState('')
  const [loading, setLoading]     = useState(true)
  const [error, setError]         = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  useEffect(() => {
    const ctrl = new AbortController()
    abortRef.current = ctrl

    async function fetchSummary() {
      try {
        const resp = await fetch('/api/summarize/stream', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ fileName, userId }),
          signal: ctrl.signal,
        })

        if (!resp.ok || !resp.body) {
          setError('Failed to fetch summary')
          setLoading(false)
          return
        }

        const reader = resp.body.getReader()
        const decoder = new TextDecoder()
        setLoading(false)

        while (true) {
          const { done, value } = await reader.read()
          if (done) break

          const text = decoder.decode(value)
          const lines = text.split('\n')

          for (const line of lines) {
            if (!line.startsWith('data: ')) continue
            const token = line.slice(6)
            if (token === '[DONE]') return
            setSummary(prev => prev + token)
          }
        }
      } catch (err: unknown) {
        if ((err as Error).name !== 'AbortError') {
          setError('Summary request failed')
          setLoading(false)
        }
      }
    }

    fetchSummary()

    return () => ctrl.abort()
  }, [fileName, userId])

  function handleClose() {
    abortRef.current?.abort()
    onClose()
  }

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[80vh] flex flex-col">

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">Document Summary</h2>
            <p className="text-sm text-gray-500">{fileName}</p>
          </div>
          <button
            onClick={handleClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-4">
          {loading && (
            <div className="flex items-center gap-2 text-gray-500">
              <Loader2 className="w-4 h-4 animate-spin" />
              <span>Generating summary…</span>
            </div>
          )}

          {error && (
            <p className="text-red-600">{error}</p>
          )}

          {summary && (
            <div className="prose prose-sm max-w-none whitespace-pre-wrap text-gray-800 leading-relaxed">
              {summary}
              {/* Blinking cursor while streaming */}
              {loading === false && summary.length > 0 ? null : (
                <span className="inline-block w-2 h-4 bg-blue-500 ml-0.5 animate-pulse" />
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="px-6 py-3 border-t border-gray-200 flex justify-end">
          <button
            onClick={handleClose}
            className="px-4 py-2 text-sm bg-gray-100 hover:bg-gray-200
                       text-gray-700 rounded-lg transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
