enum Choice {
  A,
  B,
}

function value(choice: Choice): number {
  return match choice {
    A => 1,
    B => 2,
  };
}
