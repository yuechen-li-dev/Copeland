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
    return scores.score.average();
}

function highScores(scores: Scores): ScoreView[] {
    return scores.rows()
        .where(row => row.score >= 90.0)
        .select(scoreView);
}

function scoreView(row: Scores.Row): ScoreView {
    return {
        name: row.name,
        score: row.score
    };
}

function scoreForEmployee(employeeId: int): number {
    for (const score of Scores.rows()) {
        if (score.employeeId == employeeId) {
            return score.score;
        }
    }
    return 0.0;
}

function engineeringAverage(): number {
    let total: number = 0.0;
    let count: int = 0;
    for (const employee of Employees.rows()) {
        const isEngineering: boolean = match employee.department {
            Engineering => true,
            Sales => false,
        };
        if (isEngineering) {
            total = total + scoreForEmployee(employee.id);
            count = count + 1;
        }
    }
    return total / Float.From(count);
}
