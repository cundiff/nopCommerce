import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { calculateTaxAmount } from '../src/utils/decimal.js';

describe('calculateTaxAmount', () => {
  it('returns tax on top of price when price excludes tax', () => {
    assert.equal(calculateTaxAmount(100, 10, false), 10);
    assert.equal(calculateTaxAmount(50, 8.25, false), 4.125);
  });

  it('returns embedded tax when price includes tax', () => {
    assert.equal(calculateTaxAmount(110, 10, true), 10);
    assert.equal(calculateTaxAmount(100, 20, true), 16.6667);
  });

  it('returns zero for non-positive inputs', () => {
    assert.equal(calculateTaxAmount(100, 0, false), 0);
    assert.equal(calculateTaxAmount(0, 10, false), 0);
    assert.equal(calculateTaxAmount(-5, 10, true), 0);
  });
});
