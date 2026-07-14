function read(): number ! string { return ok(1); }
function main(): number ! string { return try { read()?; ok(1) } except (error) { err(error) }; }
