// Places the rows of the hex view: 16 bytes per row, a row of dots for each part of the data that is not in memory.
// No DOM, so `npm test` can run it. "Offset" is a position in the data, "index" one in the bytes that were kept.

class HexLayout {
  /**
   * @param {number} count The bytes that were kept
   * @param {{ offset: number, length: number }[]} skipped What was left out, in the order of the data without overlaps
   */
  constructor(count, skipped) {
    this.count = count;
    this.skipped = skipped;
    /** Per skipped part: the index of the byte after it */
    this.indexes = new Array(skipped.length);
    /** Per skipped part: its row */
    this.skipRows = new Array(skipped.length);
    /** Per skipped part: the bytes left out up to and including it */
    this.passed = new Array(skipped.length);
    let passed = 0;
    let row = 0;
    let previous = 0;
    for (let t = 0; t < skipped.length; t++) {
      const index = skipped[t].offset - passed;
      row += Math.ceil((index - previous) / 16);
      this.indexes[t] = index;
      this.skipRows[t] = row;
      row++;
      previous = index;
      passed += skipped[t].length;
      this.passed[t] = passed;
    }
    this.rows = row + Math.ceil((count - previous) / 16);
  }

  /** The last position in a sorted list at or before the value, -1 when there is none */
  static lastAtOrBefore(list, value) {
    let low = 0;
    let high = list.length;
    while (low < high) {
      const middle = (low + high) >> 1;
      if (list[middle] <= value) {
        low = middle + 1;
      } else {
        high = middle;
      }
    }
    return low - 1;
  }

  /** The index of a byte of the data, a skipped byte gets the index of the next byte that was kept */
  toIndex(offset) {
    let low = 0;
    let high = this.skipped.length;
    while (low < high) { // the first part that ends after the offset
      const middle = (low + high) >> 1;
      if (this.skipped[middle].offset + this.skipped[middle].length <= offset) {
        low = middle + 1;
      } else {
        high = middle;
      }
    }
    const index = low < this.skipped.length && offset >= this.skipped[low].offset
      ? this.indexes[low]
      : offset - (low === 0 ? 0 : this.passed[low - 1]);
    return Math.min(index, this.count);
  }

  /** The offset in the data of a byte that was kept */
  toOffset(index) {
    const part = HexLayout.lastAtOrBefore(this.indexes, index);
    return index + (part < 0 ? 0 : this.passed[part]);
  }

  /**
   * What a row shows.
   * @returns {{ skip: number, start: number, count: number }} The skipped part (skip, -1 for bytes), or the index of the first byte and the number of bytes
   */
  row(row) {
    const part = HexLayout.lastAtOrBefore(this.skipRows, row);
    if (part >= 0 && this.skipRows[part] === row) {
      return { skip: part, start: 0, count: 0 };
    }
    const first = part < 0 ? 0 : this.indexes[part];
    const start = first + 16 * (row - (part < 0 ? 0 : this.skipRows[part] + 1));
    const end = part + 1 < this.indexes.length ? this.indexes[part + 1] : this.count;
    return { skip: -1, start, count: Math.min(16, end - start) };
  }

  /** The row of a byte that was kept, the last row when there is no such byte */
  rowOf(index) {
    if (index >= this.count) {
      return Math.max(0, this.rows - 1);
    }
    const part = HexLayout.lastAtOrBefore(this.indexes, index);
    const first = part < 0 ? 0 : this.indexes[part];
    return (part < 0 ? 0 : this.skipRows[part] + 1) + Math.floor((index - first) / 16);
  }
}

if (typeof module !== 'undefined') {
  module.exports = { HexLayout };
}
