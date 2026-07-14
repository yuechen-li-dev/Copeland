const $schema: string = "copeland://fixtures/table/enum";
enum State { Missing, Named(label: string), }
record table States { state: State = [State.Missing, State.Named("ready")]; }
const $value = States;
