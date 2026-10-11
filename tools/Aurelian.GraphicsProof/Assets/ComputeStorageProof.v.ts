record Sample {
    position: float3;
    id: u32;
}
enum Command {
    Move(delta: float3),
    Stop,
}
function Apply(readonly input: StorageBuffer<Sample>, readwrite output: StorageBuffer<Sample>, command: Command): void {
    match command {
        case Command.Move(move) => {
            let value = input[0];
            output[0] = value with { position: value.position + move.delta };
            output[0].position.z = 33.0;
            return;
        }
        case Command.Stop => { return; }
    }
}
function Select(readwrite output: StorageBuffer<Sample>): Command {
    Store<u32>(output, 10, Load<u32>(output, 10) + 1);
    return Command.Stop;
}
@compute
@numthreads(1, 1, 1)
function Main(@binding(0) readonly input: StorageBuffer<Sample>,
    @binding(1) readwrite output: StorageBuffer<Sample>): void {
    Apply(input, output, Command.Move(float3(1.0, 2.0, 3.0)));
    let invalidOffset: u32 = input[0].id;
    Store<u32>(output, 4, WordCount<Sample>());
    Store<u32>(output, 5, Load<u32>(input, 3));
    Store<u32>(output, 6, U32(input[invalidOffset].position.x));
    Store<u32>(output, 7, Load<u32>(input, invalidOffset));
    Store<f32>(output, 8, Load<f32>(input, 0));
    Store<u32>(output, 9, U32(Load<Sample>(input, invalidOffset).position.x));
    Store<u32>(output, 10, 0);
    match Select(output) {
        case Command.Move(move) => { Store<u32>(output, 10, 900); }
        case Command.Stop => {}
    }
    Store<u32>(output, 11, 99);
    output[invalidOffset] = input[0];
    output[invalidOffset].id = 999;
    Store<Sample>(output, 11, input[0]);
    Store<u32>(output, invalidOffset, 999);
}
