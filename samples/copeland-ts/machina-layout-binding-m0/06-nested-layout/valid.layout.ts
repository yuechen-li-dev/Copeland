layout type Shell { row root { column sidebar { slot navigation; } column main { slot hero; slot footer; } } }
layout Page<0px, 0px> satisfies Shell {
    width: 1200px;
    height: 800px;
    row root { column sidebar { slot navigation; } column main { slot hero; slot footer; } }
}
