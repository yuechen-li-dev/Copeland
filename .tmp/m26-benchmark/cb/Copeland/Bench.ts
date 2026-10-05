import { runFib } from "./Fib";
import { runSieve } from "./Sieve";
import { runClosures } from "./Closures";
import { runNBody } from "./NBody";
import { runTrees } from "./Trees";
import { runObjects } from "./Objects";
import { runArrays } from "./Arrays";
import { runStrings } from "./Strings";

export function fibBench(bias: int): int { return runFib(bias); }
export function sieveBench(limit: int): int { return runSieve(limit); }
export function closuresBench(iterations: int): int { return runClosures(iterations); }
export function nbodyBench(steps: int): float { return runNBody(steps); }
export function treesBench(depth: int): int { return runTrees(depth); }
export function objectsBench(rounds: int, count: int): float { return runObjects(rounds, count); }
export function arraysBench(rounds: int, count: int): float { return runArrays(rounds, count); }
export function stringsBench(rounds: int, count: int): int { return runStrings(rounds, count); }
