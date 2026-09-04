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
            board.sequence = nextSequence(board.sequence);
        };
        on Cancel() -> Cancelled;
    }

    state Staging {
        on Tick(amount) when amount > 0 -> Staging {
            board.total = board.total + amount;
            board.sequence = nextSequence(board.sequence);
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
