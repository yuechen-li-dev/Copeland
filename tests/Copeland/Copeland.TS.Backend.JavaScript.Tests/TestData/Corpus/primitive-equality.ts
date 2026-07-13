function booleanEqual(): boolean {
  return true == true;
}

function booleanNotEqual(): boolean {
  return true != false;
}

function numberEqual(): boolean {
  return 42 == 42;
}

function numberNotEqual(): boolean {
  return 42 != 41;
}

function nanEqual(): boolean {
  const nan: number = 0 / 0;
  return nan == nan;
}

function nanNotEqual(): boolean {
  const nan: number = 0 / 0;
  return nan != nan;
}

function signedZeroEqual(): boolean {
  const positiveZero: number = 0;
  const negativeZero: number = 0 * (0 - 1);
  return positiveZero == negativeZero;
}

function signedZeroNotEqual(): boolean {
  const positiveZero: number = 0;
  const negativeZero: number = 0 * (0 - 1);
  return positiveZero != negativeZero;
}
