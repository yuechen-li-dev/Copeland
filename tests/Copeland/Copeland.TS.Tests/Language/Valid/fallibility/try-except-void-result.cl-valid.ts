function effect(): void ! string { return; }
function main(): void { try { effect()? } except (error) { effect()! }; }
