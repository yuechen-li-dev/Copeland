function read(): number ! string { return ok(1); }
function main(): number { return try { read()? } except (error) { 0 }; }
