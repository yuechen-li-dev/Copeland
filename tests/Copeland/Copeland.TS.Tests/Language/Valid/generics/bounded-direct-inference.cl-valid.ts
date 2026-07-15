type UserId = number;

interface Positioned {
  x: number;
  y: number;
}

record Point {
  x: number;
  y: number;
}

function identity<T>(value: T): T {
  return value;
}

function chooseLeft<T, U>(left: T, right: U): T {
  return left;
}

function takeArray<T>(values: T[], fallback: T): T {
  return fallback;
}

function sum<T extends Positioned>(value: T): number {
  return value.x + value.y;
}

const point: Point = { x: 20, y: 22 };
const id: UserId = 42;
const a: number = identity(42);
const b: string = identity("value");
const c: number = identity(id);
const d: number = chooseLeft(42, "ignored");
const e: number = takeArray([42], 0);
const f: number = sum(point);
const g: number = identity<number>(42);
