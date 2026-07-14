type UserAlias = User;

record User {
    id: number;
}

function invalid(): User {
    return UserAlias(1);
}
