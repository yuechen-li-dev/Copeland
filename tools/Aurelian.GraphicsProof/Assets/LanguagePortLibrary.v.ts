export enum SampleValue { Missing, Present(value: f32) }

function Square(value: f32): f32 {
    static if (true) {
        let factor: f32 = value;
        var squared: f32 = 0.0;
        squared = factor * factor;
        return squared;
    } else {
        return unavailableHostOperation(value);
    }
}

export function Classify(value: f32): SampleValue {
    if (value < 0.0) {
        return SampleValue.Missing;
    }
    return SampleValue.Present(value);
}

export function Shade(value: f32): f32 {
    let gain: f32 = static Square(0.5);
    return match Classify(value) {
        SampleValue.Missing => 0.0,
        SampleValue.Present(payload) => payload.value * gain,
    };
}
