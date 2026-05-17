enum Choice {
  A,
  B,
}

function f(choice: Choice): number {
  return match choice {
    A => 1,
    B => 2,
  };
}
