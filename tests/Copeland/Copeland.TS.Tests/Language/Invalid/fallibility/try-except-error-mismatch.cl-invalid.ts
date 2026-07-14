function one(): number ! string { return ok(1); }
function two(): number ! number { return ok(2); }
function main(): number { return try { one()?; two()? } except (error) { 0 }; }
