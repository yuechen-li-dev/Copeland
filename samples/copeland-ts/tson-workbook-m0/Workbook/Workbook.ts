enum Department {
    Engineering,
    Sales,
}

export record table Scores {
    employeeId: int = [1, 2, 3];
    name: string = ["Alice", "Bob", "Carol"];
    score: number = [95.0, 81.5, 91.0];
}

export record table Employees {
    id: int = [1, 2, 3];
    name: string = ["Alice", "Bob", "Carol"];
    department: Department = [
        Department.Engineering,
        Department.Sales,
        Department.Engineering
    ];
}

record ScoreView {
    name: string;
    score: number;
}

function data(): Scores {
    return Scores;
}

function employees(): Employees {
    return Employees;
}

function revisedScores(): Scores {
    return Scores with {
        score: [95.0, 84.0, 91.0]
    };
}

function originalBobScore(): number {
    return Scores.score[1]!;
}

function revisedBobScore(): number {
    return revisedScores().score[1]!;
}

function averageScore(scores: Scores): number {
    return (
        scores.score[0]!
        + scores.score[1]!
        + scores.score[2]!
    ) / 3.0;
}

function highScores(scores: Scores): ScoreView[] {
    return [
        {
            name: scores.name[0]!,
            score: scores.score[0]!
        },
        {
            name: scores.name[2]!,
            score: scores.score[2]!
        }
    ];
}

function engineeringAverage(): number {
    const firstIsEngineering: number = match Employees.department[0]! {
        Engineering => Scores.score[0]!,
        Sales => 0.0,
    };
    const thirdIsEngineering: number = match Employees.department[2]! {
        Engineering => Scores.score[2]!,
        Sales => 0.0,
    };
    return (firstIsEngineering + thirdIsEngineering) / 2.0;
}
