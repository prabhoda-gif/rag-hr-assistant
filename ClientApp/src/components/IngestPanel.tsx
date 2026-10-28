import { useState, useRef } from 'react'
import { Upload, FileText, CheckCircle, AlertCircle, Loader2 } from 'lucide-react'
import type { IngestResponse } from '../types'

interface IngestPanelProps {
  userId: string
  onIngested: (info: IngestResponse) => void
}

type Status = 'idle' | 'uploading' | 'success' | 'error'

export function IngestPanel({ userId, onIngested }: IngestPanelProps) {
  const [status, setStatus]   = useState<Status>('idle')
  const [message, setMessage] = useState('')
  const [isDragging, setIsDragging] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  async function upload(file: File) {
    if (!file) return

    const ext = file.name.split('.').pop()?.toLowerCase()
    if (ext !== 'pdf' && ext !== 'txt') {
      setStatus('error')
      setMessage('Only .pdf and .txt files are supported')
      return
    }

    setStatus('uploading')
    setMessage(`Uploading ${file.name}…`)

    const form = new FormData()
    form.append('file', file)
    form.append('userId', userId || 'public')

    try {
      const resp = await fetch('/api/ingest', { method: 'POST', body: form })
      if (!resp.ok) {
        const err = await resp.text()
        throw new Error(err)
      }
      const data: IngestResponse = await resp.json()
      setStatus('success')
      setMessage(`${data.chunks} chunks · ${data.timeTaken}`)
      onIngested(data)
    } catch (err: unknown) {
      setStatus('error')
      setMessage(err instanceof Error ? err.message : 'Upload failed')
    }
  }

  function onFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (file) upload(file)
    e.target.value = ''
  }

  function onDrop(e: React.DragEvent) {
    e.preventDefault()
    setIsDragging(false)
    const file = e.dataTransfer.files[0]
    if (file) upload(file)
  }

  return (
    <div className="border-b border-amber-200 bg-amber-50 px-6 py-3">
      <div className="flex items-center gap-4">
        {/* Drop zone */}
        <div
          onDragOver={e => { e.preventDefault(); setIsDragging(true) }}
          onDragLeave={() => setIsDragging(false)}
          onDrop={onDrop}
          onClick={() => fileRef.current?.click()}
          className={`flex items-center gap-2 border-2 border-dashed rounded-lg px-4 py-2
                      cursor-pointer transition-colors text-sm flex-1 max-w-sm
                      ${isDragging
                        ? 'border-blue-400 bg-blue-50 text-blue-600'
                        : 'border-amber-300 hover:border-amber-400 text-amber-700'}`}
        >
          <Upload className="w-4 h-4 shrink-0" />
          <span>Drop a PDF or click to upload</span>
          <input
            ref={fileRef}
            type="file"
            accept=".pdf,.txt"
            className="hidden"
            onChange={onFileChange}
          />
        </div>

        {/* Status */}
        {status !== 'idle' && (
          <div className="flex items-center gap-2 text-sm">
            {status === 'uploading' && (
              <><Loader2 className="w-4 h-4 animate-spin text-blue-500" />
              <span className="text-gray-600">{message}</span></>
            )}
            {status === 'success' && (
              <><CheckCircle className="w-4 h-4 text-green-500" />
              <span className="text-green-700 font-medium">{message}</span></>
            )}
            {status === 'error' && (
              <><AlertCircle className="w-4 h-4 text-red-500" />
              <span className="text-red-600">{message}</span></>
            )}
          </div>
        )}
      </div>

      {/* Recently ingested file info */}
      {status === 'success' && (
        <p className="text-xs text-amber-600 mt-1 flex items-center gap-1">
          <FileText className="w-3 h-3" />
          Document indexed for user <strong>{userId}</strong> — ready to query
        </p>
      )}
    </div>
  )
}
