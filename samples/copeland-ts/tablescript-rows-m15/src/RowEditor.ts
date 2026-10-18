export type CopelandProfile = "Copeland";

import { WorkbookView } from "./Application";
import { EditorState, EditorEvent, initialEditor, reduceEditor } from "./ExistingEditor";
import { EditableProduct, PriceError, editableProduct } from "./PriceApplication";
import { StockError } from "./Application";
import { Draft, ParseError, beginDraft, editDraft, revealDraft, parseIntText, parseCentsText, formatInt, formatCents } from "./Editing";
import { NewProduct, RowError, createProduct, removeProduct, validateView, limitWorkbook } from "./Rows";

export record ProductDraft {
    selected: EditableProduct;
    stock: Draft<int, ParseError>;
    price: Draft<int, ParseError>;
    stockError: Option<StockError>;
    priceError: Option<PriceError>;
}

export record CreationDraft {
    productId: Draft<int, ParseError>;
    name: string;
    supplierId: Draft<int, ParseError>;
    stock: Draft<int, ParseError>;
    price: Draft<int, ParseError>;
}

export record RowState {
    view: WorkbookView;
    focus: Option<ProductDraft>;
    creation: CreationDraft;
    nextId: int;
    error: Option<RowError>;
}

export enum RowEvent {
    Select(productId: int),
    Existing(event: EditorEvent),
    ChangeId(text: string),
    ChangeName(text: string),
    ChangeSupplier(text: string),
    ChangeStock(text: string),
    ChangePrice(text: string),
    Create,
    CancelCreate,
    Remove(productId: int),
    Reset,
}

function draftFromEditor(editor: EditorState): ProductDraft {
    return { selected: editor.selected, stock: editor.stock, price: editor.price,
        stockError: editor.stockError, priceError: editor.priceError };
}

export function existingState(view: WorkbookView, draft: ProductDraft): EditorState {
    return { view, selected: draft.selected, stock: draft.stock, price: draft.price,
        stockError: draft.stockError, priceError: draft.priceError };
}

function beginCreation(productId: int): CreationDraft {
    return {
        productId: beginDraft<int, ParseError>(productId, formatInt), name: "",
        supplierId: beginDraft<int, ParseError>(30, formatInt),
        stock: beginDraft<int, ParseError>(0, formatInt),
        price: beginDraft<int, ParseError>(500, formatCents),
    };
}

export function initialRows(): RowState {
    const editor: EditorState = initialEditor();
    return { view: editor.view, focus: Some(draftFromEditor(editor)), creation: beginCreation(106), nextId: 106, error: None };
}

function selectProduct(state: RowState, productId: int): RowState {
    return match editableProduct(state.view, productId) {
        err(error) => state with { error: Some(RowError.Price(error)) },
        ok(selected) => state with { focus: Some(beginProduct(selected)), error: None },
    };
}

function beginProduct(selected: EditableProduct): ProductDraft {
    return {
        selected,
        stock: beginDraft<int, ParseError>(selected.onHand, formatInt),
        price: beginDraft<int, ParseError>(selected.costCents, formatCents),
        stockError: None,
        priceError: None,
    };
}

function parseCreation(draft: CreationDraft): NewProduct ! ParseError {
    const productId: int = draft.productId.value?;
    const supplierId: int = draft.supplierId.value?;
    const onHand: int = draft.stock.value?;
    const costCents: int = draft.price.value?;
    return ok({ productId, name: draft.name, supplierId, onHand, costCents });
}

function applyCreation(state: RowState): RowState {
    const creation: CreationDraft = state.creation with {
        productId: revealDraft<int, ParseError>(state.creation.productId),
        supplierId: revealDraft<int, ParseError>(state.creation.supplierId),
        stock: revealDraft<int, ParseError>(state.creation.stock),
        price: revealDraft<int, ParseError>(state.creation.price),
    };
    return match parseCreation(creation) {
        err(error) => state with { creation, error: None },
        ok(request) => match createProduct(state.view, request) {
            err(error) => state with { creation, error: Some(error) },
            ok(view) => acceptCreation(state, view, request.productId),
        },
    };
}

