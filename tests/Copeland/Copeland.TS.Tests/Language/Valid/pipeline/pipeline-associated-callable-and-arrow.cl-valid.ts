type Operation = (value: number) => number;

class Person {
    name: string;
    age: number;

    constructor(name: string, age: number): Person {
        return { name, age };
    }

    birthday(person: Person): Person {
        return person with { age: person.age + 1 };
    }
}

function add(value: number, amount: number): number { return value + amount; }
function main(): number {
    const operation: Operation = value => value * 2;
    const older: Person = Person("Ada", 41) |> Person.birthday;
    const doubled: number = 21 |> operation;
    return doubled |> capture { older } (value: number) => add(value, older.age);
}
