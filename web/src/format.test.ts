import { describe, expect, it } from 'vitest';

import { averageSpeed, heartRate, speed } from './format';
import { uk } from './i18n/uk';

describe('speed', () => {
  it('is one decimal with the dictionary’s separator and unit', () => {
    expect(speed(12.34, uk.units)).toBe('12,3 км/год');
    expect(speed(0, uk.units)).toBe('0,0 км/год');
  });
});

describe('averageSpeed', () => {
  it('is metres over seconds in km/h, to one decimal', () => {
    expect(averageSpeed(6537.4, 2162, uk.units)).toBe('10,9 км/год');
  });

  it('is null without a distance or a time, never zero or infinity', () => {
    expect(averageSpeed(0, 100, uk.units)).toBeNull();
    expect(averageSpeed(1000, 0, uk.units)).toBeNull();
  });
});

describe('heartRate', () => {
  it('is a whole number with its unit', () => {
    expect(heartRate(153.6, 'уд/хв')).toBe('154 уд/хв');
  });
});
