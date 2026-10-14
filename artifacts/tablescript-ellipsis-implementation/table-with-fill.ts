record table Marks {
    key id: int = [10, 20, 30];
    valid: boolean = [true, true, true];
}
function cleared(): Marks {
    return Marks with { valid: [false ...] };
}