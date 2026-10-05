enum Tree {
    Leaf,
    Node(left: Tree, right: Tree),
}

function make(depth: int): Tree {
    if (depth == 0) {
        return Tree.Node(Tree.Leaf, Tree.Leaf);
    }
    return Tree.Node(make(depth - 1), make(depth - 1));
}

function check(tree: Tree): int {
    return match tree {
        Leaf => 0,
        Node(left, right) => 1 + check(left) + check(right),
    };
}

export function runTrees(maxDepth: int): int {
    const longLived: Tree = make(maxDepth);
    let total: int = 0;
    for (let depth: int = 4; depth <= maxDepth; depth = depth + 2) {
        let iterations: int = 1;
        for (let k: int = 0; k < maxDepth - depth + 4; k = k + 1) {
            iterations = iterations * 2;
        }
        let checksum: int = 0;
        for (let i: int = 0; i < iterations; i = i + 1) {
            checksum = checksum + check(make(depth));
        }
        total = total + checksum;
    }
    return total + check(longLived);
}
