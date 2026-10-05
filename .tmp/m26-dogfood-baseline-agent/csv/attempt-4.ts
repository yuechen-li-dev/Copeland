using System;
function parseCsvIntegers(text: string): int[] {
    let count: int = 1;
    for (let index: int = 0; index < text.length; index = index + 1) {
        if (text[index] == ",") {
            count = count + 1;
        }
    }
    const result: int[] = new int[count];
    let outputIndex: int = 0;
    let value: int = 0;
    let sign: int = 1;
    for (let index: int = 0; index < text.length; index = index + 1) {
        const character = text[index];
        if (character == ",") {
            result[outputIndex] = value * sign;
            outputIndex = outputIndex + 1;
            value = 0;
            sign = 1;
        } else if (character == "-") {
            sign = -1;
        } else if (character != " " && character != "\t" && character != "\r" && character != "\n") {
            value = value * 10 + Int32.Parse(character);
        }
    }
    result[outputIndex] = value * sign;
    return result;
}