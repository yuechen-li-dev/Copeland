record Address {
    city?: string;
}

record User {
    address?: Address;
}

function city(user: User): string {
    return user.address?.city ?? "Unknown";
}

function choose(value: Option<string>): string {
    return value ?? "fallback";
}
