import { describe, expect, it } from 'vitest';

import { speed } from './format';
import { uk } from './i18n/uk';

describe('speed', () => {
  it('is one decimal with the dictionary’s separator and unit', () => {
    expect(speed(12.34, uk.units)).toBe('12,3 км/год');
    expect(speed(0, uk.units)).toBe('0,0 км/год');
  });
});
