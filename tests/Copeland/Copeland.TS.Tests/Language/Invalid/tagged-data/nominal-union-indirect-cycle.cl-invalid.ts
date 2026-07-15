record Branch {
  left: Tree;
}

record TreeLeaf {
  node: Node;
}

type Tree = Branch | TreeLeaf;

record Node {
  child: Tree;
}
