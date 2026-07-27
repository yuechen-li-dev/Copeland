import { createElement } from "react";
import { Dialog } from "@base-ui-components/react/dialog";
import { AppEvent, AppState } from "./State";

function SendDialogEvent(open: boolean, send: (event: AppEvent) => void): void {
    if (open) {
        send(AppEvent.OpenDialog);
    } else {
        send(AppEvent.CloseDialog);
    }
}

export function View(state: AppState, send: (event: AppEvent) => void): ReactNode {
    return (
        <main>
            <h1>Copeland TS + Base UI</h1>
            <button id="open-dialog" onClick={capture { send } () => SendDialogEvent(true, send)}>
                Open dialog
            </button>
            <p id="state">{if state.dialogOpen { "Dialog open" } else { "Dialog closed" }}</p>
            <Dialog.Root
                open={state.dialogOpen}
                onOpenChange={capture { send } (open: boolean) => SendDialogEvent(open, send)}
            >
                <Dialog.Portal>
                    <Dialog.Backdrop className="dialog-backdrop" />
                    <Dialog.Popup className="dialog-popup">
                        <Dialog.Title>Third-party React works</Dialog.Title>
                        <Dialog.Description>Base UI is running inside Copeland TS.</Dialog.Description>
                        <Dialog.Close>Close</Dialog.Close>
                    </Dialog.Popup>
                </Dialog.Portal>
            </Dialog.Root>
        </main>
    );
}
