// expect: COPE-OPTION-0005
record User {
    name: string;
}

function invalid(user: User): Option<string> {
    return user?.name;
}
