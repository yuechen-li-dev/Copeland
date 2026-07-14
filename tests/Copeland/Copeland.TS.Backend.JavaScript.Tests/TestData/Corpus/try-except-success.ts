function read(): number ! string {
    return ok(4);
}

function main(): number {
    return try {
        const value: number = read()?;
        value + 1
    } except (error) {
        0
    };
}
