// Importing the /vitest entry point registers jest-dom's matchers with `expect` and augments
// its types, so `toBeDisabled` needs no declaration of its own anywhere.
import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

afterEach(cleanup);

// Two things Leaflet needs from a browser that jsdom does not have, both read when Leaflet loads
// or when a map is first sized, so they live here and not in one test.
//
// Without `createSVGRect` Leaflet decides the browser has no SVG and draws no path at all; without
// a non-zero size the map has no pixels to cover, so it requests no tiles and places nothing.
if (typeof SVGSVGElement !== 'undefined' && !('createSVGRect' in SVGSVGElement.prototype)) {
  Object.defineProperty(SVGSVGElement.prototype, 'createSVGRect', { value: () => ({}) });
}
// (Some suites run under the node environment, where there is no DOM to shim.)
if (typeof HTMLElement !== 'undefined') {
  for (const [property, size] of [
    ['clientWidth', 800],
    ['clientHeight', 400],
  ] as const) {
    Object.defineProperty(HTMLElement.prototype, property, { configurable: true, get: () => size });
  }
}
