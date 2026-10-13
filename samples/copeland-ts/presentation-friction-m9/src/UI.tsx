import { SearchField } from "@copeland/presentation-controls";
import { ArrangementToggle } from "@copeland/presentation-controls";
import { Map } from "./Collections";
import type { Projection } from "./Collections";

export type EventSink<Event> = (event: Event) => void;
type ChildViews = View[];

// Data contracts contain meaning and typed application events. The projection
// below owns DOM semantics, callback adaptation, and imported framework controls.
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
    identity: Projection<Item, string>;
    text: Projection<Item, string>;
    select: Projection<Item, Event>;
    selected: Option<string>;
    emptyMessage: string;
}

export record SearchFieldSpec<Event> {
    id: string;
    label: string;
    value: string;
    edit: Projection<string, Event>;
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

export function PresentAction<Event>(action: ActionSpec<Event>, send: EventSink<Event>): View {
    return <button key={action.label} onClick={capture { send, event: action.event } () => send(event)}>{action.label}</button>;
}

export function PresentNotice<Event>(notice: NoticeSpec<Event>, send: EventSink<Event>): View {
    const actions: ChildViews = match notice.action {
        Some(action) => [PresentAction<Event>(action, send)],
        None => [],
    };
    const role: string = match notice.kind {
        Pending => "status",
        Information => "status",
        Failure => "alert",
    };
    return <section role={role}>{notice.message}{actions}</section>;
}

function ChoiceRow<Item, Event>(spec: ChoiceListSpec<Item, Event>, item: Item, identity: string, send: EventSink<Event>): View {
    const selected: string = match spec.selected {
        Some(current) => if current == identity { "true" } else { "false" },
        None => "false",
    };
    const text: string = spec.text(item);
    return <li key={identity}>
        <button data-choice={identity} aria-pressed={selected}
            onClick={capture { send, item, select: spec.select } () => send(select(item))}>{text}</button>
    </li>;
}

export function PresentChoices<Item, Event>(spec: ChoiceListSpec<Item, Event>, send: EventSink<Event>): View {
    if (spec.items.length == 0) {
        return PresentNotice<Event>({ kind: NoticeKind.Information, message: spec.emptyMessage, action: None }, send);
    }
    // Read each identity once. Invalid identities produce a visible diagnostic
    // before the renderer sees an ambiguous key; no fallback to positional identity.
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
    const first: View = ChoiceRow<Item, Event>(spec, spec.items[0], identities[0], send);
    const rows: MutableArray<View> = MutableArray<View>(spec.items.length, first);
    for (let index: int = 1; index < spec.items.length; index = index + 1) {
        rows[index] = ChoiceRow<Item, Event>(spec, spec.items[index], identities[index], send);
    }
    const children: ChildViews = rows.freeze();
    return <section aria-label={spec.label}>
        <h2>{spec.heading}</h2>
        <ul class="issue-list">{children}</ul>
    </section>;
}

export function PresentHeader<Event>(spec: PageHeaderSpec<Event>, send: EventSink<Event>): View {
    const actions: ChildViews = Map<ActionSpec<Event>, View>(spec.actions,
        capture { send } (action: ActionSpec<Event>) => PresentAction<Event>(action, send));
    return <header class="panel">
        <h1>{spec.title}</h1>
        <label for={spec.search.id}>{spec.search.label}</label>
        <SearchField id={spec.search.id} type="search" value={spec.search.value}
            onValueChange={capture { send, edit: spec.search.edit } (value: string) => send(edit(value))} />
        {actions}
    </header>;
}

export function PresentReading(spec: ReadingSpec): View {
    return <article id={spec.id}>
        <p class="eyebrow">{spec.eyebrow}</p>
        <h2>{spec.title}</h2>
        <p class="readable">{spec.summary}</p>
        <p class="readable" data-prose={spec.proseRegion}>{spec.prose}</p>
        <a href={spec.linkTarget}>{spec.linkLabel}</a>
    </article>;
}

export function PageSurface(header: View, arrangement: View, body: View): View {
    return <section>{header}{arrangement}{body}</section>;
}

export function PresentArrangement<Event>(pressed: boolean, event: Event, send: EventSink<Event>): View {
    return <ArrangementToggle value={pressed}
        onValueChange={capture { event, send } (value: boolean) => send(event)}>Change arrangement</ArrangementToggle>;
}
