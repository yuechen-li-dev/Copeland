class Person {
    public name: string;
    private secret: string;
    constructor(name: string): Person {
        return { name, secret: name };
    }
}

function leak(person: Person): string {
    return person.secret;
}
