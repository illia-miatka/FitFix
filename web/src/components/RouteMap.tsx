import { useEffect, useMemo, useState } from 'react';
import type { LatLngBounds, LatLngTuple } from 'leaflet';
import { divIcon, latLngBounds } from 'leaflet';
import {
  AttributionControl,
  MapContainer,
  Marker,
  Polyline,
  Popup,
  TileLayer,
  ZoomControl,
  useMap,
} from 'react-leaflet';

import type { RepairSeries, TrustZone } from '../api/repair';

/** `uk.result.map` — every string of the map, Leaflet's own control labels included (AD-18). */
export interface MapTexts {
  readonly label: string;
  readonly layers: string;
  readonly repaired: string;
  readonly original: string;
  readonly start: string;
  readonly finish: string;
  readonly measuredUntil: string;
  readonly zoomIn: string;
  readonly zoomOut: string;
  /** The basemap's credit as markup: Leaflet writes an attribution with innerHTML. */
  readonly attribution: string;
}

/**
 * The OpenStreetMap standard tiles, darkened in `theme.css`. CARTO's keyless dark tiles now serve
 * an "API KEY REQUIRED" watermark, so they are not an option without a key (found by looking at
 * the page in a browser, which no jsdom test can). The browser fetches these tiles itself, so the
 * tile host sees which area was viewed — never the file and never the track. The tile policy asks
 * for a real Referer, so the default referrer policy stays. One constant: another provider is one
 * line, and the attribution beside it is the dictionary's.
 */
const tileUrl = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';

/**
 * The original's drawable stretches: one run of consecutive records that carry a position is one
 * segment, and a hole (`null`) ends it, so a stall never draws a line across the gap. A lone
 * point is not a line and draws nothing. Index order is the original's record order (AD-5).
 */
export function originalSegments(
  latitude: readonly (number | null)[],
  longitude: readonly (number | null)[],
): LatLngTuple[][] {
  const segments: LatLngTuple[][] = [];
  let run: LatLngTuple[] = [];
  const close = () => {
    if (run.length >= 2) segments.push(run);
    run = [];
  };

  for (let i = 0; i < latitude.length; i++) {
    const lat = latitude[i];
    const lon = longitude[i];
    if (lat === null || lat === undefined || lon === null || lon === undefined) close();
    else run.push([lat, lon]);
  }
  close();
  return segments;
}

/**
 * The record index where the last measured zone ends (AD-20): the last record at or before that
 * second. Null when there is no measured zone, or when it reaches the last record — a third
 * marker there would sit on the finish — or ends at the first, where it would sit on the start.
 * Read from `zones` rather than assumed to be a prefix of the activity.
 */
export function measuredEndIndex(
  zones: readonly TrustZone[],
  secondsFromStart: readonly number[],
): number | null {
  let end: number | null = null;
  for (const zone of zones) {
    if (zone.kind === 'measured' && (end === null || zone.toSeconds > end)) end = zone.toSeconds;
  }
  if (end === null) return null;

  let index = -1;
  for (let i = 0; i < secondsFromStart.length; i++) {
    if ((secondsFromStart[i] ?? Infinity) <= end) index = i;
    else break;
  }
  return index <= 0 || index >= secondsFromStart.length - 1 ? null : index;
}

/**
 * Where the three markers sit: the first and last record of the repaired track, and the record
 * where the last measured zone ends. Pure, so a swapped or shifted marker is a failing test rather
 * than a pin in the wrong place. The finish needs a second record to be a place of its own.
 */
export function routeMarkers(
  route: readonly LatLngTuple[],
  measuredIndex: number | null,
): { start?: LatLngTuple; measured?: LatLngTuple; finish?: LatLngTuple } {
  return {
    start: route[0],
    measured: measuredIndex === null ? undefined : route[measuredIndex],
    finish: route.length > 1 ? route[route.length - 1] : undefined,
  };
}

/**
 * The shape lives in an inner element: Leaflet positions a marker's own element with an inline
 * `transform`, which would silently replace the rotation that makes the measured pin a diamond.
 */
function pin(kind: 'start' | 'finish' | 'measured') {
  return divIcon({
    className: `route-pin route-pin-${kind}`,
    html: '<span class="route-pin-shape"></span>',
    iconSize: [18, 18],
  });
}

const pins = { start: pin('start'), finish: pin('finish'), measured: pin('measured') };

/** Refits the view when the drawn points change, e.g. when the repeated repair answers. */
function FitBounds({ bounds }: { readonly bounds: LatLngBounds }) {
  const map = useMap();
  useEffect(() => {
    map.fitBounds(bounds, { padding: [24, 24] });
  }, [map, bounds]);
  return null;
}

/** Names Leaflet's focusable container: it is a tab stop, and a tab stop needs a name. */
function Labelled({ label }: { readonly label: string }) {
  const map = useMap();
  useEffect(() => {
    const container = map.getContainer();
    container.setAttribute('role', 'region');
    container.setAttribute('aria-label', label);
  }, [map, label]);
  return null;
}