function acceptCreation(state: RowState, view: WorkbookView, productId: int): RowState {
    let nextId: int = productId;
    if (nextId < 2147483647) {
        nextId = nextId + 1;
    }
    const accepted: RowState = state with { view, nextId, creation: beginCreation(nextId), error: None };
    return selectProduct(accepted, productId);
}

function applyRemoval(state: RowState, productId: int): RowState {
    return match removeProduct(state.view, productId) {
        err(error) => state with { error: Some(error) },
        ok(view) => acceptRemoval(state, view, productId),
    };
}

function acceptRemoval(state: RowState, view: WorkbookView, productId: int): RowState {
    const focus: Option<ProductDraft> = match state.focus {
        None => None,
        Some(draft) => if draft.selected.productId == productId { None } else { Some(draft) },
    };
    return state with { view, focus, error: None };
}

function editExisting(state: RowState, event: EditorEvent): RowState {
    return match state.focus {
        None => state,
        Some(draft) => acceptExisting(state, reduceEditor(existingState(state.view, draft), event), needsValidation(event)),
    };
}

function needsValidation(event: EditorEvent): boolean {
    return match event {
        ApplyStock => true, ApplyPrice => true, Reset => true,
        Select(productId) => false, ChangeStock(text) => false, ChangePrice(text) => false,
        CancelStock => false, CancelPrice => false,
    };
}

function acceptExisting(state: RowState, editor: EditorState, check: boolean): RowState {
    if (check) {
        return match validateView(editor.view) {
            err(error) => state with { error: Some(error) },
            ok(view) => state with { view, focus: Some(draftFromEditor(editor)), error: None },
        };
    }
    return state with { view: editor.view, focus: Some(draftFromEditor(editor)) };
}

export function editLimitProbe(): int[] {
    const seed: RowState = initialRows() with { view: limitWorkbook() };
    const selected: RowState = reduceRows(seed, RowEvent.Select(100));
    const changed: RowState = reduceRows(selected, RowEvent.Existing(EditorEvent.ChangePrice("10000.00")));
    const refused: RowState = reduceRows(changed, RowEvent.Existing(EditorEvent.ApplyPrice));
    const flagged: int = match refused.error { None => 0, Some(error) => 1, };
    const accepted: int = match refused.focus { None => 0, Some(draft) => draft.price.accepted, };
    return [refused.view.totals.totalCents, flagged, accepted];
}

export function reduceRows(state: RowState, event: RowEvent): RowState {
    return match event {
        Select(productId) => selectProduct(state, productId),
        Existing(existingEvent) => editExisting(state, existingEvent),
        ChangeId(text) => state with {
            creation: state.creation with {
                productId: editDraft<int, ParseError>(state.creation.productId, text, parseIntText),
            },
            error: None,
        },
        ChangeName(text) => state with { creation: state.creation with { name: text }, error: None },
        ChangeSupplier(text) => state with {
            creation: state.creation with {
                supplierId: editDraft<int, ParseError>(state.creation.supplierId, text, parseIntText),
            },
            error: None,
        },
        ChangeStock(text) => state with {
            creation: state.creation with {
                stock: editDraft<int, ParseError>(state.creation.stock, text, parseIntText),
            },
            error: None,
        },
        ChangePrice(text) => state with {
            creation: state.creation with {
                price: editDraft<int, ParseError>(state.creation.price, text, parseCentsText),
            },
            error: None,
        },
        Create => applyCreation(state),
        CancelCreate => state with { creation: beginCreation(state.nextId), error: None },
        Remove(productId) => applyRemoval(state, productId),
        Reset => initialRows(),
    };
}
