using System;
function xorIntegers(left: int, right: int): int {
    const modulus: long = Convert.ToInt64(65536) * 65536;
    let leftRemaining: long = if (left < 0) { left + modulus } else { left };
    let rightRemaining: long = if (right < 0) { right + modulus } else { right };
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
    return Convert.ToInt32(if (result > 2147483647) { result - modulus } else { result });
}
function hashIntegers(values: int[]): int {
    const modulus: long = Convert.ToInt64(65536) * 65536;
    let hash: int = -2128831035;
    for (const value of values) {
        const product: long = Convert.ToInt64(xorIntegers(hash, value)) * 16777619;
        let wrapped: long = product % modulus;
        if (wrapped < 0) {
            wrapped = wrapped + modulus;
        }
        hash = Convert.ToInt32(if (wrapped > 2147483647) { wrapped - modulus } else { wrapped });
    }
    return hash;
}