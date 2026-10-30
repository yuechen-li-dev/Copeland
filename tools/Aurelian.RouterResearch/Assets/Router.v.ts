function Missing(): u32 {
    return (65535 << 16) | 65535;
}

// Bounded Ethernet/IPv4 TCP/UDP policy evaluation. Tables are immutable per batch.
function Byte(readonly packets: StorageBuffer<u32>, start: u32, offset: u32): u32 {
    return (packets[start + offset / 4] >> ((offset % 4) * 8)) & 255;
}

function Network16(readonly packets: StorageBuffer<u32>, start: u32, offset: u32): u32 {
    return (Byte(packets, start, offset) << 8) | Byte(packets, start, offset + 1);
}

function Network32(readonly packets: StorageBuffer<u32>, start: u32, offset: u32): u32 {
    return (Network16(packets, start, offset) << 16) | Network16(packets, start, offset + 2);
}

function FindRule(readonly tables: StorageBuffer<u32>, offset: u32, count: u32, stride: u32,
    source: u32, destination: u32, port: u32): u32 {
    var found: u32 = Missing();
    for (let rule of BoundedIndices<512>(tables[7])) {
        if (rule < count && found == Missing()) {
            if ((source & tables[offset + stride + rule]) == tables[offset + rule]
                && (destination & tables[offset + stride * 3 + rule]) == tables[offset + stride * 2 + rule]
                && port >= tables[offset + stride * 4 + rule]
                && port <= tables[offset + stride * 5 + rule]) {
                found = rule;
            }
        }
    }
    return found;
}

@compute @numthreads(64, 1, 1)
function Main(@builtin(dispatchThreadId) thread: uint3,
    @binding(0) readonly packets: StorageBuffer<u32>,
    @binding(1) readonly tables: StorageBuffer<u32>,
    @binding(2) readwrite output: StorageBuffer<u32>): void {
    let index = thread.x;
    if (index >= tables[0]) { return; }
    let start = index * 16;
    let result = index * 5;
    output[result] = 3;
    output[result + 1] = Missing();
    output[result + 2] = 0;
    output[result + 3] = Missing();
    output[result + 4] = tables[3];
    let protocol = Byte(packets, start, 23);
    let length = Network16(packets, start, 16);
    if (Network16(packets, start, 12) != 2048 || Byte(packets, start, 14) != 69
        || (Network16(packets, start, 20) & 16383) != 0
        || length > 46 || length < 28 || (protocol != 6 && protocol != 17)) { return; }
    if (protocol == 6 && length < 40) { return; }
    var sum: u32 = 0;
    static for (let word of Indices<10>()) {
        sum = sum + Network16(packets, start, 14 + word * 2);
    }
    sum = (sum & 65535) + (sum >> 16);
    sum = (sum & 65535) + (sum >> 16);
    if (sum != 65535) { return; }
    let ttl = Byte(packets, start, 22);
    if (ttl <= 1) { output[result] = 4; return; }
    let source = Network32(packets, start, 26);
    let destination = Network32(packets, start, 30);
    let port = Network16(packets, start, 36);
    var protocolIndex: u32 = 0;
    if (protocol == 17) { protocolIndex = 1; }
    let bucket = protocolIndex * 16 + port / 4096;
    let header = tables[6] + bucket * 3;
    let offset = tables[header];
    let count = tables[header + 1];
    let stride = tables[header + 2];
    let matchingRule = FindRule(tables, offset, count, stride, source, destination, port);
    if (matchingRule != Missing()) {
        output[result + 3] = tables[offset + stride * 7 + matchingRule];
        if (tables[offset + stride * 6 + matchingRule] == 1) {
            output[result] = 6;
            return;
        }
    }
    if ((destination >> 16) != 2560) { output[result] = 5; return; }
    let group = (destination >> 8) & 255;
    if (group == 255) { output[result] = 5; return; }
    let bestPort = tables[tables[5] + group];
    if (bestPort == Missing()) { output[result] = 2; return; }
    let oldWord = (ttl << 8) | protocol;
    let newWord = ((ttl - 1) << 8) | protocol;
    var checksum = (65535 - Network16(packets, start, 24)) + (65535 - oldWord) + newWord;
    checksum = (checksum & 65535) + (checksum >> 16);
    checksum = (checksum & 65535) + (checksum >> 16);
    checksum = 65535 - checksum;
    output[result] = 0;
    output[result + 1] = bestPort;
    output[result + 2] = ((ttl - 1) << 16) | checksum;
}
