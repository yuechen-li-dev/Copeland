type UserId = NumericId;
type NumericId = number;
type UserAlias = User;
type Users = User[];
type ParseResult = User ! string;

record User {
    id: UserId;
    name: string;
}

enum Event {
    Created(user: UserAlias),
}

function identity(id: UserId): number {
    const raw: number = id;
    return raw;
}

function make(): UserAlias {
    return { id: 42, name: "Ada" };
}

function parse(): ParseResult {
    return ok({ id: 42, name: "Ada" });
}

function same(left: UserId, right: NumericId): boolean {
    return left == right;
}

function all(): Users {
    return [make()];
}
