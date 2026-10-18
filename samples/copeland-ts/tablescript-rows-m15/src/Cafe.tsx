export type CopelandProfile = "JavaScript";

import { createElement } from "react";
import { TextField, PreviewForm } from "@copeland/react-controls";
import { ProductLine, StockError } from "./Application";
import { EditableProduct, PriceError, editableProduct } from "./PriceApplication";
import { SupplierSummary } from "./Workbook";
import { Draft, ParseError, isDirty, formatCents } from "./Editing";
import { EditorState, EditorEvent, initialEditor, reduceEditor } from "./ExistingEditor";
import { RowState, RowEvent, initialRows, reduceRows, existingState } from "./RowEditor";
import { RowError } from "./Rows";
import { Map } from "./Collections";

function ParseMessage(error: ParseError): string {
    return match error {
        Empty => "Enter a value.",
        Incomplete => "Finish entering the number.",
        InvalidCharacter(position) => `Use plain decimal digits; unexpected character at position ${position + 1}.`,
        FractionTooLong(places) => `Use at most ${places} decimal places. Values are never rounded.`,
        OutOfRange => "This value is outside the signed 32-bit range.",
    };
}

function ParseFeedback(draft: Draft<int, ParseError>): string {
    if (!draft.revealErrors) {
        return "";
    }
    return match draft.value { ok(value) => "", err(error) => ParseMessage(error), };
}

function StockMessage(error: StockError): string {
    return match error {
        NegativeCount(productId, units) => `On hand for product ${productId} cannot be negative (received ${units}).`,
        UnknownProduct(productId) => `Product ${productId} does not exist in this stock snapshot.`,
    };
}

function PriceMessage(error: PriceError): string {
    return match error {
        UnknownProduct(productId) => `Product ${productId} does not exist in this product snapshot.`,
        NegativePrice(productId, cents) => `Price for product ${productId} cannot be negative (received ${formatCents(cents)}).`,
        PriceTooLarge(productId, cents) => `Price for product ${productId} must be at most 10000.00 (received ${formatCents(cents)}).`,
    };
}

function Feedback(id: string, message: string): View[] {
    if (message == "") {
        return [];
    }
    return [<p key={id} id={id} role="alert">{message}</p>];
}

function DraftStatus(draft: Draft<int, ParseError>): string {
    if (isDirty<int, ParseError>(draft)) {
        return "Unapplied changes";
    }
    return "Matches accepted value";
}

function IsSelected(state: RowState, productId: int): boolean {
    return match state.focus { None => false, Some(draft) => draft.selected.productId == productId, };
}

function EmptyNotice(state: RowState): View[] {
    if (state.view.products.length > 0) { return []; }
    return [<p key="empty" id="empty-state">No products. Create a product to start a new plan.</p>];
}

function ExistingPanel(state: RowState, send: (event: RowEvent) => void): View {
    return match state.focus {
        None => <section className="panel"><h2>No product selected</h2><p id="no-selection">Choose Edit on a row, or create a product.</p></section>,
        Some(draft) => EditPanel(existingState(state.view, draft), capture { send } (event: EditorEvent) => send(RowEvent.Existing(event))),
    };
}

function RowMessage(error: RowError): string {
    return match error {
        InvalidId => "Product ID must be positive.",
        EmptyName => "Enter a product name.",
        NegativeStock => "Initial stock cannot be negative.",
        TotalTooLarge => "This change exceeds the purchase total limit. The accepted plan has been kept.",
        Price(error) => PriceMessage(error),
        Calculation(error) => match error {
            Unresolved(relation) => `The plan is missing a required relationship: ${relation}.`,
            NonUnique(relation) => `The plan needs a unique relationship: ${relation}.`,
        },
        Integrity(error) => match error {
            DuplicateKey(tableName, keyValue) => `${tableName} already contains key ${keyValue}.`,
            NotFound(tableName, keyValue) => `${tableName} does not contain key ${keyValue}.`,
            ConflictingEdit(tableName, keyValue) => `More than one change targets ${tableName} key ${keyValue}.`,
            MissingReference(tableName, columnName, keyValue) => `${tableName}.${columnName} references missing key ${keyValue}.`,
            RowLimit(tableName) => `${tableName} reached the preview row limit.`,
        },
    };
}

