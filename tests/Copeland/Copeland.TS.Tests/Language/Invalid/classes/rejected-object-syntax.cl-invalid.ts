class Person extends Base {
    readonly name: string = "Ada";
    constructor(name: string): Person {
        return { name };
    }

    birthday(): Person {
        return this;
    }
}

function create(): Person {
    return new Person("Ada");
}
