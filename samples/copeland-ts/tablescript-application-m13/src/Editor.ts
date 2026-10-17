export type CopelandProfile = "JavaScript";

import { WorkbookView, StockError, initialWorkbook, applyStockEdit } from "./Application";

export record EditorState {
    view: WorkbookView;
    draftGeneration: int;
    productId: int;
    units: int;
    error: Option<StockError>;
}

export enum EditorEvent {
    Select(productId: int, units: int),
    SetProductId(value: int),
    SetUnits(value: int),
    Apply,
    Reset,
}

export function initialEditor(): EditorState {
    return { view: initialWorkbook(), draftGeneration: 0, productId: 100, units: 4, error: None };
}

export function reduceEditor(state: EditorState, event: EditorEvent): EditorState {
    return match event {
        Select(productId, units) => state with { productId, units, error: None },
        SetProductId(value) => state with { productId: value },
        SetUnits(value) => state with { units: value },
        Apply => match applyStockEdit(state.view, state.productId, state.units) {
            ok(view) => state with { view, error: None },
            err(error) => state with { error: Some(error) },
        },
        Reset => initialEditor() with { draftGeneration: state.draftGeneration + 1 },
    };
}
