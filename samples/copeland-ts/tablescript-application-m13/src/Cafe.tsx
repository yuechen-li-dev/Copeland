export type CopelandProfile = "JavaScript";

import { createElement } from "react";
import { CountField, PreviewForm } from "@copeland/cafe-controls";
import { ProductLine, StockError } from "./Application";
import { SupplierSummary } from "./Workbook";
import { EditorState, EditorEvent, initialEditor, reduceEditor } from "./Editor";
import { Map } from "./Collections";

function ProductRow(line: ProductLine, send: (event: EditorEvent) => void): View {
    return <tr key={line.productId} data-product={String.From(line.productId)}>
        <th scope="row">{line.productName}</th>
        <td>{line.supplierName}</td>
        <td data-field="onHand">{line.onHand}</td>
        <td data-field="orderUnits">{line.orderUnits}</td>
        <td data-field="orderCents">{line.orderCents}</td>
        <td><button onClick={capture { send, id: line.productId, units: line.onHand } () => send(EditorEvent.Select(id, units))}>Edit {line.productId}</button></td>
    </tr>;
}

function PresentProducts(state: EditorState, send: (event: EditorEvent) => void): View {
    const rows: View[] = Map<ProductLine, View>(state.view.products,
        capture { send } (line: ProductLine) => ProductRow(line, send));
    return <section className="panel" aria-label="Stock and purchase plan">
        <h2>Stock and purchase plan</h2>
        <p className="table-hint">Scroll the table horizontally to see all columns.</p>
        <div className="table-scroll" tabIndex={0} aria-label="Scrollable stock table"><table>
            <thead><tr><th>Product</th><th>Supplier</th><th>On hand</th><th>Order units</th><th>Order cents</th><th>Edit</th></tr></thead>
            <tbody>{rows}</tbody>
        </table></div>
    </section>;
}

function SupplierRow(line: SupplierSummary): View {
    return <li key={line.supplierId} data-supplier={String.From(line.supplierId)}>
        {line.supplierName}: {line.units} units / {line.totalCents} cents
    </li>;
}

function Totals(state: EditorState): View {
    const suppliers: View[] = Map<SupplierSummary, View>(state.view.suppliers, SupplierRow);
    return <section className="panel" aria-label="Purchase totals">
        <h2>Purchase totals</h2>
        <p><strong id="total-cents">{state.view.totals.totalCents}</strong> cents</p>
        <p><span id="total-units">{state.view.totals.units}</span> units / <span id="total-lines">{state.view.totals.lines}</span> orders</p>
        <ul id="supplier-totals">{suppliers}</ul>
    </section>;
}

function ErrorMessage(error: StockError): string {
    return match error {
        NegativeCount(productId, units) => `On hand for product ${productId} cannot be negative (received ${units}).`,
        UnknownProduct(productId) => `Product ${productId} does not exist in this stock snapshot.`,
    };
}

function EditPanel(state: EditorState, send: (event: EditorEvent) => void): View {
    const error: View[] = match state.error {
        Some(error) => [<p key="stock-error" id="edit-error" role="alert">{ErrorMessage(error)}</p>],
        None => [],
    };
    return <section className="panel" aria-label="Edit stock">
        <h2>Edit stock by product identity</h2>
        <PreviewForm onCommit={capture { send } () => send(EditorEvent.Apply)}>
        <CountField id="product-id" label="Product ID" value={state.productId} resetKey={state.draftGeneration}
            onValueChange={capture { send } (value: int) => send(EditorEvent.SetProductId(value))} />
        <CountField id="on-hand" label="On hand" value={state.units} resetKey={state.draftGeneration}
            onValueChange={capture { send } (value: int) => send(EditorEvent.SetUnits(value))} />
        {error}
        <button type="submit">Apply preview</button>
        <button type="button" onClick={capture { send } () => send(EditorEvent.Reset)}>Reset preview</button>
        </PreviewForm>
        <p className="note">This is a local scenario. Applying a preview does not publish inventory changes.</p>
    </section>;
}

function Page(state: EditorState, send: (event: EditorEvent) => void): View {
    stream Surface<0px, 0px> {
        width: fill;
        height: fit;
        gap: 16px;
        products: PresentProducts(state, send) { height: fit; }
        editor: EditPanel(state, send) { height: fit; }
        totals: Totals(state) { height: fit; }
    }
    return <main><header><p className="eyebrow">CAFÉ / TABLESCRIPT</p><h1>Purchasing preview</h1>
        <p>Immutable inputs, explicit calculations, typed application errors.</p></header>{Surface()}</main>;
}

export function Cafe(): View {
    state: EditorState = initialEditor();
    on Edit(event: EditorEvent) => reduceEditor(state, event);
    return Page(state, on.Edit);
}

