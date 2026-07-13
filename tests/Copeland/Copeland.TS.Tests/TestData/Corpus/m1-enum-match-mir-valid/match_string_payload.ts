enum Status {
  Idle,
  Loaded(name: string),
}

function label(status: Status): string {
  return match status {
    Idle => "idle",
    Loaded(name) => name,
  };
}
