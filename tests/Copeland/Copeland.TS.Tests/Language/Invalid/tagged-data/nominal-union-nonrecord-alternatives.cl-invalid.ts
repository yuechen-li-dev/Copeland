record Circle { radius: number; }
enum Existing { Value, }
interface Required { value: number; }
record table Samples { value: [1]; }
type Alias = Circle;
type BadAlias = Alias | Circle;
type BadEnum = Existing | Circle;
type BadInterface = Required | Circle;
type BadTable = Samples | Circle;
type BadUnknown = Missing | Circle;
