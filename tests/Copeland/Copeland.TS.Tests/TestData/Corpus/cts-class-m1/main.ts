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
    age: number;

    constructor(name: string, age: number): Person ! PersonError {
        if (age < 0) {
            return err(PersonError.InvalidAge(age));
        }
        return ok({ name, normalizedName: Person.normalize(name), age });
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

function main(): string {
    return try {
        const person: Person = Person("Ada", 41)?;
        const envelope: PersonEnvelope = { people: [person] };
        const birthday: (person: Person) => Person = Person.birthday;
        const older: Person = birthday(person);
        const age: number = Person.identity<number>(older.age);
        label(older)
    } except (error) {
        "recovered"
    };
}
