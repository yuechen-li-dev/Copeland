export function setText(id, text) {
    const element = document.getElementById(id);
    if (element === null) {
        throw new Error(`Browser M0 host could not find text element '${id}'.`);
    }

    element.textContent = text;
}

export function onClick(id, transition) {
    const button = document.getElementById(id);
    if (!(button instanceof HTMLButtonElement)) {
        throw new Error(`Browser M0 host expected button '${id}'.`);
    }

    let state = 0;
    button.addEventListener("click", () => {
        state = transition(state);
    });
}