function CreationField(id: string, label: string, draft: Draft<int, ParseError>, mode: string, change: (text: string) => void): View {
    const message: string = ParseFeedback(draft);
    return <div>
        <TextField id={id} label={label} value={draft.text} invalid={message != ""}
            describedBy={id + "-feedback"} inputMode={mode} onValueChange={change} />
        <div id={id + "-feedback"}>{Feedback(id + "-parse-error", message)}</div>
    </div>;
}

function CreationPanel(state: RowState, send: (event: RowEvent) => void): View {
    const error: View[] = match state.error { None => [], Some(error) => Feedback("row-error", RowMessage(error)), };
    return <section className="panel" aria-label="Create product">
        <h2>Create a product</h2>
        <p id="create-help" className="hint">Product IDs are unique. Supplier IDs: 10 North Roastery, 20 Leaf House, 30 Pantry Co. New products reorder below 8 units, up to 24 units.</p>
        <PreviewForm onCommit={capture { send } () => send(RowEvent.Create)}>
            <div className="creation-fields">
                {CreationField("new-id", "New product ID", state.creation.productId, "numeric", capture { send } (text: string) => send(RowEvent.ChangeId(text)))}
                <TextField id="new-name" label="Product name" value={state.creation.name} invalid={false}
                    describedBy="create-help row-feedback" inputMode="text"
                    onValueChange={capture { send } (text: string) => send(RowEvent.ChangeName(text))} />
                {CreationField("new-supplier", "Supplier ID", state.creation.supplierId, "numeric", capture { send } (text: string) => send(RowEvent.ChangeSupplier(text)))}
                {CreationField("new-stock", "Initial stock", state.creation.stock, "numeric", capture { send } (text: string) => send(RowEvent.ChangeStock(text)))}
                {CreationField("new-price", "New unit price", state.creation.price, "decimal", capture { send } (text: string) => send(RowEvent.ChangePrice(text)))}
            </div>
            <div id="row-feedback">{error}</div>
            <button type="submit">Create product</button>
            <button type="button" onClick={capture { send } () => send(RowEvent.CancelCreate)}>Cancel creation</button>
            <button type="button" onClick={capture { send } () => send(RowEvent.Reset)}>Reset preview</button>
        </PreviewForm>
        <p className="note">Local preview. Products and stock change together; nothing is published to inventory.</p>
    </section>;
}

function StockEditor(state: EditorState, send: (event: EditorEvent) => void): View {
    const parse: string = ParseFeedback(state.stock);
    const domain: string = match state.stockError { Some(error) => StockMessage(error), None => "", };
    return <PreviewForm onCommit={capture { send } () => send(EditorEvent.ApplyStock)}>
        <h3>Stock count</h3>
        <TextField id="on-hand" label="On hand" value={state.stock.text} invalid={parse != "" || domain != ""}
            describedBy="stock-help stock-feedback" inputMode="numeric"
            onValueChange={capture { send } (text: string) => send(EditorEvent.ChangeStock(text))} />
        <p id="stock-help" className="hint">Whole units. Apply updates the purchase preview.</p>
        <div id="stock-feedback">{Feedback("stock-parse-error", parse)}{Feedback("stock-domain-error", domain)}</div>
        <p id="stock-status" className="hint">{DraftStatus(state.stock)}</p>
        <button type="submit">Apply stock</button>
        <button type="button" onClick={capture { send } () => send(EditorEvent.CancelStock)}>Cancel stock</button>
    </PreviewForm>;
}

function PriceEditor(state: EditorState, send: (event: EditorEvent) => void): View {
    const parse: string = ParseFeedback(state.price);
    const domain: string = match state.priceError { Some(error) => PriceMessage(error), None => "", };
    return <PreviewForm onCommit={capture { send } () => send(EditorEvent.ApplyPrice)}>
        <h3>Unit price</h3>
        <TextField id="unit-price" label="Unit price" value={state.price.text} invalid={parse != "" || domain != ""}
            describedBy="price-help price-feedback" inputMode="decimal"
            onValueChange={capture { send } (text: string) => send(EditorEvent.ChangePrice(text))} />
        <p id="price-help" className="hint">Decimal price, up to two places. No rounding. Maximum 10000.00.</p>
        <div id="price-feedback">{Feedback("price-parse-error", parse)}{Feedback("price-domain-error", domain)}</div>
        <p id="price-status" className="hint">{DraftStatus(state.price)}</p>
        <button type="submit">Apply price</button>
        <button type="button" onClick={capture { send } () => send(EditorEvent.CancelPrice)}>Cancel price</button>
    </PreviewForm>;
}

