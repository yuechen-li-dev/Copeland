// Sixteen packet teams per workgroup; 32 lanes cooperate on each ordered rule search.
// Correctness uses workgroup storage/barriers, without assuming subgroup width.
function Missing(): u32 {
    return (65535 << 16) | 65535;
}

function Byte(readonly packets: StorageBuffer<u32>, start: u32, offset: u32): u32 {
    return (packets[start + offset / 4] >> ((offset % 4) * 8)) & 255;
}

function Network16(readonly packets: StorageBuffer<u32>, start: u32, offset: u32): u32 {
    return (Byte(packets, start, offset) << 8) | Byte(packets, start, offset + 1);
}

function Network32(readonly packets: StorageBuffer<u32>, start: u32, offset: u32): u32 {
    return (Network16(packets, start, offset) << 16) | Network16(packets, start, offset + 2);
}

function Parse(readonly packets: StorageBuffer<u32>, start: u32): u32 {
    let protocol = Byte(packets, start, 23);
    let length = Network16(packets, start, 16);
    if (Network16(packets, start, 12) != 2048 || Byte(packets, start, 14) != 69
        || (Network16(packets, start, 20) & 16383) != 0
        || length > 46 || length < 28 || (protocol != 6 && protocol != 17)) { return 3; }
    if (protocol == 6 && length < 40) { return 3; }
    var sum: u32 = 0;
    static for (let word of Indices<10>()) {
        sum = sum + Network16(packets, start, 14 + word * 2);
    }
    sum = (sum & 65535) + (sum >> 16);
    sum = (sum & 65535) + (sum >> 16);
    if (sum != 65535) { return 3; }
    if (Byte(packets, start, 22) <= 1) { return 4; }
    return 0;
}

@compute @numthreads(512, 1, 1)
function Main(@builtin(groupId) group: uint3, @builtin(groupIndex) thread: u32,
    @workgroup readwrite keys: WorkgroupArray<u32, 256>,
    @workgroup readwrite matches: WorkgroupArray<u32, 512>,
    @binding(0) readonly packets: StorageBuffer<u32>,
    @binding(1) readonly tables: StorageBuffer<u32>,
    @binding(2) readwrite output: StorageBuffer<u32>): void {
    let packetSlot = thread / 32;
    let lane = thread % 32;
    let index = group.x * 16 + packetSlot;
    let key = packetSlot;
    let result = index * 5;
    // Field-major shared keys make parser lanes write adjacent words; each
    // rule team then reads one broadcast key, avoiding strided shared stores.
    if (thread < 16) {
        let parserIndex = group.x * 16 + thread;
        let parserKey = thread;
        let parserResult = parserIndex * 5;
        static for (let field of Indices<16>()) {
            keys[field * 16 + parserKey] = 0;
        }
        keys[parserKey] = 3;
        if (parserIndex < tables[0]) {
            let start = parserIndex * 16;
            output[parserResult] = 3;
            output[parserResult + 1] = Missing();
            output[parserResult + 2] = 0;
            output[parserResult + 3] = Missing();
            output[parserResult + 4] = tables[3];
            keys[parserKey] = Parse(packets, start);
            if (keys[parserKey] == 0) {
                keys[16 + parserKey] = Network32(packets, start, 26);
                keys[32 + parserKey] = Network32(packets, start, 30);
                keys[48 + parserKey] = Network16(packets, start, 36);
                keys[64 + parserKey] = Byte(packets, start, 23);
                keys[80 + parserKey] = Byte(packets, start, 22);
                keys[96 + parserKey] = Network16(packets, start, 24);
                var protocolIndex: u32 = 0;
                if (keys[64 + parserKey] == 17) { protocolIndex = 1; }
                let bucket = protocolIndex * 16 + keys[48 + parserKey] / 4096;
                let header = tables[6] + bucket * 3;
                keys[112 + parserKey] = tables[header];
                keys[128 + parserKey] = tables[header + 1];
                keys[144 + parserKey] = tables[header + 2];
            }
        }
    }
    WorkgroupBarrier();
    let source = keys[16 + key];
    let destination = keys[32 + key];
    let port = keys[48 + key];
    let offset = keys[112 + key];
    let count = keys[128 + key];
    let stride = keys[144 + key];
    var first: u32 = Missing();
    for (let tile of BoundedIndices<16>((tables[7] + 31) / 32)) {
        let rule = tile * 32 + lane;
        if (keys[key] == 0 && rule < count && first == Missing()) {
            // Neighboring lanes read neighboring rules: the existing SoA index
            // now supplies coalesced loads instead of independent serial scans.
            if ((source & tables[offset + stride + rule]) == tables[offset + rule]
                && (destination & tables[offset + stride * 3 + rule]) == tables[offset + stride * 2 + rule]
                && port >= tables[offset + stride * 4 + rule]
                && port <= tables[offset + stride * 5 + rule]) {
                first = rule;
            }
        }
    }
    matches[thread] = first;
    WorkgroupBarrier();
    static for (let stage of Indices<5>()) {
        let distance = 16 >> stage;
        if (lane < distance) {
            let other = matches[thread + distance];
            if (other < matches[thread]) {
                matches[thread] = other;
            }
        }
        WorkgroupBarrier();
    }
    // All invocations, including padding and rejected packets, crossed every wait.
    if (lane == 0 && index < tables[0]) {
        if (keys[key] != 0) {
            output[result] = keys[key];
            return;
        }
        let winningRule = matches[thread];
        if (winningRule != Missing()) {
            output[result + 3] = tables[offset + stride * 7 + winningRule];
            if (tables[offset + stride * 6 + winningRule] == 1) {
                output[result] = 6;
                return;
            }
        }
        if ((destination >> 16) != 2560) { output[result] = 5; return; }
        let route = (destination >> 8) & 255;
        if (route == 255) { output[result] = 5; return; }
        let bestPort = tables[tables[5] + route];
        if (bestPort == Missing()) { output[result] = 2; return; }
        let ttl = keys[80 + key];
        let oldWord = (ttl << 8) | keys[64 + key];
        let newWord = ((ttl - 1) << 8) | keys[64 + key];
        var checksum = (65535 - keys[96 + key]) + (65535 - oldWord) + newWord;
        checksum = (checksum & 65535) + (checksum >> 16);
        checksum = (checksum & 65535) + (checksum >> 16);
        output[result] = 0;
        output[result + 1] = bestPort;
        output[result + 2] = ((ttl - 1) << 16) | (65535 - checksum);
    }
}
