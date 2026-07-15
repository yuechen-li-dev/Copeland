interface Positioned {
    x: number;
    y: number;
}

interface Named {
    name: string;
}

record table Samples {
    x: [1];
    y: [2];
    name: string = ["row"];
}

function describe<T extends Positioned & Named>(value: T): string {
    return value.name;
}

const row: Samples.Row = Samples[0]!;
const answer: string = describe(row);
