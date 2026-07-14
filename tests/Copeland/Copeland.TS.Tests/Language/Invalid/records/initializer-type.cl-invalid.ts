record Point { x: number; }
function bad(): Point { return { x: "zero" }; }
