export function ToggleMobileMenuEvent(): int {
    return 1;
}

export function CloseMobileMenuEvent(): int {
    return 2;
}

export function PrimaryCopiedEvent(): int {
    return 3;
}

export function PrimaryCopyFailedEvent(): int {
    return 4;
}

export function SecondaryCopiedEvent(): int {
    return 5;
}

export function SecondaryCopyFailedEvent(): int {
    return 6;
}

export function DesktopProfileChangedEvent(): int {
    return 10;
}

export function TabletProfileChangedEvent(): int {
    return 11;
}

export function MobileProfileChangedEvent(): int {
    return 12;
}

export function SelectSectionEvent(section: int): int {
    return 20 + section;
}

export function IsSectionEvent(event: int): boolean {
    return event >= 21 && event <= 27;
}

export function SectionFromEvent(event: int): int {
    return event - 20;
}
