enum Choice {
  Yes,
  No,
}

function value(choice: Choice): number {
  return match choice {
    Yes => 1,
  };
}
