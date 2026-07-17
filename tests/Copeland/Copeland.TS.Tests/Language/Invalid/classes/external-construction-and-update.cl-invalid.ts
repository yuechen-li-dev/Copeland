class Person {
    public name: string;
    constructor(name: string): Person {
        return { name };
    }
}

function fake(person: Person): Person {
    const counterfeit: Person = { name: "fake" };
    return person with { name: counterfeit.name };
}
