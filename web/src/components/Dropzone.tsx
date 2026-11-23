import { useId, useRef, useState } from 'react';
import type { DragEvent } from 'react';

/** The strings of one zone, taken from `uk.dropzones.original` or `uk.dropzones.track`. */
export interface DropzoneTexts {
  readonly title: string;
  readonly hint: string;
  readonly role: string;
  readonly prompt: string;
  readonly pick: string;
}

export interface DropzoneProps {
  readonly texts: DropzoneTexts;
  /** `uk.dropzones.clear` — the label of the × that empties the zone. */
  readonly clearLabel: string;
  readonly file: File | null;
  readonly onFileChange: (file: File | null) => void;
}

/**
 * One zone, naming what it wants and showing what it was given.
 *
 * A real `button`, so Tab, Enter and Space reach it and drag-and-drop is never the only way in
 * (Accessibility Floor; WCAG 2.1.1). Story 2.5 replaces the file name below with what the
 * service understood about the file — this story only holds it in state and uploads nothing.
 *
 * No `accept` filter: AD-13 keeps format detection in the core, which reads the bytes. A
 * picker that hid a valid FIT because of its extension would refuse a file the service accepts.
 */
export function Dropzone({ texts, clearLabel, file, onFileChange }: DropzoneProps) {
  const inputId = useId();
  const describedById = useId();
  const input = useRef<HTMLInputElement>(null);
  const [isDraggedOver, setIsDraggedOver] = useState(false);

  function take(files: FileList | null) {
    const first = files?.[0];
    if (first !== undefined) onFileChange(first);
  }

  function onDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setIsDraggedOver(false);
    take(event.dataTransfer.files);
  }

  return (
    <div
      className="relative"
      onDragOver={(event) => {
        event.preventDefault();
        setIsDraggedOver(true);
      }}
      onDragLeave={() => setIsDraggedOver(false)}
      onDrop={onDrop}
    >
      <button
        type="button"
        aria-label={texts.pick}
        aria-describedby={describedById}
        onClick={() => input.current?.click()}
        className={`flex w-full flex-col items-start gap-2 rounded-lg border-[1.5px] border-dashed bg-surface p-gutter text-left transition-colors ${
          isDraggedOver ? 'border-accent bg-surface-raised' : 'border-border hover:border-text-muted'
        }`}
      >
        <span className="type-display text-base text-text">{texts.title}</span>
        <span className="text-sm text-text-muted">{texts.role}</span>
        <span id={describedById} className="mt-4 text-sm text-text-muted">
          {file === null ? `${texts.prompt} · ${texts.hint}` : file.name}
        </span>
      </button>

      {/* Outside the button rather than inside it: one control may not contain another. */}
      {file !== null && (
        <button
          type="button"
          aria-label={clearLabel}
          onClick={() => onFileChange(null)}
          className="absolute top-3 right-3 flex size-8 items-center justify-center rounded-pill border border-border bg-surface-raised text-text-muted transition-colors hover:text-text"
        >
          <span aria-hidden="true">×</span>
        </button>
      )}

      <input
        ref={input}
        id={inputId}
        type="file"
        className="hidden"
        onChange={(event) => take(event.target.files)}
      />
    </div>
  );
}
