export function RunWorkload(iterations: int): int {
    let checksum: int = 0;

    for (let index: int = 0; index < iterations; index = index + 1) {
        checksum = (checksum * 31 + index * 17 + 7) % 1000003;
    }

    return checksum;
}
