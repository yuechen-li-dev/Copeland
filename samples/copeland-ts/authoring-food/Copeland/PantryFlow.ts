export flow PantryRun -> number {
    board {
        servings: number = 0;
    }

    event Add(amount: number);
    event Close();

    state Planning initial {
        on Add(amount) -> Planning {
            board.servings = board.servings + amount;
        };
        on Close() -> Completed;
    }

    state Completed {
        finish board.servings;
    }
}
