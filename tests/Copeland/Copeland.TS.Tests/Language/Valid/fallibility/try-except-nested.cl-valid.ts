function read(): number ! string { return err("bad"); }
function main(): number { return try { try { read()? } except (inner) { read()? } } except (outer) { 0 }; }
