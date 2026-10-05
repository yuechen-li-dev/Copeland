using System;
function xorIntegers(left: int, right: int): int {
    let leftRemaining: long = if (left < 0) { left + 4294967296 } else { left };
    let rightRemaining: long = if (right < 0) { right + 4294967296 } else { right };
    let place: long = 1;
    let result: long = 0;
    for (let bit: int = 0; bit < 32; bit = bit + 1) {
        if (leftRemaining % 2 != rightRemaining % 2) {
            result = result + place;
        }
        leftRemaining = leftRemaining / 2;
        rightRemaining = rightRemaining / 2;
        place = place * 2;
    }
    return Convert.ToInt32(if (result > 2147483647) { result - 4294967296 } else { result });
}
function hashIntegers(values: int[]): int {
    let hash: int = -2128831035;
    for (const value of values) {
        const product: long = xorIntegers(hash, value) * 16777619;
        let wrapped: long = product % 4294967296;
        if (wrapped < 0) {
            wrapped = wrapped + 4294967296;
        }
        hash = Convert.ToInt32(if (wrapped > 2147483647) { wrapped - 4294967296 } else { wrapped });
    }
    return hash;
}