function EditPanel(state: EditorState, send: (event: EditorEvent) => void): View {
    return <section className="panel" aria-label="Edit product">
        <h2 id="selected-product">{state.selected.productName} · {state.selected.productId}</h2>
        <p className="hint">Selecting a product discards unapplied changes in both fields.</p>
        <div className="editors">{StockEditor(state, send)}{PriceEditor(state, send)}</div>
        <p className="note">Local scenario only. Applying a preview does not publish inventory or price changes.</p>
    </section>;
}

function ProductRow(line: ProductLine, state: RowState, send: (event: RowEvent) => void): View {
    const product: EditableProduct = editableProduct(state.view, line.productId)!;
    return <tr key={line.productId} data-product={String.From(line.productId)} data-selected={String.From(IsSelected(state, line.productId))}>
        <th scope="row">{line.productName}</th>
        <td data-field="onHand">{line.onHand}</td>
        <td data-field="price">{formatCents(product.costCents)}</td>
        <td data-field="orderUnits">{line.orderUnits}</td>
        <td data-field="orderCents">{line.orderCents}</td>
        <td><button onClick={capture { send, id: line.productId } () => send(RowEvent.Select(id))}>Edit {line.productId}</button>
            <button onClick={capture { send, id: line.productId } () => send(RowEvent.Remove(id))}>Remove {line.productId}</button></td>
    </tr>;
}

function PresentProducts(state: RowState, send: (event: RowEvent) => void): View {
    if (state.view.products.length == 0) {
        return <section className="panel" aria-label="Stock and purchase plan">
            <h2>Stock and purchase plan · <span id="row-count">0</span> products</h2>
            {EmptyNotice(state)}
        </section>;
    }
    const rows: View[] = Map<ProductLine, View>(state.view.products,
        capture { state, send } (line: ProductLine) => ProductRow(line, state, send));
    return <section className="panel" aria-label="Stock and purchase plan">
        <h2>Stock and purchase plan · <span id="row-count">{state.view.products.length}</span> products</h2>
        {EmptyNotice(state)}
        <p className="table-hint">Scroll the table horizontally to see all columns.</p>
        <div className="table-scroll" tabIndex={0} aria-label="Scrollable stock table"><table>
            <thead><tr><th>Product</th><th>On hand</th><th>Unit price</th><th>Order units</th><th>Order cents</th><th>Edit</th></tr></thead>
            <tbody>{rows}</tbody>
        </table></div>
    </section>;
}

function SupplierRow(line: SupplierSummary): View {
    return <li key={line.supplierId} data-supplier={String.From(line.supplierId)}>
        {line.supplierName}: {line.units} units / {line.totalCents} cents
    </li>;
}

function Totals(state: RowState): View {
    const suppliers: View[] = Map<SupplierSummary, View>(state.view.suppliers, SupplierRow);
    return <section className="panel" aria-label="Purchase totals">
        <h2>Purchase totals</h2>
        <p><strong id="total-cents">{state.view.totals.totalCents}</strong> cents</p>
        <p><span id="total-units">{state.view.totals.units}</span> units / <span id="total-lines">{state.view.totals.lines}</span> orders</p>
        <ul id="supplier-totals">{suppliers}</ul>
    </section>;
}

function Page(state: RowState, send: (event: RowEvent) => void): View {
    stream Surface<0px, 0px> {
        width: fill;
        height: fit;
        gap: 16px;
        creation: CreationPanel(state, send) { height: fit; }
        editor: ExistingPanel(state, send) { height: fit; }
        products: PresentProducts(state, send) { height: fit; }
        totals: Totals(state) { height: fit; }
    }
    return <main><header><p className="eyebrow">CAFÉ / TABLESCRIPT</p><h1>Purchasing preview</h1>
        <p>Edit stock and unit prices, then apply each change to your local plan.</p></header>{Surface()}</main>;
}

export function Cafe(): View {
    state: RowState = initialRows();
    on Edit(event: RowEvent) => reduceRows(state, event);
    return Page(state, on.Edit);
}
