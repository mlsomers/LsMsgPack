// The invoice model of BenchmarkInvoices ported to JavaScript (the seeded PRNG differs from .NET's Random, the shapes and value kinds are the same),
// the type descriptors a JS client of x-lsmsgpack would need, and the two shapes: maps keyed by names and arrays of values in schema order.
import crypto from "node:crypto";

let seed = 42;
const rnd = (min, max) => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; const r = seed / 0x80000000; return max === undefined ? Math.floor(r * min) : min + Math.floor(r * (max - min)); };
const rndD = () => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed / 0x80000000; };
const Companies = ["Acme Corporation", "Globex B.V.", "Initech Ltd.", "Umbrella GmbH", "Stark Industries", "Wayne Enterprises", "Hooli Inc.", "Vandelay Industries"];
const Contacts = ["Jan de Vries", "Maria Jansen", "Peter Smith", "Anna Müller", "Luca Rossi", "Sofia García", "Tom Bakker", "Emma Visser"];
const Streets = ["Keizersgracht", "Main Street", "Hauptstraße", "Rue de la Paix", "Via Roma", "Kalverstraat", "Baker Street", "Coolsingel"];
const Cities = ["Amsterdam", "Rotterdam", "London", "Berlin", "Paris", "Rome", "Madrid", "Utrecht"];
const Countries = ["NL", "NL", "GB", "DE", "FR", "IT", "ES", "NL"];
const Products = ["Consultancy services", "Software license (annual)", "Office chair, ergonomic", "USB-C docking station", "Printer paper A4, 500 sheets", "Travel expenses", "On-site support", "Cloud hosting, standard tier", "Coffee beans, 1kg", "Network cable Cat6, 5m"];
const Vat = [21, 9, 0];

function address(i) {
  const c = rnd(Cities.length);
  return { Street: Streets[rnd(Streets.length)], HouseNumber: rnd(1, 500) + (i % 5 === 0 ? "a" : ""), PostalCode: rnd(1000, 9999) + " " + String.fromCharCode(65 + rnd(26), 65 + rnd(26)), City: Cities[c], Region: i % 3 === 0 ? null : "Region " + c, CountryCode: Countries[c] };
}
function customer(i) {
  const company = Companies[rnd(Companies.length)]; const billing = address(i);
  return { CustomerId: 10000 + i, CompanyName: company, ContactName: Contacts[rnd(Contacts.length)], Email: "invoices@" + company.split(" ")[0].toLowerCase() + ".example.com", Phone: "+31 " + rnd(10, 99) + " " + rnd(1000000, 9999999), VatNumber: "NL" + rnd(100000000, 999999999) + "B01", BillingAddress: billing, ShippingAddress: i % 4 === 0 ? address(i + 1) : billing };
}
// decimals as strings (what Generic() / MessagePack-CSharp write; x-lsmsgpack writes ext 1, about the same size)
const dec = (n) => n.toFixed(2);
function invoice(i) {
  const day = rnd(365); const date = new Date(Date.UTC(2026, 0, 1 + day)); const status = rnd(5);
  const inv = { Id: crypto.randomBytes(16), InvoiceNumber: "INV-2026-" + String(i).padStart(5, "0"), InvoiceDate: date, DueDate: new Date(date.getTime() + 30 * 864e5), PaidDate: status === 2 ? new Date(date.getTime() + rnd(1, 30) * 864e5) : null, Status: status, Currency: "EUR", Reference: "PO-" + rnd(100000, 999999), Notes: i % 2 === 0 ? "Please mention the invoice number when making the payment. Thank you for your business!" : null, Customer: customer(i), Lines: [] };
  let sub = 0, vatT = 0; const n = rnd(3, 25);
  for (let l = 1; l <= n; l++) {
    const q = rnd(1, 100), p = Math.round(rndD() * 50000) / 100, d = l % 4 === 0 ? 10 : 0, v = Vat[rnd(3)], t = Math.round(q * p * (100 - d)) / 100;
    inv.Lines.push({ LineNumber: l, ProductCode: "P" + rnd(1000, 9999), Description: Products[rnd(Products.length)], Quantity: dec(q), Unit: rnd(6), UnitPrice: dec(p), DiscountPercentage: dec(d), VatPercentage: dec(v), LineTotal: dec(t) });
    sub += t; vatT += Math.round(t * v) / 100;
  }
  inv.SubTotal = dec(sub); inv.VatTotal = dec(vatT); inv.Total = dec(sub + vatT);
  return inv;
}

// The type descriptors a JS client would need for x-lsmsgpack: the payload names the properties, not which class a nested value is.
const T = {};
T.Address = { name: "Address", props: ["Street", "HouseNumber", "PostalCode", "City", "Region", "CountryCode"], types: {} };
T.Customer = { name: "Customer", props: ["CustomerId", "CompanyName", "ContactName", "Email", "Phone", "VatNumber", "BillingAddress", "ShippingAddress"], types: { BillingAddress: T.Address, ShippingAddress: T.Address } };
T.InvoiceLine = { name: "InvoiceLine", props: ["LineNumber", "ProductCode", "Description", "Quantity", "Unit", "UnitPrice", "DiscountPercentage", "VatPercentage", "LineTotal"], types: {} };
T.Invoice = { name: "Invoice", props: ["Id", "InvoiceNumber", "InvoiceDate", "DueDate", "PaidDate", "Status", "Currency", "Reference", "Notes", "Customer", "Lines", "SubTotal", "VatTotal", "Total"], types: { Customer: T.Customer, Lines: { list: T.InvoiceLine } } };

const isDefault = (v) => v === null || v === undefined || v === 0 || v === "0.00";
// plain msgpack (application/msgpack, Generic preset): maps by name, defaults left out
function toNames(o, t) {
  const r = {};
  for (const p of t.props) { let v = o[p]; if (isDefault(v)) continue; const st = t.types[p]; if (st) v = st.list ? v.map((e) => toNames(e, st.list)) : toNames(v, st); r[p] = v; }
  return r;
}
// x-lsmsgpack body: arrays of values in schema order, nil for a left out value (trailing nils kept, the default)
function toPositional(o, t) {
  return t.props.map((p) => { const v = o[p]; if (isDefault(v)) return null; const st = t.types[p]; return st ? (st.list ? v.map((e) => toPositional(e, st.list)) : toPositional(v, st)) : v; });
}
// reading it back into named objects needs the descriptor (the "class") and the schema's names for the positions
function fromPositional(a, t, schema) {
  const names = schema[t.name] || t.props; const r = {};
  for (let k = 0; k < a.length; k++) { let v = a[k]; if (v === null) continue; const p = names[k]; const st = t.types[p]; if (st) v = st.list ? v.map((e) => fromPositional(e, st.list, schema)) : fromPositional(v, st, schema); r[p] = v; }
  return r;
}

const COUNT = +process.argv[2] || 100;
const invoices = []; for (let i = 1; i <= COUNT; i++) invoices.push(invoice(i));
const schema = { Invoice: T.Invoice.props, Customer: T.Customer.props, Address: T.Address.props, InvoiceLine: T.InvoiceLine.props };
const named = invoices.map((x) => toNames(x, T.Invoice));
const positional = invoices.map((x) => toPositional(x, T.Invoice));
export { COUNT, T, schema, invoices, named, positional, toNames, toPositional, fromPositional };
