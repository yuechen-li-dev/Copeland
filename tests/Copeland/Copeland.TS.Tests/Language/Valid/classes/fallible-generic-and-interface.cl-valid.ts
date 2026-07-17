enum PersonError {
    InvalidAge(age: number),
}

interface HasName {
    name: string;
}

record PersonEnvelope {
    people: Person[];
}

class Person {
    public name: string;
    private normalizedName: string;
    public age: number;

    constructor(name: string, age: number): Person ! PersonError {
        if (age < 0) {
            return err(PersonError.InvalidAge(age));
        }
        return ok({
            name,
            normalizedName: Person.normalize(name),
            age,
        });
    }

    private normalize(name: string): string {
        return name;
    }

    birthday(person: Person): Person {
        return person with { age: person.age + 1 };
    }

    identity<T>(value: T): T {
        return value;
    }
}

function label<T extends HasName>(value: T): string {
    return value.name;
}

function main(): string ! PersonError {
    const person: Person = Person("Ada", 41)?;
    const envelope: PersonEnvelope = { people: [person] };
    const older: Person = Person.birthday(person);
    const answer: number = Person.identity<number>(older.age);
    if (answer == 42) {
        return label(older);
    }
    return "wrong";
}
