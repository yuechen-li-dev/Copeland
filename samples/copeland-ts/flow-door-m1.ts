flow Door -> number ! string {
    board {
        attempts: number = 0;
    }

    event Open();
    event Reset();

    state Closed initial {
        on Open() -> Opened {
            board.attempts = board.attempts + 1;
        };
    }

    state Opened {
        on Reset() -> Closed;
        on Open() -> Completed;
    }

    state Completed {
        finish board.attempts;
    }
}
