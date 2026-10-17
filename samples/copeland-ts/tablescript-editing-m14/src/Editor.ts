export type CopelandProfile = "Copeland";

import { WorkbookView, StockError, initialWorkbook, applyStockEdit } from "./Application";
import { EditableProduct, PriceError, editableProduct, applyPriceEdit } from "./PriceApplication";
import { Draft, ParseError, beginDraft, editDraft, revealDraft, cancelDraft,
    parseIntText, parseCentsText, formatInt, formatCents } from "./Editing";

export record EditorState {
    view: WorkbookView;
    selected: EditableProduct;
    stock: Draft<int, ParseError>;
    price: Draft<int, ParseError>;
    stockError: Option<StockError>;
    priceError: Option<PriceError>;
}

export enum EditorEvent {
    Select(productId: int),
    ChangeStock(text: string),
    ChangePrice(text: string),
    ApplyStock,
    ApplyPrice,
    CancelStock,
    CancelPrice,
    Reset,
}

function selectProduct(view: WorkbookView, selected: EditableProduct): EditorState {
    return {
        view, selected,
        stock: beginDraft<int, ParseError>(selected.onHand, formatInt),
        price: beginDraft<int, ParseError>(selected.costCents, formatCents),
        stockError: None, priceError: None,
    };
}

export function initialEditor(): EditorState {
    const view: WorkbookView = initialWorkbook();
    return selectProduct(view, editableProduct(view, 100)!);
}

function applyStock(state: EditorState): EditorState {
    const stock: Draft<int, ParseError> = revealDraft<int, ParseError>(state.stock);
    return match stock.value {
        err(error) => state with { stock, stockError: None },
        ok(units) => match applyStockEdit(state.view, state.selected.productId, units) {
            err(error) => state with { stock, stockError: Some(error) },
            ok(view) => state with {
                view, selected: editableProduct(view, state.selected.productId)!,
                stock: beginDraft<int, ParseError>(units, formatInt), stockError: None,
            },
        },
    };
}

function applyPrice(state: EditorState): EditorState {
    const price: Draft<int, ParseError> = revealDraft<int, ParseError>(state.price);
    return match price.value {
        err(error) => state with { price, priceError: None },
        ok(cents) => match applyPriceEdit(state.view, state.selected.productId, cents) {
            err(error) => state with { price, priceError: Some(error) },
            ok(view) => state with {
                view, selected: editableProduct(view, state.selected.productId)!,
                price: beginDraft<int, ParseError>(cents, formatCents), priceError: None,
            },
        },
    };
}

export function reduceEditor(state: EditorState, event: EditorEvent): EditorState {
    return match event {
        Select(productId) => match editableProduct(state.view, productId) {
            ok(selected) => selectProduct(state.view, selected),
            err(error) => state with { priceError: Some(error) },
        },
        ChangeStock(text) => state with {
            stock: editDraft<int, ParseError>(state.stock, text, parseIntText), stockError: None,
        },
        ChangePrice(text) => state with {
            price: editDraft<int, ParseError>(state.price, text, parseCentsText), priceError: None,
        },
        ApplyStock => applyStock(state),
        ApplyPrice => applyPrice(state),
        CancelStock => state with { stock: cancelDraft<int, ParseError>(state.stock), stockError: None },
        CancelPrice => state with { price: cancelDraft<int, ParseError>(state.price), priceError: None },
        Reset => initialEditor(),
    };
}

// Shared C#/JS acceptance probe exercises the same reducer used by state/on.
export function editingProbe(): int[] {
    const initial: EditorState = initialEditor();
    const incomplete: EditorState = reduceEditor(initial, EditorEvent.ChangeStock("-"));
    const refused: EditorState = reduceEditor(incomplete, EditorEvent.ApplyStock);
    const stock: EditorState = reduceEditor(reduceEditor(refused, EditorEvent.ChangeStock("12")), EditorEvent.ApplyStock);
    const tea: EditorState = reduceEditor(stock, EditorEvent.Select(102));
    const price: EditorState = reduceEditor(reduceEditor(tea, EditorEvent.ChangePrice("5.00")), EditorEvent.ApplyPrice);
    const negative: EditorState = reduceEditor(reduceEditor(price, EditorEvent.ChangePrice("-0.01")), EditorEvent.ApplyPrice);
    const negativeFlag: int = match negative.priceError { Some(error) => 1, None => 0, };
    const cancelled: EditorState = reduceEditor(negative, EditorEvent.CancelPrice);
    const tooLarge: EditorState = reduceEditor(reduceEditor(price, EditorEvent.ChangePrice("10000.01")), EditorEvent.ApplyPrice);
    const limitFlag: int = match tooLarge.priceError { Some(error) => 1, None => 0, };
    const missingFlag: int = match applyPriceEdit(price.view, 999, 100) { ok(view) => 0, err(error) => 1, };
    const reset: EditorState = reduceEditor(negative, EditorEvent.Reset);
    return [initial.view.totals.totalCents, refused.view.totals.totalCents, stock.view.totals.totalCents,
        tea.selected.onHand, tea.selected.costCents, price.view.totals.totalCents,
        negative.view.totals.totalCents, negativeFlag, cancelled.price.accepted,
        tooLarge.view.totals.totalCents, limitFlag, missingFlag, reset.view.totals.totalCents];
}
