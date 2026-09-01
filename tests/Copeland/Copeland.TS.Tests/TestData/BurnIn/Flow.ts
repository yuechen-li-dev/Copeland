enum AuditKind {
    Started,
    Retried(count: int),
    Completed(total: int),
    Failed(reason: string),
}

record AuditEntry {
    kind: AuditKind;
    sequence: int;
}

function nextSequence(value: int): int {
    return value + 1;
}

flow Delivery -> int ! string {
    board {
        attempts: int = 0;
        total: int = 0;
        sequence: int = 0;
        accepted: boolean = false;
    }

    event Start(amount: int);
    event Retry(amount: int);
    event Accept(amount: int);
    event Reject(code: int);
    event Reset();
    event Cancel();
    event Tick(amount: int);

    state Idle initial {
        on Start(amount) when amount > 0 -> Staging {
            board.total = amount;
            board.attempts = board.attempts + 1;
            board.sequence = board.sequence + 1;
        };
        on Cancel() -> Cancelled;
    }

    state Staging {
        on Tick(amount) when amount > 0 -> Staging {
            board.total = board.total + amount;
            board.sequence = board.sequence + 1;
        };
        on Retry(amount) when amount > 0 -> Retrying {
            board.attempts = board.attempts + 1;
            board.total = board.total + amount;
        };
        on Accept(amount) when amount >= 0 -> Accepted {
            board.total = board.total + amount;
            board.accepted = true;
        };
        on Reject(code) -> Rejected;
    }

    state Retrying {
        on Tick(amount) when amount > 0 -> Retrying {
            board.total = board.total + amount;
        };
        on Accept(amount) when amount >= 0 -> Accepted {
            board.total = board.total + amount;
            board.accepted = true;
        };
        on Reject(code) -> Rejected;
        on Cancel() -> Cancelled;
    }

    state Accepted {
        on Tick(amount) when amount > 0 -> Completing {
            board.total = board.total + amount;
        };
        on Reset() -> Idle {
            board.total = 0;
            board.attempts = 0;
            board.accepted = false;
        };
    }

    state Completing {
        on Accept(amount) -> Completed {
            board.total = board.total + amount;
        };
        on Reject(code) -> Rejected;
    }

    state Completed {
        finish board.total;
    }

    state Rejected {
        fail "delivery rejected";
    }

    state Cancelled {
        fail "delivery cancelled";
    }
}

function auditScore(entry: AuditEntry): int {
    return match entry.kind {
        Started => entry.sequence,
        Retried(count) => entry.sequence + count,
        Completed(total) => entry.sequence + total,
        Failed(reason) => entry.sequence + reason.length,
    };
}

function main(): int {
    const entry: AuditEntry = {
        kind: AuditKind.Completed(42),
        sequence: 3
    };
    return auditScore(entry);
}
