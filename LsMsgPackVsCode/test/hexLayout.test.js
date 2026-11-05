// The rows of the hex view (media/hexLayout.js).
const test = require('node:test');
const assert = require('node:assert');
const { HexLayout } = require('../media/hexLayout.js');

test('without skipped bytes: 16 per row', () => {
  const layout = new HexLayout(40, []);
  assert.strictEqual(layout.rows, 3);
  assert.deepStrictEqual(layout.row(0), { skip: -1, start: 0, count: 16 });
  assert.deepStrictEqual(layout.row(2), { skip: -1, start: 32, count: 8 });
  assert.strictEqual(layout.toIndex(39), 39);
  assert.strictEqual(layout.toOffset(39), 39);
  assert.strictEqual(layout.rowOf(16), 1);
  assert.strictEqual(new HexLayout(0, []).rows, 0);
  assert.strictEqual(new HexLayout(0, []).rowOf(0), 0);
});

test('a row of dots for skipped bytes, the bytes after it start a new row', () => {
  // 5 header bytes + 100 kept, 3 GB skipped, 100 kept + 1
  const skipped = [{ offset: 105, length: 2999999800 }];
  const layout = new HexLayout(206, skipped);
  assert.strictEqual(layout.rows, 7 + 1 + 7);
  assert.deepStrictEqual(layout.row(6), { skip: -1, start: 96, count: 9 });
  assert.deepStrictEqual(layout.row(7), { skip: 0, start: 0, count: 0 });
  assert.deepStrictEqual(layout.row(8), { skip: -1, start: 105, count: 16 });
  assert.deepStrictEqual(layout.row(14), { skip: -1, start: 201, count: 5 });

  assert.strictEqual(layout.toIndex(104), 104);
  assert.strictEqual(layout.toIndex(105), 105, 'a skipped byte: the next that was kept');
  assert.strictEqual(layout.toIndex(1500000000), 105);
  assert.strictEqual(layout.toIndex(105 + 2999999800), 105);
  assert.strictEqual(layout.toIndex(105 + 2999999800 + 100), 205);
  assert.strictEqual(layout.toIndex(9e9), 206);

  assert.strictEqual(layout.toOffset(104), 104);
  assert.strictEqual(layout.toOffset(105), 105 + 2999999800);
  assert.strictEqual(layout.toOffset(205), 205 + 2999999800);

  assert.strictEqual(layout.rowOf(104), 6);
  assert.strictEqual(layout.rowOf(105), 8);
  assert.strictEqual(layout.rowOf(205), 14);
  assert.strictEqual(layout.rowOf(206), 14, 'beyond the bytes: the last row');
});

test('dots at the very end and several parts', () => {
  const end = new HexLayout(32, [{ offset: 32, length: 1000 }]);
  assert.strictEqual(end.rows, 3);
  assert.deepStrictEqual(end.row(2), { skip: 0, start: 0, count: 0 });
  assert.strictEqual(end.rowOf(32), 2);

  const several = new HexLayout(30, [{ offset: 10, length: 100 }, { offset: 120, length: 50 }]);
  assert.strictEqual(several.rows, 1 + 1 + 1 + 1 + 1);
  assert.deepStrictEqual(several.row(0), { skip: -1, start: 0, count: 10 });
  assert.deepStrictEqual(several.row(1), { skip: 0, start: 0, count: 0 });
  assert.deepStrictEqual(several.row(2), { skip: -1, start: 10, count: 10 });
  assert.deepStrictEqual(several.row(3), { skip: 1, start: 0, count: 0 });
  assert.deepStrictEqual(several.row(4), { skip: -1, start: 20, count: 10 });
  assert.strictEqual(several.toOffset(10), 110);
  assert.strictEqual(several.toOffset(20), 170);
  assert.strictEqual(several.toIndex(115), 15);
  assert.strictEqual(several.toIndex(175), 25);
});

test('offsets and indexes convert back, every row holds what it says', () => {
  const skipped = [{ offset: 17, length: 3 }, { offset: 40, length: 1000 }, { offset: 1050, length: 7 }];
  const total = 1100;
  const count = total - 3 - 1000 - 7;
  const layout = new HexLayout(count, skipped);
  let index = 0;
  for (let row = 0; row < layout.rows; row++) {
    const place = layout.row(row);
    if (place.skip >= 0) {
      continue;
    }
    assert.strictEqual(place.start, index);
    assert.ok(place.count > 0 && place.count <= 16);
    for (let t = 0; t < place.count; t++) {
      assert.strictEqual(layout.rowOf(index), row);
      const offset = layout.toOffset(index);
      assert.strictEqual(offset, layout.toOffset(place.start) + t, 'the bytes of a row follow each other');
      assert.strictEqual(layout.toIndex(offset), index);
      index++;
    }
  }
  assert.strictEqual(index, count);
});
