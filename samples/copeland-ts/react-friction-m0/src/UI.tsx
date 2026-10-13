import { createElement } from "react";
import { Input } from "@base-ui-components/react/input";
import { Map } from "./Collections";

// Data contracts contain meaning and typed application events. The projection
// below owns DOM semantics, callback adaptation, and the Base UI control.
export record ActionSpec<Event> {
    label: string;
    event: Event;
}

export function Action<Event>(label: string, event: Event): ActionSpec<Event> {
    return { label, event };
}

export enum NoticeKind {
    Pending,
    Information,
    Failure,
}

export record NoticeSpec<Event> {
    kind: NoticeKind;
    message: string;
    action: Option<ActionSpec<Event>>;
}

export record ChoiceListSpec<Item, Event> {
    label: string;
    heading: string;
    items: Item[];
    identity: (item: Item) => string;
    text: (item: Item) => string;
    select: (item: Item) => Event;
    selected: Option<string>;
    emptyMessage: string;
}

export record SearchFieldSpec<Event> {
    id: string;
    label: string;
    value: string;
    edit: (value: string) => Event;
}

export record PageHeaderSpec<Event> {
    title: string;
    search: SearchFieldSpec<Event>;
    actions: ActionSpec<Event>[];
}

export record ReadingSpec {
    id: string;
    eyebrow: string;
    title: string;
    summary: string;
    prose: string;
    proseRegion: string;
    linkLabel: string;
    linkTarget: string;
}

export function PresentAction<Event>(action: ActionSpec<Event>, send: (event: Event) => void): ReactNode {
    return <button key={action.label} onClick={capture { send, event: action.event } () => send(event)}>{action.label}</button>;
}

export function PresentNotice<Event>(notice: NoticeSpec<Event>, send: (event: Event) => void): ReactNode {
    const actions: ReactNode[] = match notice.action {
        Some(action) => [PresentAction<Event>(action, send)],
        None => [],
    };
    const role: string = match notice.kind {
        Pending => "status",
        Information => "status",
        Failure => "alert",
    };
    return <section className="panel" role={role}>{notice.message}{actions}</section>;
}

function ChoiceRow<Item, Event>(spec: ChoiceListSpec<Item, Event>, item: Item, identity: string, send: (event: Event) => void): ReactNode {
    const selected: string = match spec.selected {
        Some(current) => if current == identity { "true" } else { "false" },
        None => "false",
    };
    const label: (item: Item) => string = spec.text;
    const text: string = label(item);
    return <li key={identity}>
        <button data-choice={identity} aria-pressed={selected}
            onClick={capture { send, item, select: spec.select } () => send(select(item))}>{text}</button>
    </li>;
}

export function PresentChoices<Item, Event>(spec: ChoiceListSpec<Item, Event>, send: (event: Event) => void): ReactNode {
    if (spec.items.length == 0) {
        return PresentNotice<Event>({ kind: NoticeKind.Information, message: spec.emptyMessage, action: None }, send);
    }
    // Read each identity once. Invalid identities produce a visible diagnostic
    // before React sees an ambiguous key; no fallback to positional identity.
    const identities: string[] = Map<Item, string>(spec.items, spec.identity);
    for (let index: int = 0; index < identities.length; index = index + 1) {
        if (identities[index] == "") {
            return PresentNotice<Event>({ kind: NoticeKind.Failure, message: "Choice identities must be nonempty.", action: None }, send);
        }
        for (let previous: int = 0; previous < index; previous = previous + 1) {
            if (identities[index] == identities[previous]) {
                return PresentNotice<Event>({ kind: NoticeKind.Failure, message: "Choice identities must be unique.", action: None }, send);
            }
        }
    }
    const first: ReactNode = ChoiceRow<Item, Event>(spec, spec.items[0], identities[0], send);
    const rows: MutableArray<ReactNode> = MutableArray<ReactNode>(spec.items.length, first);
    for (let index: int = 1; index < spec.items.length; index = index + 1) {
        rows[index] = ChoiceRow<Item, Event>(spec, spec.items[index], identities[index], send);
    }
    const children: ReactNode[] = rows.freeze();
    return <section className="panel" aria-label={spec.label}>
        <h2>{spec.heading}</h2>
        <ul className="issue-list">{children}</ul>
    </section>;
}

export function PresentHeader<Event>(spec: PageHeaderSpec<Event>, send: (event: Event) => void): ReactNode {
    const actions: ReactNode[] = Map<ActionSpec<Event>, ReactNode>(spec.actions,
        capture { send } (action: ActionSpec<Event>) => PresentAction<Event>(action, send));
    return <header className="panel">
        <h1>{spec.title}</h1>
        <label htmlFor={spec.search.id}>{spec.search.label}</label>
        <Input id={spec.search.id} type="search" value={spec.search.value}
            onValueChange={capture { send, edit: spec.search.edit } (value: string) => send(edit(value))} />
        {actions}
    </header>;
}

export function PresentReading(spec: ReadingSpec): ReactNode {
    return <article className="panel reading" id={spec.id}>
        <p className="eyebrow">{spec.eyebrow}</p>
        <h2>{spec.title}</h2>
        <p className="readable">{spec.summary}</p>
        <p className="readable" data-prose={spec.proseRegion}>{spec.prose}</p>
        <a href={spec.linkTarget}>{spec.linkLabel}</a>
    </article>;
}

export function PageSurface(children: ReactNode[]): ReactNode {
    return <section>{children}</section>;
}
