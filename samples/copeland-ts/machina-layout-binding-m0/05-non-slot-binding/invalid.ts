layout type Shell { column root { slot body; } }
layout Page<0px, 0px> satisfies Shell { width: 100px; height: 100px; column root { slot body; } }
bind Page { root: Root(); body: Body(); }
