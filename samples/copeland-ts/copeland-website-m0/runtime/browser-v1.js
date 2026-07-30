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

const textFitHosts = new WeakMap();
let textFitFrame = 0;

// The browser owns this operation: it measures shaped text in one explicitly
// declared host box. It never changes a compiler-owned layout frame.
export function scheduleTextFit() {
  if (textFitFrame !== 0) return;
  textFitFrame = requestAnimationFrame(() => {
    textFitFrame = 0;
    for (const host of document.querySelectorAll("[data-machina-text-fit]")) {
      registerTextFitHost(host);
    }
  });
}

export function installTextFit() {
  const mount = document.getElementById("app");
  if (mount === null) return;
  const observer = new MutationObserver(() => scheduleTextFit());
  observer.observe(mount, { childList: true, subtree: true });
  scheduleTextFit();
}

function registerTextFitHost(host) {
  if (!(host instanceof HTMLElement)) return;
  const target = host.querySelector(".text-fit-target");
  if (!(target instanceof HTMLElement)) {
    host.dataset.machinaTextFit = "fallback";
    return;
  }

  const existing = textFitHosts.get(host);
  if (existing?.target === target) {
    fitText(host, target);
    return;
  }
  existing?.observer.disconnect();
  const observer = new ResizeObserver(() => scheduleHostFit(host, target));
  observer.observe(host);
  textFitHosts.set(host, { target, observer });
  fitText(host, target);
  if (document.fonts?.ready) {
    document.fonts.ready.then(() => scheduleHostFit(host, target)).catch(() => {});
  }
}

function scheduleHostFit(host, target) {
  requestAnimationFrame(() => {
    if (host.isConnected && target.isConnected) fitText(host, target);
  });
}

function fitText(host, target) {
  const preferred = positiveNumber(host.dataset.machinaTextPreferredSize);
  const minimum = positiveNumber(host.dataset.machinaTextMinimumSize);
  const lines = Math.max(1, Number.parseInt(host.dataset.machinaTextLines ?? "1", 10));
  if (preferred === null || minimum === null || minimum > preferred || host.clientWidth === 0 || host.clientHeight === 0) {
    host.dataset.machinaTextFit = "fallback";
    return;
  }

  const wrapping = host.dataset.machinaTextWrap !== "nowrap";
  prepareForMeasurement(target, host, wrapping);
  let selected = minimum;
  let fits = false;
  for (let size = preferred; size >= minimum; size -= 1) {
    target.style.fontSize = `${size}px`;
    if (fitsAtSize(target, host, lines)) {
      selected = size;
      fits = true;
      break;
    }
  }
  target.style.fontSize = `${selected}px`;
  applyPresentation(target, host, lines, wrapping, fits);
  host.dataset.machinaTextSize = `${selected}`;
  host.dataset.machinaTextFit = fits ? "fit" : fallbackStatus(host.dataset.machinaTextFallback);
}

function prepareForMeasurement(target, host, wrapping) {
  target.style.width = "100%";
  target.style.height = `${host.clientHeight}px`;
  target.style.maxHeight = `${host.clientHeight}px`;
  target.style.margin = "0";
  target.style.overflow = "hidden";
  target.style.whiteSpace = wrapping ? "normal" : "nowrap";
  target.style.display = "block";
  target.style.webkitLineClamp = "unset";
  target.style.webkitBoxOrient = "unset";
}

function fitsAtSize(target, host, lines) {
  const lineHeight = Number.parseFloat(getComputedStyle(target).lineHeight);
  const lineLimit = Number.isFinite(lineHeight) ? (lineHeight * lines) + 1 : host.clientHeight;
  return target.scrollWidth <= target.clientWidth + 1
    && target.scrollHeight <= Math.min(host.clientHeight, lineLimit) + 1;
}

function applyPresentation(target, host, lines, wrapping, fits) {
  target.style.height = "100%";
  target.style.maxHeight = "100%";
  target.style.whiteSpace = wrapping ? "normal" : "nowrap";
  target.style.overflow = "hidden";
  if (wrapping) {
    target.style.display = "-webkit-box";
    target.style.webkitBoxOrient = "vertical";
    target.style.webkitLineClamp = `${lines}`;
  }
  if (!fits && host.dataset.machinaTextFallback === "overflow") {
    target.style.overflow = "visible";
  }
  if (!fits && host.dataset.machinaTextFallback === "ellipsis") {
    target.style.textOverflow = "ellipsis";
  }
}

function fallbackStatus(fallback) {
  return fallback === "clip" ? "minimum-overflow" : "fallback";
}

function positiveNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 ? number : null;
}
