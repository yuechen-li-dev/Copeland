record table Greetings {
    value: string = ["Welcome"];
}

export function domainGreeting(name: string): string {
    return match Greetings[0] {
        ok(row) => `${row.value}, ${name}`,
        err(error) => `Welcome, ${name}`,
    };
}
