function sumRange(limit: int): int {
    let total: int = 0;
    for (let index: int = 0; index < limit; index = index + 1) {
        total = total + index;
    }
    return total;
}
function run(): int { return sumRange(10); }