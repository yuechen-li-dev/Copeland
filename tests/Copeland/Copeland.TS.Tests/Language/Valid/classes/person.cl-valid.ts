class Person {
    public name: string;
    private normalizedName: string;
    age: number;

    constructor(name: string, age: number): Person {
        return {
            name,
            normalizedName: Person.normalize(name),
            age,
        };
    }

    private normalize(name: string): string {
        return name;
    }

    birthday(person: Person): Person {
        return person with {
            age: person.age + 1,
        };
    }
}

const john: Person = Person("John", 22);
const older: Person = Person.birthday(john);
const operation: (person: Person) => Person = Person.birthday;
const olderAgain: Person = operation(older);

function main(): number {
    const person: Person = Person("Ada", 41);
    const birthday: (person: Person) => Person = Person.birthday;
    const older: Person = birthday(person);
    return older.age;
}
