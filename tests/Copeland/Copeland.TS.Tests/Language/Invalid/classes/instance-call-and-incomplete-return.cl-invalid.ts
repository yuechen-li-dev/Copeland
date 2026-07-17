class Person {
    public name: string;
    public age: number;
    constructor(name: string, age: number): Person {
        return { name };
    }

    birthday(person: Person): Person {
        return person;
    }
}

function invalid(person: Person): Person {
    return person.birthday();
}
