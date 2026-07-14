function read(): number ! string { return err("bad"); }
function recover(): number ! string { return try { read()? } except (error) { read()? }; }
