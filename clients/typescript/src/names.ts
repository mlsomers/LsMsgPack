// Property names as System.Text.Json's JsonNamingPolicy.CamelCase writes them (the propertyNames option, docs/TypeScriptClient.md).
// lsmsgpack-ts names the fields of the models with JsonNamingPolicy.CamelCase itself: keep the two the same.

/** The names of the properties of .NET classes in JavaScript: 'camelCase' (invoiceDate, the client's default) or 'dotnet' (as they are, e.g. InvoiceDate). Never dictionary keys or $type. */
export type PropertyNames = 'dotnet' | 'camelCase';

const upper = /^\p{Lu}$/u;

/** char.IsUpper of one UTF-16 unit (a lone surrogate is not). */
function isUpper(c: string): boolean {
  return upper.test(c);
}

/**
 * JsonNamingPolicy.CamelCase.ConvertName: the upper case letters at the start become lower case, except the last of them when a lower case letter follows
 * (InvoiceDate → invoiceDate, ID → id, URLValue → urlValue, IOStream → ioStream).
 */
export function camelCase(name: string): string {
  if (name.length === 0 || !isUpper(name[0])) return name;
  const chars = name.split(''); // UTF-16 units, as the chars of .NET
  for (let i = 0; i < chars.length; i++) {
    if (i === 1 && !isUpper(chars[i])) break;
    if (i > 0 && i + 1 < chars.length && !isUpper(chars[i + 1])) {
      if (chars[i + 1] === ' ') chars[i] = toLower(chars[i]);
      break;
    }
    chars[i] = toLower(chars[i]);
  }
  return chars.join('');
}

/** char.ToLowerInvariant: a unit whose lower case is longer stays as it is (İ is i + a combining dot, .NET keeps İ). */
function toLower(c: string): string {
  const lower = c.toLowerCase();
  return lower.length === 1 ? lower : c;
}

/** The JavaScript name of a .NET property name. */
export function nameFunction(names: PropertyNames): (name: string) => string {
  return names === 'camelCase' ? camelCase : (name) => name;
}
