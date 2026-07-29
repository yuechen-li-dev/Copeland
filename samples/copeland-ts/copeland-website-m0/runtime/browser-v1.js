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
  if (navigator.clipboard && typeof navigator.clipboard.writeText === "function") {
    navigator.clipboard.writeText(text).then(onSuccess, () => copyTextWithDocument(text, onSuccess, onFailure));
    return;
  }

  copyTextWithDocument(text, onSuccess, onFailure);
}

function copyTextWithDocument(text, onSuccess, onFailure) {
  const element = document.createElement("textarea");
  element.value = text;
  element.setAttribute("readonly", "");
  element.style.position = "fixed";
  element.style.opacity = "0";
  document.body.appendChild(element);
  element.select();

  const copied = document.execCommand("copy");
  element.remove();
  if (copied) onSuccess();
  else onFailure();
}

export function getViewportWidth() {
  return window.innerWidth;
}

export function subscribeViewport(onChange) {
  window.addEventListener("resize", onChange, { passive: true });
  window.addEventListener("orientationchange", onChange, { passive: true });

  return () => {
    window.removeEventListener("resize", onChange);
    window.removeEventListener("orientationchange", onChange);
  };
}
