export function DesktopProfile(): int {
    return 3;
}

export function TabletProfile(): int {
    return 2;
}

export function MobileProfile(): int {
    return 1;
}

// The one profile law for the site: widths below 600 are Mobile, widths from
// 600 through 1023 are Tablet, and widths from 1024 onward are Desktop.
export function ClassifyViewport(width: int): int {
    if (width < 600) {
        return MobileProfile();
    }

    if (width < 1024) {
        return TabletProfile();
    }

    return DesktopProfile();
}

export function IsMobileProfile(profile: int): boolean {
    return profile == MobileProfile();
}
