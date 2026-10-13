import { Action, NoticeKind, PresentNotice, PresentChoices } from "./UI";

record Item {
    id: int;
    label: string;
}

enum Event {
    Choose(id: int),
}

function Sender(storage: MutableArray<int>): (event: Event) => void {
    return capture { storage } (event: Event) => {
        storage[0] = match event { Choose(id) => id };
    };
}

function Identity(item: Item): string {
    return String.From(item.id);
}

function EmptyIdentity(item: Item): string {
    return "";
}

function Label(item: Item): string {
    return item.label;
}

function Selector(storage: MutableArray<int>): (item: Item) => Event {
    return capture { storage } (item: Item) => {
        storage[1] = storage[1] + 1;
        return Event.Choose(item.id);
    };
}

export function Valid(storage: MutableArray<int>): ReactNode {
    return PresentChoices<Item, Event>({
        label: "Probe choices",
        heading: "Choose a reading",
        items: [{ id: 41, label: "First reading" }, { id: 42, label: "Second reading" }],
        identity: Identity,
        text: Label,
        select: Selector(storage),
        selected: None,
        emptyMessage: "No readings.",
    }, Sender(storage));
}

export function Duplicate(storage: MutableArray<int>): ReactNode {
    return PresentChoices<Item, Event>({
        label: "Probe choices",
        heading: "Choose a reading",
        items: [{ id: 41, label: "First reading" }, { id: 41, label: "Second reading" }],
        identity: Identity,
        text: Label,
        select: Selector(storage),
        selected: None,
        emptyMessage: "No readings.",
    }, Sender(storage));
}

export function InvalidIdentity(storage: MutableArray<int>): ReactNode {
    return PresentChoices<Item, Event>({
        label: "Probe choices",
        heading: "Choose a reading",
        items: [{ id: 41, label: "First reading" }],
        identity: EmptyIdentity,
        text: Label,
        select: Selector(storage),
        selected: None,
        emptyMessage: "No readings.",
    }, Sender(storage));
}

export function Failure(storage: MutableArray<int>): ReactNode {
    return PresentNotice<Event>({
        kind: NoticeKind.Failure,
        message: "Reading unavailable.",
        action: Some(Action<Event>("Try again", Event.Choose(99))),
    }, Sender(storage));
}
