record Node {
  child: Tree;
}

record Leaf {
  value: number;
}

type Tree = Node | Leaf;
