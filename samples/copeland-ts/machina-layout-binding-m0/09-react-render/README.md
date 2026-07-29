`Page.tsx` is the minimal record-shaped layout proof. It calls the generated
`PageBinding()` ReactNode factory; components have no layout-class parameter.
Its declared layout nodes become neutral `div` hosts with generated classes,
and bound values become slot-host children.
