function identity<T>(value: T): T { return value; }
function main(): number { const numberIdentity = identity<number>; return numberIdentity(1); }
