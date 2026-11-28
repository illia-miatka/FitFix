import { useEffect, useReducer } from 'react';
import { CircleMarker, Polyline, useMap, useMapEvents } from 'react-leaflet';

import {
  addPoint,
  clearPoints,
  emptyHistory,
  redo,
  removeLastPoint,
  undo,
  type History,
  type Point,
} from '../drawing';

/** `uk.result.map.draw` — every string of the drawing mode (AD-18). */
export interface DrawTexts {
  readonly begin: string;
  readonly toolbar: string;
  /** How a point is placed, with the keys: the hint is the only place the keyboard way is told. */
  readonly hint: string;
  readonly startEnd: string;
  readonly finishEnd: string;
  readonly points: string;
  readonly undo: string;
  readonly redo: string;
  readonly removeLast: string;
  readonly clear: string;
  readonly confirm: string;
  readonly confirming: string;
  readonly cancel: string;
  readonly failed: string;
}

type Action =
  | { readonly type: 'add'; readonly point: Point }
  | { readonly type: 'removeLast' | 'clear' | 'undo' | 'redo' | 'reset' };

function reduce(history: History, action: Action): History {
  switch (action.type) {
    case 'add':
      return addPoint(history, action.point);
    case 'removeLast':
      return removeLastPoint(history);
    case 'clear':
      return clearPoints(history);
    case 'undo':
      return undo(history);
    case 'redo':
      return redo(history);
    case 'reset':
      return emptyHistory;
  }
}

/** The history of the points and the one function that changes it. */
export function useDrawingHistory() {
  return useReducer(reduce, emptyHistory);
}

/**
 * What lives on the map while drawing: the line, its points, and the two ways of placing one —
 * a click, and Enter at the map centre, which is what makes it work without a pointer. Leaflet's
 * own keys (arrows pan, plus and minus zoom) move the centre, so the crosshair is the cursor.
 *
 * Enter, Backspace and the undo keys act only while the map itself has focus: on a marker, Enter
 * belongs to the marker and opens its label.
 */
export function DrawingLayer({
  line,
  points,
  dispatch,
}: {
  readonly line: readonly Point[];
  readonly points: readonly Point[];
  readonly dispatch: (action: Action) => void;
}) {
  const map = useMap();

  useMapEvents({
    click: (event) => dispatch({ type: 'add', point: [event.latlng.lat, event.latlng.lng] }),
  });

  useEffect(() => {
    const container = map.getContainer();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.target !== container) return;

      const modified = event.ctrlKey || event.metaKey;
      const key = event.key.toLowerCase();
      if (event.key === 'Enter') {
        const centre = map.getCenter();
        dispatch({ type: 'add', point: [centre.lat, centre.lng] });
      } else if (event.key === 'Backspace') {
        dispatch({ type: 'removeLast' });
      } else if (modified && key === 'z') {
        dispatch({ type: event.shiftKey ? 'redo' : 'undo' });
      } else if (modified && key === 'y') {
        dispatch({ type: 'redo' });
      } else {
        return;
      }
      event.preventDefault();
    };
    container.addEventListener('keydown', onKeyDown);
    return () => container.removeEventListener('keydown', onKeyDown);
  }, [map, dispatch]);

  return (
    <>
      <Polyline
        positions={line.map(([latitude, longitude]): [number, number] => [latitude, longitude])}
        className="route-drawn"
      />
      {points.map(([latitude, longitude], i) => (
        <CircleMarker
          key={i}
          center={[latitude, longitude]}
          radius={5}
          className="route-drawn-point"
          interactive={false}
        />
      ))}
    </>
  );
}

/** What the drawing's own answer can be: it worked, or it did not and here is the sentence. */
export interface DrawAnswer {
  readonly ok: boolean;
  readonly message?: string;
}

/**
 * The buttons of the drawing mode. Every one is a button with a visible name, and every action
 * has its inverse: undo and redo cover add, remove and clear alike.
 */
export function DrawingToolbar({
  texts,
  history,
  dispatch,
  busy,
  message,
  onConfirm,
  onCancel,
}: {
  readonly texts: DrawTexts;
  readonly history: History;
  readonly dispatch: (action: Action) => void;
  readonly busy: boolean;
  readonly message: string | null;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
}) {
  const empty = history.present.length === 0;

  return (
    <div className="mt-2">
      <p className="mb-2 max-w-[70ch] text-sm text-text-muted">{texts.hint}</p>
      <div role="toolbar" aria-label={texts.toolbar} className="flex flex-wrap items-center gap-2">
        <Tool
          label={texts.undo}
          disabled={busy || history.past.length === 0}
          onPress={() => dispatch({ type: 'undo' })}
        />
        <Tool
          label={texts.redo}
          disabled={busy || history.future.length === 0}
          onPress={() => dispatch({ type: 'redo' })}
        />
        <Tool
          label={texts.removeLast}
          disabled={busy || empty}
          onPress={() => dispatch({ type: 'removeLast' })}
        />
        <Tool
          label={texts.clear}
          disabled={busy || empty}
          onPress={() => dispatch({ type: 'clear' })}
        />
        <span className="text-sm text-text-muted">
          {texts.points} {history.present.length}
        </span>
        <button
          type="button"
          disabled={busy || empty}
          aria-busy={busy}
          onClick={onConfirm}
          className="min-h-11 rounded-pill bg-accent px-4 py-2 text-sm text-accent-ink disabled:cursor-not-allowed disabled:bg-surface-raised disabled:text-text-muted"
        >
          {busy ? texts.confirming : texts.confirm}
        </button>
        <Tool label={texts.cancel} disabled={busy} onPress={onCancel} />
      </div>
      <div role="status" className="empty:hidden">
        {message !== null && <p className="mt-2 text-sm text-danger">{message}</p>}
      </div>
    </div>
  );
}

function Tool({
  label,
  disabled,
  onPress,
}: {
  readonly label: string;
  readonly disabled: boolean;
  readonly onPress: () => void;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onPress}
      className="min-h-11 rounded-pill border border-border bg-surface px-4 py-2 text-sm text-text transition-colors enabled:hover:bg-surface-raised disabled:cursor-not-allowed disabled:text-text-muted"
    >
      {label}
    </button>
  );
}
