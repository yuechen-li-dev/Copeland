export function setText(id, text) {
  const element = document.getElementById(id);
  if (element === null) throw new Error("Copeland browser host could not find text element '" + id + "'.");
  element.textContent = text;
}

export function onClick(id, callback) {
  const button = document.getElementById(id);
  if (!(button instanceof HTMLButtonElement)) throw new Error("Copeland browser host expected button '" + id + "'.");
  button.addEventListener("click", callback);
}

export function dispatch(initialState, reduce, render) {
  let currentState = initialState;
  render(currentState);
  return event => {
    const nextState = reduce(currentState, event);
    if (nextState !== currentState) {
      currentState = nextState;
      render(currentState);
    }
  };
}

export function getMountElement(id) {
  const element = document.getElementById(id);
  if (element === null) throw new Error("Copeland React host could not find mount element '" + id + "'.");
  return element;
}

export function dispatchReact(initialState, reduce, render) {
  let currentState = initialState;
  const send = event => {
    currentState = reduce(currentState, event);
    render(currentState, send);
  };
  render(currentState, send);
  return send;
}
