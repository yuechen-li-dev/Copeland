interface I0 { f0: number; }
interface I1 { f1: number; }
interface I2 { f2: number; }
interface I3 { f3: number; }
interface I4 { f4: number; }
interface I5 { f5: number; }
interface I6 { f6: number; }
interface I7 { f7: number; }
interface I8 { f8: number; }

function pack<T0, T1, T2, T3, T4, T5, T6, T7, T8>(value: T0): T0 {
    return value;
}

function use<T extends I0 & I1 & I2 & I3 & I4 & I5 & I6 & I7 & I8>(value: T): number {
    return value.f0;
}
