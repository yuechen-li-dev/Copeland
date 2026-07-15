interface Positioned {
  x: number;
  y: number;
}

function discard<T extends Positioned>(value: number): void {
}

discard(42);
