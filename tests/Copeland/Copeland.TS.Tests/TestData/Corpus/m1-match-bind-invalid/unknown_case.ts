enum Choice { A, }
function f(choice: Choice): number {
  return match choice {
    B => 1,
  };
}
