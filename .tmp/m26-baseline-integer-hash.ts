function xorIntegers(left: int, right: int): int {
    let leftRemaining: int = if (left < 0) { left + 2147483647 + 1 } else { left };
    let rightRemaining: int = if (right < 0) { right + 2147483647 + 1 } else { right };
    let place: int = 1;
    let result: int = 0;
    for (let bit: int = 0; bit < 31; bit = bit + 1) {
        if (leftRemaining % 2 != rightRemaining % 2) {
            result = result + place;
        }
        leftRemaining = leftRemaining / 2;
        rightRemaining = rightRemaining / 2;
        if (bit < 30) {
            place = place * 2;
        }
    }
    return if ((left < 0) != (right < 0)) { result - 2147483647 - 1 } else { result };
}
function hashIntegers(values: int[]): int {
    let hash: int = -2128831035;
    for (const value of values) {
        hash = xorIntegers(hash, value) * 16777619;
    }
    return hash;
}
function run(): int { return hashIntegers([1, 2, 3]); }