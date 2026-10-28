import { BookOpen } from 'lucide-react'
import type { ChunkResult } from '../types'

interface SourceCitationsProps {
  sources: ChunkResult[]
}

export function SourceCitations({ sources }: SourceCitationsProps) {
  if (sources.length === 0) return null

  return (
    <div className="mt-3 pt-3 border-t border-gray-100">
      <p className="text-xs font-medium text-gray-400 mb-2 flex items-center gap-1">
        <BookOpen className="w-3 h-3" /> Sources
      </p>
      <div className="flex flex-wrap gap-1.5">
        {sources.map((s, i) => (
          <span
            key={i}
            title={s.chunkText}
            className="inline-flex items-center gap-1 bg-blue-50 text-blue-700
                       border border-blue-100 rounded-full px-2.5 py-0.5 text-xs
                       cursor-default hover:bg-blue-100 transition-colors"
          >
            <BookOpen className="w-3 h-3" />
            {s.fileName}
            {s.pageNumber > 0 && (
              <span className="text-blue-400">· p.{s.pageNumber}</span>
            )}
            <span className="text-blue-300">· {(1 - s.distance).toFixed(2)}</span>
          </span>
        ))}
      </div>
    </div>
  )
}
