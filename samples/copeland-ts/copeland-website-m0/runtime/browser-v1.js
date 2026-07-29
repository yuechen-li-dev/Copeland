export function getMountElement(id) {
  const element = document.getElementById(id);
  if (element === null) {
    throw new Error("Copeland browser host could not find mount element '" + id + "'.");
  }

  return element;
}

export function dispatchReact(initialState, reduce, render) {
  let currentState = initialState;
  const send = (event) => {
    currentState = reduce(currentState, event);
    render(currentState, send);
  };

  render(currentState, send);
  return send;
}

export function copyText(text, onSuccess, onFailure) {
  if (!navigator.clipboard || typeof navigator.clipboard.writeText !== "function") {
    onFailure();
    return;
  }

  navigator.clipboard.writeText(text).then(onSuccess, onFailure);
}