/**
 * The route on a map: the repaired track solid in the accent, the original dashed and muted,
 * and markers for start, finish and where the last measured zone ends. Two toggle buttons switch
 * the lines. Positions are read from the series by record index and never thinned (AD-5).
 *
 * Everything is reachable without a pointer: Leaflet pans on the arrow keys, the zoom buttons
 * and the markers are tab stops, and Enter on a marker opens its label.
 */
export function RouteMap({
  series,
  zones,
  texts,
  className,
}: {
  readonly series: RepairSeries;
  readonly zones: readonly TrustZone[];
  readonly texts: MapTexts;
  readonly className?: string;
}) {
  const [showRepaired, setShowRepaired] = useState(true);
  const [showOriginal, setShowOriginal] = useState(true);

  const { repaired, original } = series;
  const route = useMemo<LatLngTuple[]>(
    () => repaired.latitude.map((lat, i): LatLngTuple => [lat, repaired.longitude[i] ?? lat]),
    [repaired],
  );
  const segments = useMemo(
    () => originalSegments(original.latitude, original.longitude),
    [original],
  );
  const measuredIndex = useMemo(
    () => measuredEndIndex(zones, series.secondsFromStart),
    [zones, series.secondsFromStart],
  );
  // Fitted to everything the map can draw, so toggling a line never moves the view.
  const bounds = useMemo(
    () => (route.length === 0 ? null : latLngBounds([...route, ...segments.flat()])),
    [route, segments],
  );

  // Nothing to draw, nothing to show: a blank map would read as a failure of the repair.
  if (bounds === null) return null;

  const hasOriginal = segments.length > 0;
  const markers = routeMarkers(route, measuredIndex);

  return (
    <section className={className}>
      <div role="group" aria-label={texts.layers} className="mb-2 flex flex-wrap gap-2">
        <Toggle
          pressed={showRepaired}
          onToggle={() => setShowRepaired((value) => !value)}
          label={texts.repaired}
          swatch="border-t-4 border-solid border-accent"
        />
        {hasOriginal && (
          <Toggle
            pressed={showOriginal}
            onToggle={() => setShowOriginal((value) => !value)}
            label={texts.original}
            swatch="border-t-4 border-dashed border-original"
          />
        )}
      </div>

      <MapContainer
        bounds={bounds}
        boundsOptions={{ padding: [24, 24] }}
        zoomControl={false}
        attributionControl={false}
        className="h-80 w-full rounded-lg border border-border md:h-105"
      >
        <Labelled label={texts.label} />
        <FitBounds bounds={bounds} />
        <TileLayer
          url={tileUrl}
          maxZoom={19}
          attribution={texts.attribution}
        />
        <ZoomControl zoomInTitle={texts.zoomIn} zoomOutTitle={texts.zoomOut} />
        {/* No prefix: Leaflet's own is an English tooltip and a link the credit does not need. */}
        <AttributionControl prefix={false} />

        {hasOriginal &&
          showOriginal &&
          segments.map((positions, i) => (
            <Polyline key={i} positions={positions} className="route-original" />
          ))}
        {showRepaired && <Polyline positions={route} className="route-repaired" />}

        {markers.start !== undefined && (
          <Pin position={markers.start} icon={pins.start} label={texts.start} />
        )}
        {markers.measured !== undefined && (
          <Pin position={markers.measured} icon={pins.measured} label={texts.measuredUntil} />
        )}
        {markers.finish !== undefined && (
          <Pin position={markers.finish} icon={pins.finish} label={texts.finish} />
        )}
      </MapContainer>
    </section>
  );
}

/**
 * A marker that is a tab stop with a name: `title` is its accessible name, and the popup is the
 * label Enter opens. The popup's close button is Leaflet's English one, so it is off; Escape and
 * a click elsewhere close it.
 */
function Pin({
  position,
  icon,
  label,
}: {
  readonly position: LatLngTuple;
  readonly icon: ReturnType<typeof divIcon>;
  readonly label: string;
}) {
  return (
    <Marker position={position} icon={icon} title={label}>
      <Popup closeButton={false}>{label}</Popup>
    </Marker>
  );
}

/** A layer switch: pressed state in `aria-pressed`, a line swatch and the name in words. */
function Toggle({
  pressed,
  onToggle,
  label,
  swatch,
}: {
  readonly pressed: boolean;
  readonly onToggle: () => void;
  readonly label: string;
  readonly swatch: string;
}) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      onClick={onToggle}
      className={`flex items-center gap-2 rounded-pill border px-4 py-2 text-sm transition-colors ${
        pressed
          ? 'border-text-muted bg-surface-raised text-text'
          : 'border-border bg-surface text-text-muted line-through'
      }`}
    >
      <span aria-hidden="true" className={`inline-block w-6 ${swatch}`} />
      {label}
    </button>
  );
}
