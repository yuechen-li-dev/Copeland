layout type Shell { slot body; }
layout Page<0px, 0px> satisfies Shell { width: 100px; height: 100px; slot body; }
bind Page { body: Body(); body: Replacement(); }
