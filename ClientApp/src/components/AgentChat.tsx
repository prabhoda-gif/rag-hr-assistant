import { useState, useRef } from 'react'
import { Send, Loader2, Search, Eye, CheckCircle, Wrench } from 'lucide-react'

interface AgentStep {
  stepType: 'tool_call' | 'observation' | 'answer'
  content: string
  toolName?: string
}

interface AgentRun {
  question: string
  steps: AgentStep[]
  done: boolean
}

// =============================================================================
// AgentChat — UI for the ReAct agent
//
// LEARNING NOTE — Why show steps?
// Unlike the chat tab that just streams the final answer, here we show every
// step the agent takes: which tools it calls, what they return, and how it
// reasons toward the final answer. This makes the agent "explainable" —
// users can see WHY they got an answer, not just what it is.
//
// This transparency is a key selling point of agentic AI in enterprise:
// "The system called HR policy search, checked your balance, then answered."
// =============================================================================

export function AgentChat({ userId }: { userId: string }) {
  const [question, setQuestion] = useState('')
  const [runs, setRuns]         = useState<AgentRun[]>([])
  const [loading, setLoading]   = useState(false)
  const abortRef = useRef<AbortController | null>(null)

  async function ask() {
    const q = question.trim()
    if (!q || loading) return

    setQuestion('')
    setLoading(true)

    const run: AgentRun = { question: q, steps: [], done: false }
    setRuns(prev => [...prev, run])
    const runIndex = runs.length  // index of this run

    const ctrl = new AbortController()
    abortRef.current = ctrl

    try {
      const resp = await fetch('/api/agent/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question: q, userId }),
        signal: ctrl.signal,
      })

      if (!resp.ok || !resp.body) throw new Error('Agent request failed')

      const reader  = resp.body.getReader()
      const decoder = new TextDecoder()
      let   buffer  = ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''

        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          const data = line.slice(6)
          if (data === '[DONE]') break

          try {
            const step: AgentStep = JSON.parse(data)
            setRuns(prev => {
              const next = [...prev]
              const r = { ...next[runIndex] }
              r.steps = [...r.steps, step]
              if (step.stepType === 'answer') r.done = true
              next[runIndex] = r
              return next
            })
          } catch {}
        }
      }
    } catch (err: unknown) {
      if ((err as Error).name !== 'AbortError') {
        setRuns(prev => {
          const next = [...prev]
          const r = { ...next[runIndex] }
          r.steps = [...r.steps, { stepType: 'answer', content: '❌ Agent request failed.' }]
          r.done = true
          next[runIndex] = r
          return next
        })
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex flex-col h-full">
      {/* Intro banner */}
      {runs.length === 0 && (
        <div className="flex-1 flex items-center justify-center p-8">
          <div className="text-center max-w-md">
            <div className="text-4xl mb-3">🤖</div>
            <h2 className="text-lg font-semibold text-gray-800 mb-2">HR Agent</h2>
            <p className="text-sm text-gray-500">
              Ask multi-step questions. The agent will search HR policies,
              check leave balances, and calculate dates to answer you.
            </p>
            <p className="text-xs text-gray-400 mt-3 italic">
              Try: "I want to take leave from Dec 20 to Jan 3. Do I have enough days left?"
            </p>
          </div>
        </div>
      )}

      {/* Agent runs */}
      {runs.length > 0 && (
        <div className="flex-1 overflow-y-auto p-4 space-y-6">
          {runs.map((run, ri) => (
            <div key={ri} className="space-y-2">
              {/* User question */}
              <div className="flex justify-end">
                <div className="bg-blue-600 text-white rounded-2xl rounded-br-sm px-4 py-2 max-w-lg text-sm">
                  {run.question}
                </div>
              </div>

              {/* Agent steps */}
              <div className="space-y-1.5 pl-2">
                {run.steps.map((step, si) => (
                  <StepRow key={si} step={step} />
                ))}

                {/* Spinner while loading */}
                {!run.done && loading && ri === runs.length - 1 && (
                  <div className="flex items-center gap-2 text-xs text-gray-400 py-1">
                    <Loader2 className="w-3 h-3 animate-spin" />
                    <span>Agent is thinking…</span>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Input bar */}
      <div className="border-t border-gray-200 p-4 bg-white">
        <div className="flex gap-2">
          <input
            value={question}
            onChange={e => setQuestion(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && !e.shiftKey && ask()}
            placeholder="Ask the HR agent a question…"
            disabled={loading}
            className="flex-1 border border-gray-300 rounded-xl px-4 py-2 text-sm
                       focus:outline-none focus:ring-2 focus:ring-blue-500
                       disabled:opacity-50"
          />
          <button
            onClick={ask}
            disabled={loading || !question.trim()}
            className="bg-blue-600 hover:bg-blue-700 text-white rounded-xl
                       px-4 py-2 disabled:opacity-50 transition-colors"
          >
            {loading
              ? <Loader2 className="w-4 h-4 animate-spin" />
              : <Send className="w-4 h-4" />}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Individual step row ───────────────────────────────────────────────────────

function StepRow({ step }: { step: AgentStep }) {
  if (step.stepType === 'tool_call') {
    return (
      <div className="flex items-start gap-2 text-xs text-purple-700 bg-purple-50
                      border border-purple-200 rounded-lg px-3 py-2">
        <Wrench className="w-3 h-3 mt-0.5 shrink-0" />
        <div>
          <span className="font-medium">{step.toolName}</span>
          <span className="text-purple-500 ml-1">{formatArgs(step.content)}</span>
        </div>
      </div>
    )
  }

  if (step.stepType === 'observation') {
    return (
      <details className="text-xs text-gray-600 bg-gray-50 border border-gray-200
                          rounded-lg px-3 py-2 cursor-pointer">
        <summary className="flex items-center gap-2 font-medium select-none">
          <Eye className="w-3 h-3" />
          Observation from {step.toolName}
        </summary>
        <pre className="mt-2 whitespace-pre-wrap font-sans text-gray-500 leading-relaxed">
          {step.content}
        </pre>
      </details>
    )
  }

  // Final answer
  return (
    <div className="flex items-start gap-2 bg-white border border-gray-200
                    rounded-2xl rounded-tl-sm px-4 py-3 text-sm text-gray-800
                    shadow-sm max-w-2xl">
      <CheckCircle className="w-4 h-4 text-green-500 shrink-0 mt-0.5" />
      <div className="whitespace-pre-wrap leading-relaxed">{step.content}</div>
    </div>
  )
}

// Extract argument values from the content string "Calling toolName({"key":"val"})"
function formatArgs(content: string): string {
  const match = content.match(/\((\{.+\})\)$/)
  if (!match) return ''
  try {
    const args = JSON.parse(match[1])
    return '→ ' + Object.values(args).join(', ')
  } catch {
    return ''
  }
}
