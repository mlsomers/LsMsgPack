// propertyNames: 'camelCase' names as System.Text.Json's JsonNamingPolicy.CamelCase does (lsmsgpack-ts uses it for the fields of the models).
import assert from 'node:assert';
import { test } from 'node:test';
import { camelCase } from '../dist/index.js';

test('camelCase equals JsonNamingPolicy.CamelCase.ConvertName', () => {
  const expected = [ // the output of JsonNamingPolicy.CamelCase.ConvertName (.NET 8)
    ['InvoiceDate', 'invoiceDate'], ['ID', 'id'], ['Id', 'id'], ['id', 'id'], ['URLValue', 'urlValue'], ['IOStream', 'ioStream'], ['A', 'a'], ['ABC', 'abc'],
    ['XMLHttpRequest', 'xmlHttpRequest'], ['IPhone', 'iPhone'], ['I Am', 'i Am'], ['ABC DEF', 'abc DEF'], ['Ünder', 'ünder'], ['ÉCOLE', 'école'], ['İstanbul', 'İstanbul'],
    ['Key', 'key'], ['Value', 'value'], ['_Name', '_Name'], ['Name2', 'name2'], ['HTML5', 'htmL5'], ['X1Y', 'x1Y'], ['$Type', '$Type'], ['', ''],
  ];
  for (const [name, camel] of expected) assert.strictEqual(camelCase(name), camel, name);
});
