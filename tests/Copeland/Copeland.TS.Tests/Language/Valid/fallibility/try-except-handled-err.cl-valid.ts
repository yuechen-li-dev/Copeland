function read(): number ! string { return err("bad"); }
function main(): number { return try { read()? } except (error) { 0 }; }
